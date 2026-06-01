using System;
using UnityEditor;
using UnityEngine;
using ZZZ;

namespace ZZZ.Editor.AnimationTool
{
    public class AnimationConfigTool : EditorWindow
    {
        // ── 기본 ─────────────────────────────────────────────────
        private GameObject      _target;
        private AnimationConfig _config;
        private SerializedObject _serializedConfig;

        // ── Preview ───────────────────────────────────────────────
        private bool   _isPlaying;
        private float  _trackTime;
        private double _lastEditorTime;
        private float  _previewSpeed = 1f;

        // ── Combo Preview (링크 따라가기) ─────────────────────────
        private bool   _comboMode;        // true = 링크 흐름 재생
        private int    _comboActiveClip;  // 현재 재생 중 클립 인덱스
        private float  _comboClipTime;    // 현재 클립 로컬 시간(초, 스피드 적용 후)
        private bool[] _heldInput = new bool[5];  // 토글로 눌러둔 입력 (ComboInput 인덱스)
        private string _comboLog = "";    // 전이 흐름 로그

        // ── Transition 블렌딩 (CrossFade 시뮬레이션) ──────────────
        private bool          _blending;
        private AnimationClip _blendFromClip;
        private float         _blendFromTime;   // 이전 클립 로컬 시간(초)
        private float         _blendElapsed;
        private float         _blendDuration;
        private Transform[]   _poseBones;        // 보간 대상 본 캐시
        private Vector3[]     _poseAPos;
        private Quaternion[]  _poseARot;

        // ── 레이아웃 상수 ─────────────────────────────────────────
        private const float ToolbarH  = 28f;
        private const float PlaybarH  = 58f;
        private const float RulerH    = 22f;
        private const float ClipH     = 58f;
        private const float ClipGap   = 3f;
        private const float LabelW    = 144f;
        private const float InspW     = 250f;   // 우측 인스펙터 폭

        // ── 타임라인 ─────────────────────────────────────────────
        private float _pxPerSec = 80f;
        private float _scrollX  = 0f;

        // ── 선택 ─────────────────────────────────────────────────
        private int _selectedClip   = -1;
        private int _selectedNotify = -1;
        private int _notifyClipIdx  = -1;

        // ── 드래그: Notify ────────────────────────────────────────
        private bool _draggingNotify;
        private int  _dragNotifyClip;
        private int  _dragNotifyIdx;

        // ── 드래그: Playhead ──────────────────────────────────────
        private bool _draggingPlayhead;

        // ── 드래그: Clip 순서 변경 ────────────────────────────────
        private int  _reorderingClip   = -1;
        private int  _reorderTargetIdx = -1;

        // ── 루트 모션 (PlayerController와 동일한 본 기반 방식) ─────
        private Transform _rootBone;            // 이동량 추출 본
        private Transform _bip001Bone;          // 메시 드리프트 방지용 XZ 리셋 본
        private float     _rootMotionScale = 1f;

        private int     _rmPrevClipIdx  = -1;   // 직전 샘플이 속한 클립
        private float   _rmPrevClipTime = 0f;   // 직전 샘플 클립 로컬 시간
        private Vector3 _rmPrevLocalPos;        // 직전 루트본 로컬 위치
        private bool    _rmHasPrev;             // 델타 계산용 prev 유효 여부
        private Vector3 _targetOriginPos;       // 재생 시작 시 target 위치 (리셋용)

        private Vector2 _inspScroll;

        // ── Notify 색상 ───────────────────────────────────────────
        private static readonly Color[] NotifyColors =
        {
            new Color(1.0f, 0.45f, 0.10f),
            new Color(0.2f, 0.75f, 1.00f),
            new Color(0.2f, 0.90f, 0.40f),
            new Color(0.9f, 0.30f, 0.90f),
        };

        [MenuItem("ZZZ/Animation Config Tool")]
        public static void Open()
        {
            var w = GetWindow<AnimationConfigTool>("Anim Config Tool");
            w.minSize = new Vector2(640f, 480f);
        }

        private void OnEnable()  => EditorApplication.update += OnEditorUpdate;
        private void OnDisable() { EditorApplication.update -= OnEditorUpdate; ExitPreview(); }

        // ── Editor Update ────────────────────────────────────────
        private void OnEditorUpdate()
        {
            if (!_isPlaying || _config == null) return;
            double now   = EditorApplication.timeSinceStartup;
            float  delta = (float)(now - _lastEditorTime) * _previewSpeed;
            _lastEditorTime = now;

            if (_comboMode) ComboUpdate(delta);
            else            SequentialUpdate(delta);

            Repaint();
        }

        private void SequentialUpdate(float delta)
        {
            _trackTime += delta;
            float total = GetTotalDuration();
            if (_trackTime >= total)
            {
                if (_config.LoopTrack)
                {
                    // 처음으로 되돌리고 위치를 원점(시작 위치)으로 복귀
                    _trackTime     = 0f;
                    _rmPrevClipIdx = -1;
                    _rmHasPrev     = false;
                    if (_target != null) _target.transform.position = _targetOriginPos;
                }
                else { _trackTime = total; _isPlaying = false; }
            }
            SampleAtTime(_trackTime, true);
        }

        // 링크 흐름 재생: 현재 클립을 재생하다 윈도우 안에서 입력이 들어오면 타겟으로 점프
        private void ComboUpdate(float delta)
        {
            if (_comboActiveClip < 0 || _comboActiveClip >= _config.Clips.Count)
            { _isPlaying = false; return; }

            var tc = _config.Clips[_comboActiveClip];
            if (tc.Clip == null) { _isPlaying = false; return; }

            float clipLen = tc.Clip.length;
            _comboClipTime += delta * Mathf.Max(0.01f, tc.Speed);
            float nt = clipLen > 0f ? _comboClipTime / clipLen : 1f;

            // 블렌딩 진행
            if (_blending)
            {
                _blendElapsed += delta;
                if (_blendElapsed >= _blendDuration) _blending = false;
            }

            // 눌러둔 입력이 매칭 링크 윈도우 안이면 → 점프
            foreach (var link in tc.Links)
            {
                if (!IsInputHeld(link.Input)) continue;
                if (nt >= link.WindowStart && nt <= link.WindowEnd)
                {
                    JumpToLink(link);
                    return;
                }
            }

            // 클립 끝 도달
            if (nt >= 1f)
            {
                if (tc.Loop) { _comboClipTime = Mathf.Repeat(_comboClipTime, clipLen); }
                else if (_config.LoopTrack)
                {
                    _comboLog += "  → [Loop]";
                    RestartCombo();
                    return;
                }
                else
                {
                    _comboLog += "  → [End]";
                    _isPlaying = false;
                    _comboClipTime = clipLen;
                }
            }

            float ct = Mathf.Clamp(_comboClipTime, 0f, clipLen);
            if (_blending && _blendFromClip != null && _blendDuration > 0.0001f)
            {
                float w = Mathf.Clamp01(_blendElapsed / _blendDuration);
                SampleBlended(_blendFromClip, _blendFromTime, tc.Clip, ct, w);
            }
            else
            {
                SampleClipPose(tc, _comboActiveClip, ct, true);
            }
        }

