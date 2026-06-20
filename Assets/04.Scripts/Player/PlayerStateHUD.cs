using UnityEngine;
using UnityEngine.InputSystem;
using ZZZ.Player.StateMachine;

namespace ZZZ.Player
{
    // 런타임 상태 디버그 오버레이.
    // 상태 머신/이동 플래그/입력 버퍼를 화면 좌상단에 표시한다 — 흩어진 bool을 한눈에 보기 위함.
    // 토글 키(기본 F1)로 끄고 켤 수 있다. 플레이어 루트에 붙이면 참조는 자동으로 찾는다.
    [DisallowMultipleComponent]
    public class PlayerStateHUD : MonoBehaviour
    {
        [Header("References (비우면 자동 탐색)")]
        [SerializeField] private PlayerStateMachine _stateMachine;
        [SerializeField] private PlayerController    _controller;

        [Header("Display")]
        [SerializeField] private Key     _toggleKey = Key.F1;
        [SerializeField] private bool    _visible   = true;
        [SerializeField] private Vector2 _origin    = new Vector2(12f, 12f);
        [SerializeField] private float   _width     = 260f;

        private GUIStyle _header, _label;
        private bool     _stylesReady;

        private void Awake()
        {
            if (_stateMachine == null) _stateMachine = GetComponentInParent<PlayerStateMachine>();
            if (_controller   == null) _controller   = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[_toggleKey].wasPressedThisFrame) _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible) return;
            EnsureStyles();

            float lineH = 18f;
            float pad   = 8f;
            // 줄 수에 맞춰 박스 높이 동적 계산
            int lines = 14;
            var rect  = new Rect(_origin.x, _origin.y, _width, lines * lineH + pad * 2f);
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, rect.height - pad * 2f));

            GUILayout.Label($"PLAYER STATE   [{_toggleKey}] 토글", _header);

            if (_stateMachine != null)
            {
                Label("Config",  _stateMachine.CurrentConfig != null ? _stateMachine.CurrentConfig.name : "-");
                Label("Section", _stateMachine.CurrentSection ?? "-");
                LabelBar("nt",   _stateMachine.CurrentNormalizedTime);
                Label("MoveDir", _stateMachine.CurrentMoveDir.ToString());

                string buf = _stateMachine.HasBufferedInput ? _stateMachine.BufferedInput.ToString() : "-";
                LabelColored("Input Buf", buf, _stateMachine.HasBufferedInput ? Color.cyan : Color.grey);

                LabelColored("I-Frame", _stateMachine.Invulnerable ? "INVULN" : "-",
                    _stateMachine.Invulnerable ? Color.yellow : Color.grey);

                LabelColored("Parry", _stateMachine.ParryActive ? "ACTIVE" : "-",
                    _stateMachine.ParryActive ? new Color(0.4f, 0.8f, 1f) : Color.grey);

                string atk = _stateMachine.IncomingAttackActive
                    ? $"PERFECT! ({_stateMachine.IncomingStrength})" : "-";
                LabelColored("Atk Window", atk,
                    _stateMachine.IncomingAttackActive ? new Color(1f, 0.4f, 0.7f) : Color.grey);
            }

            if (_controller != null)
            {
                Label("Speed",    _controller.CurrentSpeed.ToString("F2"));
                LabelColored("Flags", _controller.CurrentFlags.ToString(),
                    _controller.CurrentFlags == PlayerStateFlags.None ? Color.grey : Color.green);

                // 개별 플래그를 색으로 한 줄에 — 켜짐 초록 / 꺼짐 회색
                DrawFlagRow(_controller.CurrentFlags);

                if (_controller.IsRootMotionActive)
                    Label("RootΔ", _controller.LastRootDelta.ToString("F4"));
            }

            GUILayout.EndArea();
        }

        // ── 그리기 헬퍼 ────────────────────────────────────────────
        private void Label(string key, string value)
            => GUILayout.Label($"<b>{key}</b>  {value}", _label);

        private void LabelColored(string key, string value, Color c)
        {
            string hex = ColorUtility.ToHtmlStringRGB(c);
            GUILayout.Label($"<b>{key}</b>  <color=#{hex}>{value}</color>", _label);
        }

        private void LabelBar(string key, float t01)
        {
            t01 = Mathf.Clamp01(t01);
            int filled = Mathf.RoundToInt(t01 * 10f);
            string bar = new string('#', filled) + new string('-', 10 - filled);
            GUILayout.Label($"<b>{key}</b>  [{bar}] {t01:F2}", _label);
        }

        private void DrawFlagRow(PlayerStateFlags flags)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (PlayerStateFlags v in System.Enum.GetValues(typeof(PlayerStateFlags)))
            {
                if (v == PlayerStateFlags.None) continue;
                bool on  = (flags & v) != 0;
                string c = on ? "7CFC00" : "606060";   // 초록 / 회색
                sb.Append($"<color=#{c}>{v}</color>  ");
            }
            GUILayout.Label(sb.ToString(), _label);
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _header = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold };
            _label  = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            _stylesReady = true;
        }
    }
}
