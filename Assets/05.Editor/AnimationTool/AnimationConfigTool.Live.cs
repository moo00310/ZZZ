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
        // 플레이 중인 씬에서 ILiveMonitor 머신(플레이어/몬스터)을 골라 현재 config/섹션/시간을 읽어온다
        private void UpdateLiveState()
        {
            RefreshLiveCandidates();
            if (_liveMachine == null) { _liveConfig = null; return; }

            _liveConfig  = _liveMachine.CurrentConfig;
            _liveClipIdx = _liveMachine.CurrentClipIndex;
            _liveSection = _liveMachine.CurrentSection;
            _liveNt      = _liveMachine.CurrentNormalizedTime;
            _liveMove    = _liveMachine.CurrentMoveDir;

            // 입력 모니터는 입력 있는 머신(플레이어)만 구현 — 없으면 입력 상태를 비운다
            var input = _liveMachine as IInputMonitor;
            _liveHasBuffered = input != null && input.HasBufferedInput;
            _liveBuffered    = input != null ? input.BufferedInput : default;

            // Follow ON이면 런타임 config를 자동으로 따라간다 (config가 비어 있으면 무조건 채움)
            if (_liveConfig != null && _liveConfig != _config && (_liveFollow || _config == null))
                ShowConfig(_liveConfig);

            Repaint();
        }

        // 씬의 ILiveMonitor 구현체를 모아 드롭다운 선택지를 갱신.
        // 선택 머신이 사라졌거나 아직 없으면 첫 후보로 폴백한다.
        private void RefreshLiveCandidates()
        {
            _liveCandidates.Clear();
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (mb is ILiveMonitor m) _liveCandidates.Add(m);

            if (_liveMachine == null || !_liveCandidates.Contains(_liveMachine))
                _liveMachine = _liveCandidates.Count > 0 ? _liveCandidates[0] : null;
        }

        // ILiveMonitor를 들고 있는 GameObject 이름 (드롭다운 표시용)
        private static string LiveName(ILiveMonitor m)
            => m is MonoBehaviour mb && mb != null ? mb.gameObject.name : "(none)";

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

            // 추적 대상 선택 — 씬의 플레이어/몬스터 머신 중 하나를 고른다
            DrawLiveTargetSelector();

            if (_liveMachine == null)
                GUILayout.Label("씬에서 추적할 캐릭터(PlayerStateMachine/MonsterStateMachine)를 찾는 중…",
                    EditorStyles.miniLabel);
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

                // Buffered/Held는 입력 있는 머신(플레이어)만 표시 — 몬스터는 IInputMonitor 미구현이라 생략.
                if (_liveMachine is IInputMonitor input)
                {
                    var bufStyle = new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = _liveHasBuffered ? InputColor(_liveBuffered) : Color.gray } };
                    GUILayout.Label($"Buffered: {(_liveHasBuffered ? _liveBuffered.ToString() : "-")}",
                        bufStyle, GUILayout.Width(150));

                    // Held: 지금 눌려 있는 키 (OnRelease 차지 디버그용). Buffered는 1프레임만 떴다 소비돼
                    // 안 보이지만, Held는 누르고 있는 내내 켜져 있다 떼면 꺼진다.
                    bool enhanceHeld = input.IsInputHeld(ComboInput.Enhance);
                    var heldStyle = new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = enhanceHeld ? InputColor(ComboInput.Enhance) : Color.gray } };
                    GUILayout.Label($"Held: {(enhanceHeld ? "Enhance" : "-")}",
                        heldStyle, GUILayout.Width(200));
                }
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

        // 추적 대상 드롭다운 — 씬의 ILiveMonitor 머신(플레이어/몬스터)을 이름으로 골라 _liveMachine 교체.
        // 후보가 하나뿐이면 굳이 선택할 게 없으니 이름만 보여준다.
        private void DrawLiveTargetSelector()
        {
            int count = _liveCandidates.Count;
            if (count == 0) return;

            if (count == 1)
            {
                GUILayout.Label(LiveName(_liveCandidates[0]), EditorStyles.miniBoldLabel,
                    GUILayout.Width(120));
                return;
            }

            int cur = _liveCandidates.IndexOf(_liveMachine);
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = LiveName(_liveCandidates[i]);

            int next = EditorGUILayout.Popup(Mathf.Max(cur, 0), names, GUILayout.Width(140));
            if (next != cur && next >= 0 && next < count)
            {
                _liveMachine = _liveCandidates[next];
                // 새 대상으로 즉시 전환 — Follow면 다음 갱신에서 그 머신의 config로 창이 따라간다
                _liveConfig = null;
            }
        }
    }
}