        // 이전 클립(from) 포즈와 새 클립(to) 포즈를 본 단위로 보간 (CrossFade 시뮬)
        private void SampleBlended(AnimationClip fromClip, float fromTime,
            AnimationClip toClip, float toTime, float w)
        {
            if (_target == null) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            CachePoseBones();

            // A 포즈 (이전 클립) 캡처
            AnimationMode.SampleAnimationClip(_target, fromClip, fromTime);
            for (int i = 0; i < _poseBones.Length; i++)
            {
                _poseAPos[i] = _poseBones[i].localPosition;
                _poseARot[i] = _poseBones[i].localRotation;
            }

            // B 포즈 (새 클립) → A에서 B로 w만큼 보간
            AnimationMode.SampleAnimationClip(_target, toClip, toTime);
            for (int i = 0; i < _poseBones.Length; i++)
            {
                _poseBones[i].localPosition =
                    Vector3.Lerp(_poseAPos[i], _poseBones[i].localPosition, w);
                _poseBones[i].localRotation =
                    Quaternion.Slerp(_poseARot[i], _poseBones[i].localRotation, w);
            }
            SceneView.RepaintAll();
        }

        private void CachePoseBones()
        {
            if (_poseBones != null && _poseBones.Length > 0 &&
                _poseBones[0] != null) return;
            _poseBones = _target.GetComponentsInChildren<Transform>(true);
            _poseAPos  = new Vector3[_poseBones.Length];
            _poseARot  = new Quaternion[_poseBones.Length];
        }

        // 콤보를 Entry 섹션으로 되돌리고 위치를 원점으로 복귀
        private void RestartCombo()
        {
            int entry = _config.IndexOfSection(_config.EntrySection);
            _comboActiveClip = entry >= 0 ? entry : 0;
            _comboClipTime   = 0f;
            _rmHasPrev       = false;
            _rmPrevClipIdx   = _comboActiveClip;
            _blending        = false;
            if (_target != null) _target.transform.position = _targetOriginPos;
            _comboLog += $" → {SectionLabel(_comboActiveClip)}";
        }

        // Any 링크는 아무 입력이나 눌려있으면 활성
        private bool IsInputHeld(ComboInput input)
        {
            if (input == ComboInput.Any)
            {
                for (int i = 0; i < _heldInput.Length; i++)
                    if (_heldInput[i]) return true;
                return false;
            }
            return _heldInput[(int)input];
        }

        private void JumpToLink(ClipLink link)
        {
            int ti = _config.IndexOfSection(link.TargetSection);

            if (ti < 0)   // End
            {
                if (_config.LoopTrack)
                {
                    _comboLog += $"  →({link.Input})→ [Loop]";
                    RestartCombo();
                }
                else
                {
                    _comboLog += $"  →({link.Input})→ [End]";
                    _isPlaying = false;
                }
                return;
            }

            _comboLog += $"  →({link.Input})→ {SectionLabel(ti)}";

            // TransitionIn 동안 이전 클립 → 새 클립 블렌딩 시작
            var fromTc = _config.Clips[_comboActiveClip];
            float dur  = _config.Clips[ti].TransitionIn;
            if (fromTc.Clip != null && dur > 0.0001f)
            {
                _blending      = true;
                _blendFromClip = fromTc.Clip;
                _blendFromTime = Mathf.Clamp(_comboClipTime, 0f, fromTc.Clip.length);
                _blendElapsed  = 0f;
                _blendDuration = dur;
            }
            else _blending = false;

            _comboActiveClip = ti;
            _comboClipTime   = 0f;
            _rmHasPrev       = false;   // 전이 시 루트모션 델타 끊기
            _rmPrevClipIdx   = ti;
        }

        private string SectionLabel(int idx)
        {
            var c = _config.Clips[idx];
            return !string.IsNullOrEmpty(c.SectionName) ? c.SectionName
                 : c.Clip != null ? c.Clip.name : $"Clip{idx}";
        }

        // ── Preview 제어 ─────────────────────────────────────────
        private void StartPreview()
        {
            if (_target == null || _config == null || _config.Clips.Count == 0) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            _isPlaying       = true;
            _lastEditorTime  = EditorApplication.timeSinceStartup;
            _rmPrevClipIdx   = -1;
            _rmPrevClipTime  = 0f;
            _rmHasPrev       = false;
            _blending        = false;
            _targetOriginPos = _target.transform.position;

            if (_comboMode)
            {
                // Entry 섹션(없으면 첫 클립)부터 시작
                int entry = _config.IndexOfSection(_config.EntrySection);
                _comboActiveClip = entry >= 0 ? entry : 0;
                _comboClipTime   = 0f;
                _comboLog        = SectionLabel(_comboActiveClip);
                SampleClipPose(_config.Clips[_comboActiveClip], _comboActiveClip, 0f, false);
            }
            else
            {
                if (_trackTime >= GetTotalDuration()) _trackTime = 0f;
                SampleAtTime(_trackTime, false);
            }
        }

        private void StopPreview() => _isPlaying = false;

        private void ExitPreview()
        {
            _isPlaying = false;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        }

