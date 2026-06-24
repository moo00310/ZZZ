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
                GUILayout.Label($"▶ {(_comboActiveClip >= 0 && _comboActiveClip < _config?.Clips.Count ? SectionLabel(_comboActiveClip) : "-")}",
                    GUILayout.Width(150));
            else
                GUILayout.Label($"{_trackTime:F2}s / {GetTotalDuration():F2}s", GUILayout.Width(90));

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
                InputToggle(ComboInput.Normal,   "Normal",   64);
                InputToggle(ComboInput.Enhanced, "Enhanced", 72);
                InputToggle(ComboInput.Attack_Normal_Enhance, "Enhance", 64);
                InputToggle(ComboInput.Dodge,    "Dodge",    56);
                if (GUILayout.Button("Clear", GUILayout.Width(48)))
                    System.Array.Clear(_heldInput, 0, _heldInput.Length);

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

        // 눌러두면 유지되는 입력 토글
        private void InputToggle(ComboInput input, string label, float width)
        {
            bool held = _heldInput[(int)input];
            var prevBg = GUI.backgroundColor;
            if (held) GUI.backgroundColor = InputColor(input);
            bool next = GUILayout.Toggle(held, label, "Button", GUILayout.Width(width));
            GUI.backgroundColor = prevBg;
            _heldInput[(int)input] = next;
        }
    }
}
