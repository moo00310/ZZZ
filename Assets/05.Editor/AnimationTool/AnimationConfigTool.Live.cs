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
        // 플레이 중인 씬에서 PlayerStateMachine을 찾아 현재 config/섹션/시간을 읽어온다
        private void UpdateLiveState()
        {
            if (_liveMachine == null)
                _liveMachine = UnityEngine.Object.FindFirstObjectByType<PlayerStateMachine>();
            if (_liveMachine == null) { _liveConfig = null; return; }

            _liveConfig      = _liveMachine.CurrentConfig;
            _liveClipIdx     = _liveMachine.CurrentClipIndex;
            _liveSection     = _liveMachine.CurrentSection;
            _liveNt          = _liveMachine.CurrentNormalizedTime;
            _liveHasBuffered = _liveMachine.HasBufferedInput;
            _liveBuffered    = _liveMachine.BufferedInput;
            _liveMove        = _liveMachine.CurrentMoveDir;

            // Follow ON이면 런타임 config를 자동으로 따라간다 (config가 비어 있으면 무조건 채움)
            if (_liveConfig != null && _liveConfig != _config && (_liveFollow || _config == null))
                ShowConfig(_liveConfig);

            Repaint();
        }

        // 창에 표시할 config를 교체 (SerializedObject 재생성 + 선택/스크롤 초기화)
        private void ShowConfig(AnimationConfig cfg)
        {
            _config = cfg;
            _serializedConfig = cfg != null ? new SerializedObject(cfg) : null;
            _selectedClip = -1; _selectedNotify = -1; _scrollX = 0f; _scrollY = 0f;
        }
        // ── Livebar (플레이 중 런타임 상태 표시) ──────────────────
        private void DrawLivebar(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.14f, 0.19f, 0.14f));
            GUILayout.BeginArea(area);

            // 1행: LIVE 배지 + 현재 config/섹션/시간
            EditorGUILayout.BeginHorizontal();
            var liveStyle = new GUIStyle(EditorStyles.boldLabel)
            { normal = { textColor = new Color(0.4f, 1f, 0.45f) } };
            GUILayout.Label("● LIVE", liveStyle, GUILayout.Width(52));

            if (_liveMachine == null)
                GUILayout.Label("씬에서 PlayerStateMachine을 찾는 중…", EditorStyles.miniLabel);
            else if (_liveConfig == null)
                GUILayout.Label("현재 State가 ConfigState가 아님", EditorStyles.miniLabel);
            else
            {
                GUILayout.Label($"Config: {_liveConfig.name}", GUILayout.Width(180));
                var secStyle = new GUIStyle(EditorStyles.boldLabel)
                { normal = { textColor = new Color(1f, 0.85f, 0.3f) } };
                GUILayout.Label($"▶ {(_liveSection ?? "-")}", secStyle, GUILayout.Width(160));
                GUILayout.Label($"nt={Mathf.Repeat(_liveNt, 1f):F2}", GUILayout.Width(70));
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 2행: 입력 상태 + config 동기화 버튼
            EditorGUILayout.BeginHorizontal();
            if (_liveMachine != null)
            {
                GUILayout.Label($"Move: {_liveMove}", GUILayout.Width(120));
                var bufStyle = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = _liveHasBuffered ? InputColor(_liveBuffered) : Color.gray } };
                GUILayout.Label($"Buffered: {(_liveHasBuffered ? _liveBuffered.ToString() : "-")}",
                    bufStyle, GUILayout.Width(150));

                // Held: 지금 눌려 있는 키 (OnRelease 차지 디버그용). Buffered는 1프레임만 떴다 소비돼
                // 안 보이지만, Held는 누르고 있는 내내 켜져 있다 떼면 꺼진다.
                bool enhanceHeld = _liveMachine.IsInputHeld(ComboInput.Attack_Normal_Enhance);
                var heldStyle = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = enhanceHeld ? InputColor(ComboInput.Attack_Normal_Enhance) : Color.gray } };
                GUILayout.Label($"Held: {(enhanceHeld ? "Attack_Normal_Enhance" : "-")}",
                    heldStyle, GUILayout.Width(200));
            }

            // Follow: 런타임 config 자동 추적 (ON이면 전환 시 창도 따라가고, 플레이헤드/활성 행으로 자동 스크롤)
            var prevBg = GUI.backgroundColor;
            if (_liveFollow) GUI.backgroundColor = new Color(0.4f, 0.9f, 0.45f);
            _liveFollow = GUILayout.Toggle(_liveFollow, "Follow", "Button", GUILayout.Width(60));
            GUI.backgroundColor = prevBg;

            // Follow OFF이고 런타임과 다른 config를 보고 있으면 수동 전환 버튼
            if (!_liveFollow && _liveConfig != null && _liveConfig != _config)
            {
                if (GUILayout.Button("이 Config 표시", GUILayout.Width(110)))
                    ShowConfig(_liveConfig);
                GUILayout.Label("(다른 config 표시 중)", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}