        private void SampleAtTime(float time, bool advancePlayback)
        {
            if (_target == null || _config == null) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();

            float t = 0f;
            for (int i = 0; i < _config.Clips.Count; i++)
            {
                var   tc  = _config.Clips[i];
                if (tc.Clip == null) continue;
                float dur = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);

                if (time <= t + dur || i == _config.Clips.Count - 1)
                {
                    float local    = Mathf.Clamp(time - t, 0f, dur);
                    float clipTime = local * tc.Speed;
                    if (tc.Loop) clipTime = Mathf.Repeat(clipTime, tc.Clip.length);
                    clipTime = Mathf.Clamp(clipTime, 0f, tc.Clip.length);
                    SampleClipPose(tc, i, clipTime, advancePlayback);
                    return;
                }
                t += dur;
            }
        }

        // 단일 클립을 clipTime(초)에 포즈시키고 루트모션 적용
        private void SampleClipPose(TrackClip tc, int clipIdx, float clipTime, bool advancePlayback)
        {
            if (_target == null || tc.Clip == null) return;
            if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(_target, tc.Clip, clipTime);
            ApplyRootMotion(tc, clipIdx, clipTime, advancePlayback);
            SceneView.RepaintAll();
        }

        // PlayerController.LateUpdate와 동일: 루트본 localPosition 델타를 월드 이동으로 변환
        private void ApplyRootMotion(TrackClip tc, int clipIdx, float clipTime, bool advancePlayback)
        {
            if (!tc.UseRootMotion || _rootBone == null) return;

            Vector3 curLocal = _rootBone.localPosition;

            if (advancePlayback)
            {
                bool sameClip = _rmPrevClipIdx == clipIdx;
                bool wrapped  = tc.Loop && clipTime < _rmPrevClipTime - 0.0001f; // 루프 되감김
                bool valid    = _rmHasPrev && sameClip && !wrapped;

                if (valid)
                {
                    Vector3 deltaLocal = curLocal - _rmPrevLocalPos;
                    _target.transform.position +=
                        _target.transform.TransformDirection(deltaLocal) * _rootMotionScale;
                }

                _rmPrevLocalPos = curLocal;
                _rmPrevClipTime = clipTime;
                _rmPrevClipIdx  = clipIdx;
                _rmHasPrev      = true;
            }

            ResetRootBoneVisual();
        }

        // 비주얼: 루트본 / Bip001 XZ 리셋 → 베이크된 이동량이 메시에 남는 것 방지
        private void ResetRootBoneVisual()
        {
            if (_rootBone == null) return;
            _rootBone.localPosition = Vector3.zero;
            if (_bip001Bone != null)
            {
                Vector3 lp = _bip001Bone.localPosition;
                lp.x = 0f; lp.z = 0f;
                _bip001Bone.localPosition = lp;
            }
        }

        // target에 PlayerController가 있으면 _rootBone/_bip001Bone/_rootMotionScale 자동 추출
        private void AutoDetectRootBones()
        {
            _rootBone = null; _bip001Bone = null; _rootMotionScale = 1f;
            if (_target == null) return;

            var pc = _target.GetComponentInChildren<ZZZ.Player.PlayerController>();
            if (pc == null) return;

            var so = new SerializedObject(pc);
            var rb = so.FindProperty("_rootBone");
            var bb = so.FindProperty("_bip001Bone");
            var sc = so.FindProperty("_rootMotionScale");
            if (rb != null) _rootBone        = rb.objectReferenceValue as Transform;
            if (bb != null) _bip001Bone      = bb.objectReferenceValue as Transform;
            if (sc != null) _rootMotionScale = sc.floatValue;
        }

        // ── 시간 헬퍼 ────────────────────────────────────────────
        private float GetTotalDuration()
        {
            if (_config == null) return 0f;
            float t = 0f;
            foreach (var tc in _config.Clips)
                if (tc.Clip != null) t += tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
            return t;
        }

        private float GetClipStartTime(int idx)
        {
            if (_config == null) return 0f;
            float t = 0f;
            for (int i = 0; i < idx && i < _config.Clips.Count; i++)
            {
                var tc = _config.Clips[i];
                if (tc.Clip != null) t += tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
            }
            return t;
        }

        // ── OnGUI ────────────────────────────────────────────────
        private void OnGUI()
        {
            DrawToolbar();
            if (_config != null && _serializedConfig != null) _serializedConfig.Update();

            DrawPlaybar(new Rect(0, ToolbarH, position.width, PlaybarH));

            float contentY = ToolbarH + PlaybarH;
            float contentH = position.height - contentY;
            float timelineW = Mathf.Max(100f, position.width - InspW - 1f);

            DrawTimeline(new Rect(0, contentY, timelineW, contentH));
            EditorGUI.DrawRect(new Rect(timelineW, contentY, 1f, contentH), new Color(0.1f, 0.1f, 0.1f));
            DrawInspector(new Rect(timelineW + 1f, contentY, InspW - 1f, contentH));

            if (_config != null && _serializedConfig != null)
                _serializedConfig.ApplyModifiedProperties();
        }

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
                InputToggle(ComboInput.Special,  "Special",  64);
                InputToggle(ComboInput.Dodge,    "Dodge",    56);
                if (GUILayout.Button("Clear", GUILayout.Width(48)))
                    System.Array.Clear(_heldInput, 0, _heldInput.Length);
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

        private void ResetPreview()
        {
            StopPreview();
            _trackTime       = 0f;
            _rmPrevClipIdx   = -1;
            _rmPrevClipTime  = 0f;
            _rmHasPrev       = false;
            _blending        = false;
            _comboLog        = "";
            if (_target != null && _config != null)
            {
                _target.transform.position = _targetOriginPos;
                if (_comboMode) { /* 포즈는 StartPreview에서 */ }
                else SampleAtTime(0f, false);
            }
        }

