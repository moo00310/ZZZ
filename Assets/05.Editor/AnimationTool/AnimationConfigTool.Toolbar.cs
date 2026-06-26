using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ;
using ZZZ.Player.StateMachine;

namespace ZZZ.Editor.AnimationTool
{
    public partial class AnimationConfigTool
    {
        // ── Toolbar ──────────────────────────────────────────────
        private void DrawToolbar()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, ToolbarH), new Color(0.2f, 0.2f, 0.2f));
            GUILayout.BeginArea(new Rect(4, 0, position.width - 8, ToolbarH));
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Target", GUILayout.Width(44));
            var pt = _target;
            _target = (GameObject)EditorGUILayout.ObjectField(_target, typeof(GameObject), true, GUILayout.Width(160));
            if (_target != pt)
            {
                ExitPreview(); _trackTime = 0f; AutoDetectRootBones();
                _poseBones = null;   // 본 캐시 무효화
                if (_target != null) _targetOriginPos = _target.transform.position;
            }

            GUILayout.Label("Config", GUILayout.Width(44));
            var pc = _config;
            _config = (AnimationConfig)EditorGUILayout.ObjectField(_config, typeof(AnimationConfig), false, GUILayout.Width(160));
            if (_config != pc)
            {
                ExitPreview(); _trackTime = 0f;
                _selectedClip = -1; _selectedNotify = -1;
                _serializedConfig = _config != null ? new SerializedObject(_config) : null;
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("New", GUILayout.Width(44)))
            {
                string path = EditorUtility.SaveFilePanelInProject("Create AnimationConfig",
                    "AnimationConfig", "asset", "저장 위치 선택");
                if (!string.IsNullOrEmpty(path))
                {
                    var a = CreateInstance<AnimationConfig>();
                    AssetDatabase.CreateAsset(a, path);
                    AssetDatabase.SaveAssets();
                    _config = a;
                    _serializedConfig = new SerializedObject(_config);
                    _trackTime = 0f;
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
        // ── Playbar ──────────────────────────────────────────────
        private void DrawPlaybar(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.22f, 0.22f, 0.22f));
            GUILayout.BeginArea(area);

            // ── 1행: 재생 컨트롤 ─────────────────────────────────
            EditorGUILayout.BeginHorizontal();

            string lbl = _isPlaying ? "■  Stop" : "▶  Play";
            if (GUILayout.Button(lbl, GUILayout.Height(24f), GUILayout.Width(72f)))
            {
                if (_isPlaying) StopPreview();
                else StartPreview();
            }
            if (GUILayout.Button("|◀", GUILayout.Height(24f), GUILayout.Width(28f)))
                ResetPreview();

            if (_comboMode)
            {
                GUILayout.Label($"▶ {(_comboActiveClip >= 0 && _comboActiveClip < _config?.Clips.Count ? SectionLabel(_comboActiveClip) : "-")}",
                    GUILayout.Width(150));
                GUILayout.Label(CurrentFrameLabel(), EditorStyles.boldLabel, GUILayout.Width(64));
            }
            else
                GUILayout.Label($"{_trackTime:F2}s / {GetTotalDuration():F2}s   {CurrentFrameLabel()}",
                    GUILayout.Width(170));

            GUILayout.Label("Speed", GUILayout.Width(38));
            _previewSpeed = EditorGUILayout.Slider(_previewSpeed, 0.1f, 2f, GUILayout.Width(90));
            GUILayout.Label("Scale", GUILayout.Width(36));
            _pxPerSec = EditorGUILayout.Slider(_pxPerSec, 20f, 250f, GUILayout.Width(90));

            EditorGUILayout.EndHorizontal();

            // ── 2행: 모드 + 콤보 입력 ────────────────────────────
            EditorGUILayout.BeginHorizontal();

            bool prevMode = _comboMode;
            _comboMode = GUILayout.Toggle(_comboMode, "Combo (Links)", "Button", GUILayout.Width(100f));
            if (_comboMode != prevMode) ResetPreview();

            if (_comboMode)
            {
                // 공격 입력은 한 번에 하나만 누름 → 토글 줄 대신 단일 드롭다운 (Move처럼 접음)
                GUILayout.Label("Attack", GUILayout.Width(44));
                ComboInput curAtk = CurrentHeldAttack();
                int curIdx = Mathf.Max(0, System.Array.IndexOf(s_attackInputs, curAtk));
                var prevBg = GUI.backgroundColor;
                if (curAtk != ComboInput.None) GUI.backgroundColor = InputColor(curAtk);   // 눌린 입력 색 표시
                int newIdx = EditorGUILayout.Popup(curIdx, s_attackInputLabels, GUILayout.Width(90));
                GUI.backgroundColor = prevBg;
                if (newIdx != curIdx) SetHeldAttack(s_attackInputs[newIdx]);

                GUILayout.Label("Move", GUILayout.Width(36));
                _simMoveDir = (MoveDir)EditorGUILayout.EnumPopup(_simMoveDir, GUILayout.Width(72));

                DrawLoopToggle();
                GUILayout.Label(_comboLog, EditorStyles.miniLabel);
            }
            else
            {
                DrawLoopToggle();
                GUILayout.Label("순차 재생 — 트랙 순서대로", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawLoopToggle()
        {
            if (_config == null) return;
            var prevBg = GUI.backgroundColor;
            if (_config.LoopTrack) GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
            bool loop = GUILayout.Toggle(_config.LoopTrack, "↻ Loop", "Button", GUILayout.Width(60f));
            GUI.backgroundColor = prevBg;
            if (loop != _config.LoopTrack)
            {
                Undo.RecordObject(_config, "Toggle Loop Track");
                _config.LoopTrack = loop;
                EditorUtility.SetDirty(_config);
            }
        }

        // 콤보 프리뷰에서 선택 가능한 공격 입력 (None = 아무것도 안 누름). 한 번에 하나만.
        private static readonly ComboInput[] s_attackInputs =
            { ComboInput.None, ComboInput.Normal, ComboInput.Strong, ComboInput.Enhance, ComboInput.Dodge };
        private static readonly string[]     s_attackInputLabels =
            { "None", "Normal", "Strong", "Enhance", "Dodge" };

        // 현재 눌러둔(held) 공격 입력 — 없으면 None. (_heldInput에서 역으로 읽음)
        private ComboInput CurrentHeldAttack()
        {
            foreach (var ci in s_attackInputs)
                if (ci != ComboInput.None && _heldInput[(int)ci]) return ci;
            return ComboInput.None;
        }

        // 드롭다운 선택 반영 — 하나만 held로 두고 나머지는 해제 (None이면 전부 해제)
        private void SetHeldAttack(ComboInput ci)
        {
            System.Array.Clear(_heldInput, 0, _heldInput.Length);
            if (ci != ComboInput.None) _heldInput[(int)ci] = true;
        }

        // 현재 playhead가 올라간 클립의 로컬 프레임 표시 ("21/68f"). 콤보/순차 모드 모두 지원.
        private string CurrentFrameLabel()
        {
            if (_config == null || _config.Clips.Count == 0) return "";

            // 콤보 모드 — 현재 재생 중인 클립(_comboActiveClip)의 로컬 시간(_comboClipTime)
            if (_comboMode)
            {
                if (_comboActiveClip < 0 || _comboActiveClip >= _config.Clips.Count) return "";
                var c = _config.Clips[_comboActiveClip];
                if (c.Clip == null || c.Clip.frameRate <= 0f) return "";
                float ct    = c.IsLooping ? Mathf.Repeat(_comboClipTime, c.Clip.length) : _comboClipTime;
                int   total = Mathf.Max(1, Mathf.RoundToInt(c.Clip.length * c.Clip.frameRate));
                int   frame = Mathf.Clamp(Mathf.RoundToInt(ct * c.Clip.frameRate), 0, total);
                return $"{frame}/{total}f";
            }

            // 순차 모드 — _trackTime이 올라간 클립을 찾아 로컬 프레임 계산 (SampleAtTime과 동일 규칙)
            float t = 0f;
            for (int i = 0; i < _config.Clips.Count; i++)
            {
                var tc = _config.Clips[i];
                if (tc.Clip == null) continue;
                float dur = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
                if (_trackTime <= t + dur || i == _config.Clips.Count - 1)
                {
                    float clipTime = Mathf.Clamp(_trackTime - t, 0f, dur) * tc.Speed;
                    if (tc.IsLooping) clipTime = Mathf.Repeat(clipTime, tc.Clip.length);
                    if (tc.Clip.frameRate <= 0f) return "";
                    int total = Mathf.Max(1, Mathf.RoundToInt(tc.Clip.length * tc.Clip.frameRate));
                    int frame = Mathf.Clamp(Mathf.RoundToInt(clipTime * tc.Clip.frameRate), 0, total);
                    return $"{frame}/{total}f";
                }
                t += dur;
            }
            return "";
        }
    }
}
