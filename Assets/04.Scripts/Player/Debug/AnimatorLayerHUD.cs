using UnityEngine;
using UnityEngine.InputSystem;

namespace ZZZ.Player.Debugging
{
    // 런타임 디버그용 — Animator의 레이어별 weight / 현재 State / normalizedTime을
    // 화면 좌상단에 실시간 표시한다. Additive 레이어가 실제로 켜지고(weight>0)
    // 해당 클립이 재생 중인지 눈으로 확인하는 용도.
    // 사용법: 플레이어(Animator가 있는 GameObject)에 컴포넌트로 붙이기.
    [RequireComponent(typeof(Animator))]
    public class AnimatorLayerHUD : MonoBehaviour
    {
        [SerializeField] private bool _show = true;
        [SerializeField] private int  _fontSize = 16;

        private Animator _animator;
        private GUIStyle _style;

        private void Awake() => _animator = GetComponent<Animator>();

        private void Update()
        {
            // F1로 토글
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                _show = !_show;
        }

        private void OnGUI()
        {
            if (!_show || _animator == null) return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize  = _fontSize,
                richText  = true,
                alignment = TextAnchor.UpperLeft,
            };

            int   count = _animator.layerCount;
            float lineH = _fontSize + 8f;
            float boxH  = lineH * (count * 2 + 1) + 16f;

            GUI.Box(new Rect(8, 8, 520, boxH), GUIContent.none);

            float y = 14f;
            DrawLine(ref y, lineH, $"<b>Animator Layers ({count})</b>   [F1 토글]");

            for (int i = 0; i < count; i++)
            {
                float w   = _animator.GetLayerWeight(i);
                var   st  = _animator.GetCurrentAnimatorStateInfo(i);
                bool  inT = _animator.IsInTransition(i);
                string name = _animator.GetLayerName(i);

                // weight 색상: 0이면 회색, >0이면 초록
                string wCol = w > 0.001f ? "#6f6" : "#888";
                string blend = i == 0 ? "" : "  (additive 레이어로 추정)";

                DrawLine(ref y, lineH,
                    $"<b>L{i}</b> '{name}'{blend}  weight=<color={wCol}>{w:F2}</color>{(inT ? "  <color=#fc6>[transition]</color>" : "")}");

                DrawLine(ref y, lineH,
                    $"     nt={st.normalizedTime % 1f:F2}  len={st.length:F2}s  hash={st.shortNameHash}  loop={st.loop}");
            }
        }

        private void DrawLine(ref float y, float lineH, string text)
        {
            GUI.Label(new Rect(16, y, 500, lineH), text, _style);
            y += lineH;
        }
    }
}