        // ── Timeline ─────────────────────────────────────────────
        private void DrawTimeline(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.17f, 0.17f, 0.17f));

            if (_config == null)
            {
                GUI.Label(area, "Config를 선택하거나 New로 생성하세요.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel));
                return;
            }

            // 수평 스크롤
            if (Event.current.type == EventType.ScrollWheel && area.Contains(Event.current.mousePosition))
            {
                _scrollX = Mathf.Max(0f, _scrollX + Event.current.delta.y * 15f);
                Event.current.Use(); Repaint();
            }

            // ── 눈금자 ────────────────────────────────────────────
            var rulerBg = new Rect(0, area.y, area.width, RulerH);
            EditorGUI.DrawRect(rulerBg, new Color(0.21f, 0.21f, 0.21f));
            EditorGUI.DrawRect(new Rect(0, area.y, LabelW, RulerH), new Color(0.18f, 0.18f, 0.18f));
            DrawRuler(new Rect(LabelW, area.y, area.width - LabelW, RulerH));

            // 플레이헤드 삼각형 + 선 (눈금자) — 순차 모드에서만
            float phX = LabelW + _trackTime * _pxPerSec - _scrollX;
            if (!_comboMode && phX >= LabelW && phX <= area.width)
            {
                EditorGUI.DrawRect(new Rect(phX - 1f, area.y, 2f, RulerH), new Color(1f, 0.6f, 0f));
                DrawPlayheadHandle(phX, area.y, 7f, new Color(1f, 0.6f, 0f));
            }

            // 눈금자 플레이헤드 드래그 (순차 모드에서만)
            if (!_comboMode)
                HandleRulerInput(new Rect(LabelW, area.y, area.width - LabelW, RulerH));

            // ── 클립 행 영역 ──────────────────────────────────────
            float rowsY = area.y + RulerH;
            float rowsH = area.height - RulerH;
            GUI.BeginClip(new Rect(0, rowsY, area.width, rowsH));
            var localArea = new Rect(0, 0, area.width, rowsH);

            // 플레이헤드 세로선 (클립 영역) — 순차 모드
            if (!_comboMode && phX >= LabelW && phX <= area.width)
                EditorGUI.DrawRect(new Rect(phX - 1f, 0, 2f, rowsH), new Color(1f, 0.6f, 0f, 0.6f));

            // 콤보 모드: 활성 클립 행 강조 + 로컬 플레이헤드
            if (_comboMode && _comboActiveClip >= 0 && _comboActiveClip < _config.Clips.Count)
            {
                var   atc  = _config.Clips[_comboActiveClip];
                float aRowY = _comboActiveClip * (ClipH + ClipGap);
                EditorGUI.DrawRect(new Rect(LabelW, aRowY, area.width - LabelW, ClipH),
                    new Color(1f, 0.6f, 0f, 0.08f));

                if (atc.Clip != null)
                {
                    float aStartT = GetClipStartTime(_comboActiveClip);
                    float aBarX   = LabelW + aStartT * _pxPerSec - _scrollX;
                    float aNt     = atc.Clip.length > 0f ? _comboClipTime / atc.Clip.length : 0f;
                    float aDur    = atc.Clip.length / Mathf.Max(0.01f, atc.Speed);
                    float aPhX    = aBarX + Mathf.Clamp01(aNt) * aDur * _pxPerSec;
                    EditorGUI.DrawRect(new Rect(aPhX - 1f, aRowY, 2f, ClipH), new Color(1f, 0.6f, 0f));
                }
            }

            // 전이 점선
            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                DrawTransitionConnectors();
                Handles.EndGUI();
            }

            // 클립 행 그리기
            for (int i = 0; i < _config.Clips.Count; i++)
            {
                float rowY    = i * (ClipH + ClipGap);
                float startT  = GetClipStartTime(i);
                var   tc      = _config.Clips[i];
                float dur     = tc.Clip != null ? tc.Clip.length / Mathf.Max(0.01f, tc.Speed) : 0f;
                float barX    = LabelW + startT * _pxPerSec - _scrollX;
                float barW    = dur * _pxPerSec;

                // 드래그 중인 클립은 반투명 처리
                bool isBeingDragged = _reorderingClip == i;
                if (isBeingDragged)
                    EditorGUI.DrawRect(new Rect(0, rowY, area.width, ClipH),
                        new Color(0f, 0f, 0f, 0.45f));

                DrawClipRow(tc, i, barX, barW, rowY, area.width);
            }

            // 순서 변경 삽입 위치 표시선
            if (_reorderingClip >= 0 && _reorderTargetIdx >= 0)
            {
                float lineY = _reorderTargetIdx * (ClipH + ClipGap) - 1f;
                EditorGUI.DrawRect(new Rect(0, lineY, area.width, 3f), new Color(0.3f, 0.65f, 1f));
            }

            // 클립 추가 버튼
            float addY = _config.Clips.Count * (ClipH + ClipGap) + 6f;
            if (addY < rowsH && GUI.Button(new Rect(4, addY, 100, 22), "+ Add Clip"))
            {
                Undo.RecordObject(_config, "Add Clip");
                _config.Clips.Add(new TrackClip());
                EditorUtility.SetDirty(_config);
                _serializedConfig = new SerializedObject(_config);
                Repaint();
            }

            HandleInput(localArea);
            GUI.EndClip();
        }

        // ── 눈금자 플레이헤드 드래그 ─────────────────────────────
        private void HandleRulerInput(Rect rulerRect)
        {
            var ev = Event.current;

            if (ev.type == EventType.MouseDown && ev.button == 0 && rulerRect.Contains(ev.mousePosition))
            {
                _draggingPlayhead = true;
                float t = (ev.mousePosition.x - rulerRect.x + _scrollX) / _pxPerSec;
                _trackTime = Mathf.Clamp(t, 0f, GetTotalDuration());
                StopPreview(); SampleAtTime(_trackTime, false);
                ev.Use(); Repaint();
            }
            if (ev.type == EventType.MouseDrag && _draggingPlayhead)
            {
                float t = (ev.mousePosition.x - rulerRect.x + _scrollX) / _pxPerSec;
                _trackTime = Mathf.Clamp(t, 0f, GetTotalDuration());
                StopPreview(); SampleAtTime(_trackTime, false);
                ev.Use(); Repaint();
            }
            if (ev.type == EventType.MouseUp) _draggingPlayhead = false;
        }

        private static void DrawPlayheadHandle(float x, float topY, float size, Color col)
        {
            // 아래를 향하는 삼각형
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            Handles.color = col;
            Handles.DrawAAConvexPolygon(
                new Vector3(x,        topY + size),
                new Vector3(x - size, topY),
                new Vector3(x + size, topY));
            Handles.EndGUI();
        }

        // ── 눈금자 ────────────────────────────────────────────────
        private void DrawRuler(Rect r)
        {
            float step   = _pxPerSec >= 100f ? 0.5f : _pxPerSec >= 50f ? 1f : 2f;
            float startT = _scrollX / _pxPerSec;
            float endT   = startT + r.width / _pxPerSec + step;

            for (float t = Mathf.Floor(startT / step) * step; t <= endT; t += step)
            {
                float x     = r.x + t * _pxPerSec - _scrollX;
                bool  major = Mathf.RoundToInt(t / step) % 5 == 0 || t == 0f;
                float th    = major ? 10f : 5f;
                EditorGUI.DrawRect(new Rect(x - 0.5f, r.y + r.height - th, 1f, th),
                    new Color(0.52f, 0.52f, 0.52f));
                if (major && t >= 0f)
                    GUI.Label(new Rect(x - 18f, r.y + 1f, 36f, 13f), $"{t:F1}s",
                        EditorStyles.centeredGreyMiniLabel);
            }
        }

        // ── Link 연결선 (윈도우 끝 → 타겟 섹션 행) ────────────────
        private void DrawTransitionConnectors()
        {
            for (int i = 0; i < _config.Clips.Count; i++)
            {
                var tc = _config.Clips[i];
                if (tc.Clip == null) continue;
                float startT = GetClipStartTime(i);
                float dur    = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
                float barX   = LabelW + startT * _pxPerSec - _scrollX;
                float barW   = dur * _pxPerSec;
                float srcY   = i * (ClipH + ClipGap) + ClipH * 0.5f;

                foreach (var link in tc.Links)
                {
                    int   ti = _config.IndexOfSection(link.TargetSection);
                    Color col = InputColor(link.Input);
                    // 윈도우 끝 지점에서 출발
                    float sx = barX + link.WindowEnd * barW;

                    if (ti < 0)
                    {
                        // End: 아래로 짧게 떨어지는 선
                        Handles.color = new Color(col.r, col.g, col.b, 0.5f);
                        Handles.DrawDottedLine(new Vector3(sx, srcY + 6f),
                            new Vector3(sx, srcY + ClipH * 0.5f), 3f);
                        continue;
                    }

                    float dstStartT = GetClipStartTime(ti);
                    float dstX = LabelW + dstStartT * _pxPerSec - _scrollX;
                    float dstY = ti * (ClipH + ClipGap) + ClipH * 0.5f;

                    Handles.color = new Color(col.r, col.g, col.b, 0.7f);
                    float cdx = Mathf.Abs(dstY - srcY) * 0.4f + 20f;
                    Handles.DrawBezier(
                        new Vector3(sx, srcY), new Vector3(dstX, dstY),
                        new Vector3(sx + cdx, srcY), new Vector3(dstX - cdx, dstY),
                        col, null, 2f);

                    // 타겟 도착 화살표
                    Handles.DrawAAConvexPolygon(
                        new Vector3(dstX, dstY),
                        new Vector3(dstX - 6f, dstY - 4f),
                        new Vector3(dstX - 6f, dstY + 4f));
                }
            }
        }

        // ── 클립 행 ───────────────────────────────────────────────
        private void DrawClipRow(TrackClip tc, int idx, float barX, float barW,
            float rowY, float totalW)
        {
            bool sel = idx == _selectedClip;

            // 레이블 배경
            EditorGUI.DrawRect(new Rect(0, rowY, LabelW - 1, ClipH),
                sel ? new Color(0.22f, 0.30f, 0.44f) : new Color(0.19f, 0.19f, 0.19f));

            // 드래그 핸들 표시 (좌측 3px 바)
            EditorGUI.DrawRect(new Rect(0, rowY + 2, 3, ClipH - 4),
                _reorderingClip == idx ? new Color(0.3f, 0.65f, 1f) : new Color(0.38f, 0.38f, 0.38f));

            string name = tc.Clip != null ? tc.Clip.name : "(No Clip)";
            GUI.Label(new Rect(7, rowY + 4, LabelW - 26, 15), name,
                new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 10, clipping = TextClipping.Clip,
                    normal = { textColor = sel ? Color.white : new Color(0.82f, 0.82f, 0.82f) }
                });

            DrawBadge("Loop", tc.Loop, new Rect(7, rowY + 22, 34, 12));
            DrawBadge("RM",   tc.UseRootMotion, new Rect(43, rowY + 22, 26, 12));

            if (tc.Clip != null)
                GUI.Label(new Rect(7, rowY + 38, LabelW - 26, 11),
                    $"{tc.Clip.length / Mathf.Max(0.01f, tc.Speed):F2}s  x{tc.Speed:F1}",
                    new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = new Color(0.52f, 0.52f, 0.52f) } });

            if (GUI.Button(new Rect(LabelW - 18, rowY + 3, 15, 15), "×",
                new GUIStyle(EditorStyles.miniButton) { fontSize = 9 }))
            {
                Undo.RecordObject(_config, "Remove Clip");
                _config.Clips.RemoveAt(idx);
                EditorUtility.SetDirty(_config);
                _serializedConfig = new SerializedObject(_config);
                if (_selectedClip >= _config.Clips.Count) _selectedClip = -1;
                Repaint();
                return;
            }

            // 클립 바
            if (barW > 0 && barX + barW > LabelW && barX < totalW)
            {
                Color barCol = sel ? new Color(0.28f, 0.46f, 0.78f) : new Color(0.30f, 0.30f, 0.34f);
                EditorGUI.DrawRect(new Rect(barX, rowY + 6, barW, ClipH - 12), barCol);
                EditorGUI.DrawRect(new Rect(barX, rowY + 6,           barW, 1f), new Color(0.55f, 0.55f, 0.60f));
                EditorGUI.DrawRect(new Rect(barX, rowY + ClipH - 7,   barW, 1f), new Color(0.55f, 0.55f, 0.60f));

                if (barW > 45f)
                    GUI.Label(new Rect(barX + 4, rowY + 8, barW - 8, 14), name,
                        new GUIStyle(EditorStyles.miniLabel)
                        { normal = { textColor = new Color(0.68f, 0.68f, 0.68f) },
                          clipping = TextClipping.Clip });

                // Link 윈도우 밴드 (바 하단)
                DrawLinkWindows(tc, barX, barW, rowY);
            }

            // Notify 마커
            if (tc.Clip != null)
                for (int ni = 0; ni < tc.Notifies.Count; ni++)
                    DrawNotifyMarker(tc.Notifies[ni], ni, idx, barX, barW, rowY);
        }

        // 입력 타입별 색상
        private static Color InputColor(ComboInput input) => input switch
        {
            ComboInput.Normal   => new Color(0.3f, 0.6f, 1.0f),
            ComboInput.Enhanced => new Color(1.0f, 0.55f, 0.15f),
            ComboInput.Special  => new Color(0.9f, 0.3f, 0.9f),
            ComboInput.Dodge    => new Color(0.3f, 0.9f, 0.6f),
            _                   => new Color(0.7f, 0.7f, 0.7f),
        };

        // 클립 바 하단에 각 Link의 입력 수용 윈도우를 밴드로 표시
        private void DrawLinkWindows(TrackClip tc, float barX, float barW, float rowY)
        {
            float bandH    = 5f;
            float baseY    = rowY + ClipH - 7f - bandH;   // 바 하단 안쪽
            int   linkCount = tc.Links.Count;

            for (int i = 0; i < linkCount; i++)
            {
                var   link = tc.Links[i];
                float wsX  = barX + link.WindowStart * barW;
                float weX  = barX + link.WindowEnd   * barW;
                float y    = baseY - i * (bandH + 1f);   // 링크마다 한 줄씩 위로 쌓음
                Color col  = InputColor(link.Input);

                EditorGUI.DrawRect(new Rect(wsX, y, Mathf.Max(2f, weX - wsX), bandH),
                    new Color(col.r, col.g, col.b, 0.75f));

                // 타겟 섹션 라벨 (윈도우가 충분히 넓을 때)
                string targ = string.IsNullOrEmpty(link.TargetSection) ? "End" : link.TargetSection;
                if (weX - wsX > 36f)
                    GUI.Label(new Rect(wsX + 2, y - 9f, 80, 10),
                        $"{link.Input}→{targ}",
                        new GUIStyle(EditorStyles.miniLabel)
                        { fontSize = 8, normal = { textColor = col }, clipping = TextClipping.Clip });
            }
        }

        private void DrawBadge(string label, bool active, Rect r)
        {
            Color col = active ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.38f, 0.38f, 0.38f);
            EditorGUI.DrawRect(r, new Color(col.r, col.g, col.b, 0.22f));
            GUI.Label(r, label, new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = col }, alignment = TextAnchor.MiddleCenter, fontSize = 9 });
        }

        // ── Notify 마커 ──────────────────────────────────────────
        private void DrawNotifyMarker(TrackNotify notify, int ni, int clipIdx,
            float barX, float barW, float rowY)
        {
            float mx  = barX + notify.NormalizedTime * barW;
            float my  = rowY + 6f;
            float mh  = ClipH - 12f;
            bool  sel = _selectedNotify == ni && _notifyClipIdx == clipIdx;
            Color col = sel ? Color.yellow : NotifyColors[(int)notify.Type % NotifyColors.Length];

            EditorGUI.DrawRect(new Rect(mx - 1f, my, 2f, mh), col);
            EditorGUI.DrawRect(new Rect(mx - 4f, my - 5f, 8f, 5f), col);

            string icon = notify.Type switch
            {
                NotifyType.Effect => "E",
                NotifyType.Camera => "C",
                NotifyType.Sound  => "S",
                _                 => "N",
            };
            GUI.Label(new Rect(mx - 5f, my - 5f, 10f, 10f), icon,
                new GUIStyle(EditorStyles.miniLabel)
                { alignment = TextAnchor.MiddleCenter, fontSize = 8,
                  normal = { textColor = new Color(0.1f, 0.1f, 0.1f) } });

            if (_pxPerSec > 60f && !string.IsNullOrEmpty(notify.EventName))
            {
                string lbl = notify.EventName.Length > 8
                    ? notify.EventName.Substring(0, 8) : notify.EventName;
                GUI.Label(new Rect(mx + 3, my + 2, 60, 10), lbl,
                    new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = col }, fontSize = 8 });
            }
        }

        // ── 입력 처리 ────────────────────────────────────────────
        private void HandleInput(Rect area)
        {
            var ev = Event.current;

            // ── MouseDown ────────────────────────────────────────
            if (ev.type == EventType.MouseDown && ev.button == 0 && area.Contains(ev.mousePosition))
            {
                bool hitSomething = false;

                for (int i = 0; i < _config.Clips.Count && !hitSomething; i++)
                {
                    float rowY = i * (ClipH + ClipGap);
                    if (ev.mousePosition.y < rowY || ev.mousePosition.y >= rowY + ClipH) continue;

                    // 레이블 영역 클릭 → 순서 변경 드래그 시작
                    if (ev.mousePosition.x < LabelW)
                    {
                        _reorderingClip   = i;
                        _reorderTargetIdx = i;
                        _selectedClip     = i;
                        _selectedNotify   = -1;
                        hitSomething      = true;
                        ev.Use(); Repaint();
                        break;
                    }

                    var   tc     = _config.Clips[i];
                    float startT = GetClipStartTime(i);
                    float dur    = tc.Clip != null ? tc.Clip.length / Mathf.Max(0.01f, tc.Speed) : 0f;
                    float barX   = LabelW + startT * _pxPerSec - _scrollX;
                    float barW   = dur * _pxPerSec;

                    // Notify 클릭 우선
                    if (tc.Clip != null)
                    {
                        for (int ni = 0; ni < tc.Notifies.Count; ni++)
                        {
                            float mx = barX + tc.Notifies[ni].NormalizedTime * barW;
                            if (Mathf.Abs(ev.mousePosition.x - mx) <= 7f)
                            {
                                _selectedClip = i; _selectedNotify = ni; _notifyClipIdx = i;
                                _draggingNotify = true; _dragNotifyClip = i; _dragNotifyIdx = ni;
                                hitSomething = true;
                                break;
                            }
                        }
                    }

                    if (!hitSomething) { _selectedClip = i; _selectedNotify = -1; hitSomething = true; }

                    // 타임라인 영역 클릭 → 플레이헤드 이동
                    if (ev.mousePosition.x >= LabelW)
                    {
                        float newT = (ev.mousePosition.x - LabelW + _scrollX) / _pxPerSec;
                        _trackTime = Mathf.Clamp(newT, 0f, GetTotalDuration());
                        StopPreview(); SampleAtTime(_trackTime, false);
                    }
                }

                if (!hitSomething && ev.mousePosition.x >= LabelW)
                {
                    float newT = (ev.mousePosition.x - LabelW + _scrollX) / _pxPerSec;
                    _trackTime = Mathf.Clamp(newT, 0f, GetTotalDuration());
                    StopPreview(); SampleAtTime(_trackTime, false);
                    _selectedClip = -1; _selectedNotify = -1;
                }

                ev.Use(); Repaint();
            }

            // ── MouseDrag ────────────────────────────────────────
            if (ev.type == EventType.MouseDrag && ev.button == 0)
            {
                // Notify 드래그
                if (_draggingNotify && _dragNotifyClip < _config.Clips.Count)
                {
                    var   tc     = _config.Clips[_dragNotifyClip];
                    float startT = GetClipStartTime(_dragNotifyClip);
                    float dur    = tc.Clip != null ? tc.Clip.length / Mathf.Max(0.01f, tc.Speed) : 1f;
                    float barX   = LabelW + startT * _pxPerSec - _scrollX;
                    float barW   = dur * _pxPerSec;
                    float newN   = barW > 0f ? Mathf.Clamp01((ev.mousePosition.x - barX) / barW) : 0f;
                    Undo.RecordObject(_config, "Move Notify");
                    tc.Notifies[_dragNotifyIdx].NormalizedTime = newN;
                    EditorUtility.SetDirty(_config);
                    ev.Use(); Repaint();
                }
                // 클립 순서 변경 드래그
                else if (_reorderingClip >= 0)
                {
                    // 마우스 Y 위치로 삽입 인덱스 계산
                    int target = Mathf.Clamp(
                        Mathf.RoundToInt(ev.mousePosition.y / (ClipH + ClipGap)),
                        0, _config.Clips.Count);
                    _reorderTargetIdx = target;
                    ev.Use(); Repaint();
                }
            }

            // ── MouseUp ──────────────────────────────────────────
            if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                _draggingNotify = false;

                if (_reorderingClip >= 0)
                {
                    int from = _reorderingClip;
                    int to   = _reorderTargetIdx;

                    // to가 from 이후를 가리킬 때 실제 삽입 인덱스 보정
                    int insertIdx = to > from ? to - 1 : to;

                    if (insertIdx != from && insertIdx >= 0 && insertIdx < _config.Clips.Count)
                    {
                        Undo.RecordObject(_config, "Reorder Clips");
                        var clip = _config.Clips[from];
                        _config.Clips.RemoveAt(from);
                        int clamp = Mathf.Clamp(insertIdx, 0, _config.Clips.Count);
                        _config.Clips.Insert(clamp, clip);
                        _selectedClip     = clamp;
                        EditorUtility.SetDirty(_config);
                        _serializedConfig = new SerializedObject(_config);
                    }

                    _reorderingClip   = -1;
                    _reorderTargetIdx = -1;
                    Repaint();
                }
            }

            // ── 우클릭: Notify 추가 ──────────────────────────────
            if (ev.type == EventType.ContextClick && area.Contains(ev.mousePosition))
            {
                for (int i = 0; i < _config.Clips.Count; i++)
                {
                    var tc = _config.Clips[i];
                    if (tc.Clip == null) continue;
                    float rowY   = i * (ClipH + ClipGap);
                    float startT = GetClipStartTime(i);
                    float dur    = tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
                    float barX   = LabelW + startT * _pxPerSec - _scrollX;
                    float barW   = dur * _pxPerSec;

                    if (ev.mousePosition.y < rowY || ev.mousePosition.y >= rowY + ClipH) continue;
                    if (ev.mousePosition.x < barX  || ev.mousePosition.x > barX + barW)  continue;

                    float normT = barW > 0f ? Mathf.Clamp01((ev.mousePosition.x - barX) / barW) : 0f;
                    int capI = i; float capN = normT;
                    var menu = new GenericMenu();
                    foreach (NotifyType nt in Enum.GetValues(typeof(NotifyType)))
                    {
                        var capType = nt;
                        menu.AddItem(new GUIContent($"Add {nt} Notify"), false, () =>
                        {
                            Undo.RecordObject(_config, "Add Notify");
                            _config.Clips[capI].Notifies.Add(new TrackNotify
                            { Type = capType, NormalizedTime = capN, EventName = capType.ToString() });
                            _selectedClip   = capI;
                            _selectedNotify = _config.Clips[capI].Notifies.Count - 1;
                            _notifyClipIdx  = capI;
                            EditorUtility.SetDirty(_config);
                            _serializedConfig = new SerializedObject(_config);
                            Repaint();
                        });
                    }
                    menu.ShowAsContext();
                    ev.Use();
                    break;
                }
            }
        }

        // ── 우측 인스펙터 ────────────────────────────────────────
        private void DrawInspector(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.2f, 0.2f, 0.2f));
            GUILayout.BeginArea(area);
            _inspScroll = EditorGUILayout.BeginScrollView(_inspScroll);

            if (_config == null)
            {
                EditorGUILayout.LabelField("Config를 선택하세요.", EditorStyles.centeredGreyMiniLabel);
            }
            else if (_selectedClip >= 0 && _selectedClip < _config.Clips.Count)
            {
                var tc = _config.Clips[_selectedClip];
                if (_selectedNotify >= 0 && _notifyClipIdx == _selectedClip &&
                    _selectedNotify < tc.Notifies.Count)
                    DrawNotifyInspector(tc, _selectedNotify);
                else
                    DrawClipInspector(tc, _selectedClip);
            }
            else
            {
                DrawTrackLevelInspector();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawTrackLevelInspector()
        {
            EditorGUILayout.LabelField(_config.TrackName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Clips: {_config.Clips.Count}  /  Total: {GetTotalDuration():F2}s",
                EditorStyles.miniLabel);

            DrawSeparator();
            EditorGUILayout.LabelField("Root Motion", EditorStyles.boldLabel);
            if (_target != null && _target.GetComponentInChildren<ZZZ.Player.PlayerController>() != null)
                EditorGUILayout.LabelField("PlayerController에서 자동 감지됨", EditorStyles.miniLabel);

            _rootBone   = (Transform)EditorGUILayout.ObjectField("Root Bone",  _rootBone,   typeof(Transform), true);
            _bip001Bone = (Transform)EditorGUILayout.ObjectField("Bip001 Bone", _bip001Bone, typeof(Transform), true);
            _rootMotionScale = EditorGUILayout.FloatField("RM Scale", _rootMotionScale);

            EditorGUILayout.HelpBox(
                "클립에서 Use Root Motion을 켜고 Play하면\n루트본 이동량이 GameObject에 적용됩니다.",
                MessageType.None);

            DrawSeparator();
            EditorGUILayout.LabelField("클립 바 클릭 → 편집", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField("우클릭 → Notify 추가",  EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawClipInspector(TrackClip tc, int idx)
        {
            EditorGUILayout.LabelField($"Clip  {idx + 1}", EditorStyles.boldLabel);
            DrawSeparator();

            EditorGUI.BeginChangeCheck();
            string sect = EditorGUILayout.TextField("Section Name", tc.SectionName);
            var   clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", tc.Clip, typeof(AnimationClip), false);
            bool  loop = EditorGUILayout.Toggle("Loop",            tc.Loop);
            bool  rm   = EditorGUILayout.Toggle("Use Root Motion", tc.UseRootMotion);
            float spd  = EditorGUILayout.FloatField("Speed",       tc.Speed);
            float fade = EditorGUILayout.FloatField("Transition In (s)", tc.TransitionIn);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Clip");
                // 섹션 이름이 비었으면 클립 이름으로 자동 채움
                if (string.IsNullOrEmpty(sect) && clip != null) sect = clip.name;
                tc.SectionName = sect;
                tc.Clip = clip; tc.Loop = loop; tc.UseRootMotion = rm;
                tc.Speed = Mathf.Max(0.01f, spd); tc.TransitionIn = Mathf.Max(0f, fade);
                EditorUtility.SetDirty(_config);
            }

            if (tc.Clip != null)
                EditorGUILayout.LabelField(
                    $"{tc.Clip.length:F3}s → {tc.Clip.length / Mathf.Max(0.01f, tc.Speed):F3}s   " +
                    $"({Mathf.RoundToInt(tc.Clip.length * tc.Clip.frameRate)}f)",
                    EditorStyles.miniLabel);

            // ── Links (다음 섹션 분기) ───────────────────────────
            DrawSeparator();
            EditorGUILayout.LabelField($"Links  ({tc.Links.Count})", EditorStyles.boldLabel);
            DrawLinksEditor(tc);

            DrawSeparator();
            EditorGUILayout.LabelField($"Notifies  ({tc.Notifies.Count})  —  우클릭 추가",
                EditorStyles.miniLabel);
        }

        private void DrawLinksEditor(TrackClip tc)
        {
            // 섹션 이름 드롭다운 후보 (빈 항목 = 트랙 종료)
            string[] sectionOptions = BuildSectionOptions();

            for (int i = 0; i < tc.Links.Count; i++)
            {
                var link = tc.Links[i];
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i}", GUILayout.Width(24));
                if (GUILayout.Button("×", GUILayout.Width(20)))
                {
                    Undo.RecordObject(_config, "Remove Link");
                    tc.Links.RemoveAt(i);
                    EditorUtility.SetDirty(_config);
                    Repaint();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                // Target Section 드롭다운
                int curIdx = Mathf.Max(0, Array.IndexOf(sectionOptions,
                    string.IsNullOrEmpty(link.TargetSection) ? "(End)" : link.TargetSection));
                int newIdx = EditorGUILayout.Popup("Target", curIdx, sectionOptions);
                var input  = (ComboInput)EditorGUILayout.EnumPopup("Input", link.Input);

                // 윈도우 (min-max 슬라이더)
                float ws = link.WindowStart, we = link.WindowEnd;
                EditorGUILayout.MinMaxSlider(
                    new GUIContent($"Window {ws:F2}-{we:F2}"), ref ws, ref we, 0f, 1f);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_config, "Edit Link");
                    link.TargetSection = newIdx == 0 ? "" : sectionOptions[newIdx];
                    link.Input         = input;
                    link.WindowStart   = ws;
                    link.WindowEnd     = we;
                    EditorUtility.SetDirty(_config);
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("+ Add Link"))
            {
                Undo.RecordObject(_config, "Add Link");
                tc.Links.Add(new ClipLink());
                EditorUtility.SetDirty(_config);
                Repaint();
            }
        }

        // [0] = "(End)" + 모든 섹션 이름
        private string[] BuildSectionOptions()
        {
            var list = new System.Collections.Generic.List<string> { "(End)" };
            foreach (var c in _config.Clips)
            {
                string n = !string.IsNullOrEmpty(c.SectionName) ? c.SectionName
                         : c.Clip != null ? c.Clip.name : "";
                if (!string.IsNullOrEmpty(n) && !list.Contains(n)) list.Add(n);
            }
            return list.ToArray();
        }

        private void DrawNotifyInspector(TrackClip tc, int ni)
        {
            var notify = tc.Notifies[ni];
            EditorGUILayout.LabelField($"Notify  —  {notify.Type}", EditorStyles.boldLabel);
            DrawSeparator();

            EditorGUI.BeginChangeCheck();
            var   type   = (NotifyType)EditorGUILayout.EnumPopup("Type",  notify.Type);
            float normT  = EditorGUILayout.Slider("Normalized Time", notify.NormalizedTime, 0f, 1f);
            string eName = EditorGUILayout.TextField("Event Name",   notify.EventName);
            GameObject prefab = notify.EffectPrefab;
            if (type == NotifyType.Effect)
                prefab = (GameObject)EditorGUILayout.ObjectField(
                    "Effect Prefab", notify.EffectPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_config, "Edit Notify");
                notify.Type = type; notify.NormalizedTime = normT;
                notify.EventName = eName; notify.EffectPrefab = prefab;
                EditorUtility.SetDirty(_config);
            }

            DrawSeparator();
            GUI.backgroundColor = new Color(0.72f, 0.22f, 0.22f);
            if (GUILayout.Button("Delete Notify", GUILayout.Width(120)))
            {
                Undo.RecordObject(_config, "Delete Notify");
                tc.Notifies.RemoveAt(ni);
                _selectedNotify = -1;
                EditorUtility.SetDirty(_config);
                _serializedConfig = new SerializedObject(_config);
                Repaint();
            }
            GUI.backgroundColor = Color.white;
        }

        private static void DrawSeparator()
        {
            EditorGUILayout.Space(2);
            var r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.3f, 0.3f, 0.3f, 0.5f));
        }
    }
}
