// 디버그 HUD — 에디터/개발 빌드에서만 컴파일. 릴리스 빌드에선 통째로 제외해
// 빌드 용량과 매 프레임 OnGUI 문자열 GC를 없앤다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
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
        [FormerlySerializedAs("_stateMachine")]
        [SerializeField] private PlayerActionController _actionController;
        [FormerlySerializedAs("_controller")]
        [SerializeField] private PlayerMotor _motor;

        [Header("Display")]
        [SerializeField] private Key     _toggleKey = Key.F1;
        [SerializeField] private bool    _visible   = true;
        [SerializeField] private Vector2 _origin    = new Vector2(12f, 12f);
        [SerializeField] private float   _width     = 260f;

        private GUIStyle _header, _label;
        private bool     _stylesReady;

        private void Awake()
        {
            if (_actionController == null)
                _actionController = GetComponentInParent<PlayerActionController>();
            if (_motor == null) _motor = GetComponentInParent<PlayerMotor>();
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

            if (_actionController != null)
            {
                Label("Config", _actionController.CurrentConfig != null
                    ? _actionController.CurrentConfig.name : "-");
                Label("Section", _actionController.CurrentSection ?? "-");
                LabelBar("nt", _actionController.CurrentNormalizedTime);
                Label("MoveDir", _actionController.CurrentMoveDir.ToString());

                string buf = _actionController.HasBufferedInput
                    ? _actionController.BufferedInput.ToString() : "-";
                LabelColored("Input Buf", buf,
                    _actionController.HasBufferedInput ? Color.cyan : Color.grey);

                LabelColored("I-Frame", _actionController.Invulnerable ? "INVULN" : "-",
                    _actionController.Invulnerable ? Color.yellow : Color.grey);

                LabelColored("Parry", _actionController.ParryActive ? "ACTIVE" : "-",
                    _actionController.ParryActive
                        ? new Color(0.4f, 0.8f, 1f) : Color.grey);

                string atk = _actionController.IncomingAttackActive
                    ? $"PERFECT! ({_actionController.IncomingStrength})" : "-";
                LabelColored("Atk Window", atk,
                    _actionController.IncomingAttackActive
                        ? new Color(1f, 0.4f, 0.7f) : Color.grey);
            }

            if (_motor != null)
            {
                Label("Speed", _motor.CurrentSpeed.ToString("F2"));
                LabelColored("Flags", _motor.CurrentFlags.ToString(),
                    _motor.CurrentFlags == PlayerMotorFlags.None ? Color.grey : Color.green);

                // 개별 플래그를 색으로 한 줄에 — 켜짐 초록 / 꺼짐 회색
                DrawFlagRow(_motor.CurrentFlags);

                if (_motor.IsRootMotionActive)
                    Label("RootΔ", _motor.LastRootDelta.ToString("F4"));
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

        private void DrawFlagRow(PlayerMotorFlags flags)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (PlayerMotorFlags v in System.Enum.GetValues(typeof(PlayerMotorFlags)))
            {
                if (v == PlayerMotorFlags.None) continue;
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
#endif
