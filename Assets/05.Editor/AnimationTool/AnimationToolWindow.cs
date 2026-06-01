using UnityEngine;
using UnityEditor;
using ZZZ.Player;
using ZZZ.Player.StateMachine;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Editor.AnimationTool
{
    public class AnimationToolWindow : EditorWindow
    {
        private LocomotionConfig _config;
        private SerializedObject _serializedConfig;
        private GameObject       _previewTarget;
        private Transform        _rootBone;        // 모델의 실제 Root 본

        private enum ClipType { WalkStart, WalkEnd, RunEnd }
        private ClipType _selectedClip = ClipType.WalkStart;
        private float    _previewTime;
        private bool     _isPlaying;
        private double   _lastTime;

        private Vector2 _scroll;

        [MenuItem("ZZZ/Animation Tool")]
        public static void Open()
        {
            var w = GetWindow<AnimationToolWindow>("Animation Tool");
            w.minSize = new Vector2(360f, 600f);
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            SceneView.duringSceneGui -= OnSceneGUI;
            StopPreview();
        }

        private void OnGUI()
        {
            DrawConfigSelector();
            if (_config == null)
            {
                EditorGUILayout.HelpBox("LocomotionConfig를 선택하거나 New로 생성하세요.", MessageType.Info);
                return;
            }

            _serializedConfig.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Play Mode 라이브 모니터
            if (Application.isPlaying)
                DrawLiveMonitor();

            DrawSeparator();
            DrawPreviewSection();
            DrawSeparator();
            DrawMovementConfig();

            EditorGUILayout.EndScrollView();

            bool changed = _serializedConfig.ApplyModifiedProperties();
            if (changed) SceneView.RepaintAll();

            // Play Mode에서는 SO 변경이 즉시 반영됨 (같은 레퍼런스)
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play 중 — 수치 변경 시 즉시 반영됩니다.", MessageType.None);
            }
        }

        // ── 라이브 모니터 (Play Mode) ─────────────────────────────
        private void DrawLiveMonitor()
        {
            var psm = FindFirstObjectByType<PlayerStateMachine>();
            if (psm == null) return;

            var bridge     = psm.GetComponent<PlayerAnimatorBridge>();
            var controller = psm.GetComponent<PlayerController>();
            var locoState  = psm.LocomotionState;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("▶ Live Monitor", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                // State Machine 상태
                EditorGUILayout.TextField("SM State",   psm.CurrentStateName);
                if (locoState != null)
                    EditorGUILayout.TextField("Sub State", locoState.CurrentSubState);

                EditorGUILayout.Space(2);

                // 현재 애니메이션
                if (bridge != null)
                {
                    string clipInfo = bridge.GetCurrentStateName();
                    EditorGUILayout.TextField("Animation", clipInfo);

                    float t = bridge.GetCurrentNormalizedTimeRaw();
                    t = Mathf.Repeat(t, 1f); // 루프 클립 대응
                    EditorGUILayout.Slider("Progress", t, 0f, 1f);
                }

                EditorGUILayout.Space(2);

                // 이동 상태
                if (controller != null)
                {
                    EditorGUILayout.FloatField("Move Speed",      controller.CurrentSpeed);
                    EditorGUILayout.Toggle("Code Movement",       controller.UseCodeMovement);
                    EditorGUILayout.Toggle("Root Motion Active",  controller.IsRootMotionActive);
                    EditorGUILayout.FloatField("Root Delta",      controller.LastRootDelta);
                }
            }

            // 구분선
            var rect = EditorGUILayout.GetControlRect(false, 2f);
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.8f, 0.4f, 0.5f));

            Repaint();
        }

        // ── Config Selector ───────────────────────────────────────
        private void DrawConfigSelector()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Config", GUILayout.Width(50));

            var prev = _config;
            _config = (LocomotionConfig)EditorGUILayout.ObjectField(
                _config, typeof(LocomotionConfig), false);
            if (_config != prev)
                _serializedConfig = _config != null ? new SerializedObject(_config) : null;

            if (GUILayout.Button("New", GUILayout.Width(44)))
                CreateNewConfig();
            EditorGUILayout.EndHorizontal();
            DrawSeparator();
        }

        // ── 에디터 프리뷰 ─────────────────────────────────────────
        private void DrawPreviewSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Editor Preview", EditorStyles.boldLabel);

            _previewTarget = (GameObject)EditorGUILayout.ObjectField(
                "Target", _previewTarget, typeof(GameObject), true);

            _rootBone = (Transform)EditorGUILayout.ObjectField(
                "Root Bone", _rootBone, typeof(Transform), true);

            _selectedClip = (ClipType)EditorGUILayout.EnumPopup("Clip", _selectedClip);

            string propName = _selectedClip switch
            {
                ClipType.WalkStart => "WalkStartClip",
                ClipType.WalkEnd   => "WalkEndClip",
                _                  => "RunEndClip",
            };
            EditorGUILayout.PropertyField(
                _serializedConfig.FindProperty(propName), new GUIContent("  Clip Asset"));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_isPlaying ? "■  Stop" : "▶  Play", GUILayout.Height(26)))
                TogglePreview();

            GUI.enabled = !_isPlaying;
            float newT = EditorGUILayout.Slider(_previewTime, 0f, 1f);
            if (!_isPlaying && Mathf.Abs(newT - _previewTime) > 0.001f)
            {
                _previewTime = newT;
                SampleCurrentClip(_previewTime);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        // ── 이동 설정 ─────────────────────────────────────────────
        private void DrawMovementConfig()
        {
            DrawHeader("Movement Speed");
            DrawProp("MoveSpeed",     "걷기 속도");
            DrawProp("SprintSpeed",   "달리기 속도");
            DrawProp("RotationSpeed", "회전 속도");
            DrawProp("AnimatorSpeed", "Animator 배속");
            DrawSeparator();

            DrawHeader("Root Motion");
            DrawClipSection("Walk Start", "UseRootMotion_WalkStart", "WalkStartClip", ClipType.WalkStart);
            DrawClipSection("Walk End",   "UseRootMotion_WalkEnd",   "WalkEndClip",   ClipType.WalkEnd);
            DrawClipSection("Run End",    "UseRootMotion_RunEnd",    "RunEndClip",    ClipType.RunEnd);
        }

        private void DrawClipSection(string title, string rootMotionProp, string clipProp, ClipType type)
        {
            bool isSelected = _selectedClip == type;
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (GUILayout.Button("Preview", GUILayout.Width(60)))
            {
                _selectedClip = type;
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            if (isSelected)
            {
                var rect = EditorGUILayout.GetControlRect(false, 2f);
                EditorGUI.DrawRect(rect, Color.cyan * 0.6f);
            }

            DrawProp(rootMotionProp, "루트 모션 사용");
            DrawProp(clipProp,       "클립 (프리뷰용)");
            DrawSeparator();
        }

        // ── Preview Logic ─────────────────────────────────────────
        private void TogglePreview()
        {
            if (_isPlaying) StopPreview();
            else            StartPreview();
        }

        private void StartPreview()
        {
            if (_previewTarget == null || GetCurrentClip() == null)
            {
                Debug.LogWarning("[AnimTool] Target 또는 Clip을 설정하세요.");
                return;
            }
            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();

            _isPlaying   = true;
            _previewTime = 0f;
            _lastTime    = EditorApplication.timeSinceStartup;
        }

        private void StopPreview()
        {
            _isPlaying = false;
            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();
        }

        private void OnEditorUpdate()
        {
            if (!_isPlaying) return;

            double now   = EditorApplication.timeSinceStartup;
            float  delta = (float)(now - _lastTime);
            _lastTime    = now;

            AnimationClip clip = GetCurrentClip();
            if (clip == null) { StopPreview(); return; }

            _previewTime += delta / clip.length;
            if (_previewTime >= 1f) { _previewTime = 1f; _isPlaying = false; }

            SampleCurrentClip(_previewTime);
            Repaint();
            SceneView.RepaintAll();
        }

        private void SampleCurrentClip(float t)
        {
            if (_previewTarget == null) return;
            var clip = GetCurrentClip();
            if (clip == null) return;
            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(_previewTarget, clip, t * clip.length);
        }

        private AnimationClip GetCurrentClip() => _selectedClip switch
        {
            ClipType.WalkStart => _config?.WalkStartClip,
            ClipType.WalkEnd   => _config?.WalkEndClip,
            _                  => _config?.RunEndClip,
        };

        // ── Scene Gizmo ───────────────────────────────────────────
        private void OnSceneGUI(SceneView _)
        {
            if (_previewTarget == null) return;

            if (_rootBone != null)
                DrawRootMarker(_rootBone.position, _previewTarget.transform.position);
        }

        private static void DrawRootMarker(Vector3 rootPos, Vector3 originPos)
        {
            // 녹색 구 — 실제 Root 본 위치
            Handles.color = Color.green;
            Handles.DrawWireDisc(rootPos, Vector3.up, 0.1f);
            Handles.DrawWireDisc(rootPos, Vector3.right, 0.1f);
            Handles.Label(rootPos + Vector3.up * 0.25f, $"Root  ({rootPos.x:F2}, {rootPos.z:F2})");

            // 원점에서 Root까지 선
            Handles.color = new Color(0f, 1f, 0f, 0.4f);
            Handles.DrawDottedLine(originPos, rootPos, 4f);
        }


        // ── Utility ───────────────────────────────────────────────
        private void DrawHeader(string title)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private void DrawProp(string prop, string label)
        {
            var p = _serializedConfig.FindProperty(prop);
            if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label));
        }

        private void CreateNewConfig()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create LocomotionConfig", "LocomotionConfig", "asset", "저장 위치 선택");
            if (string.IsNullOrEmpty(path)) return;
            var asset = CreateInstance<LocomotionConfig>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            _config           = asset;
            _serializedConfig = new SerializedObject(_config);
        }

        private static void DrawSeparator()
        {
            EditorGUILayout.Space(2);
            var r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.3f, 0.3f, 0.3f, 0.5f));
        }
    }
}
