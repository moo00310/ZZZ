using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ZZZ.Player.StateMachine;

namespace ZZZ.Player.Debugging
{
    // Additive 적용 전/후 비교용 디버그 도구.
    //
    //   I : 격리 모드 ON/OFF — FSM/이동 스크립트를 멈추고 base를 한 포즈(Idle)로 고정.
    //       다른 State가 layer 0을 계속 바꾸는 방해 없이 깨끗하게 비교하려면 먼저 I를 켤 것.
    //   T : additive ON/OFF 토글 (weight 0↔1 부드럽게 보간)
    //   [ / ] : weight 수동 미세 조정
    //
    // 사용법: 플레이어(Animator 있는 GameObject)에 붙이고 Play → I로 격리 → T로 비교.
    [RequireComponent(typeof(Animator))]
    public class AdditiveCompareTool : MonoBehaviour
    {
        [Header("Additive")]
        [SerializeField] private int    _layer         = 1;
        [SerializeField] private string _additiveState = "Avatar_Female_Size02_Burnice_Ani_Hit_Shake";
        [SerializeField] private float  _fadeSpeed     = 8f;   // weight 보간 속도

        [Header("Isolation")]
        [SerializeField] private string _baseState = "Avatar_Female_Size02_Burnice_Ani_Idle"; // 격리 시 layer 0 고정 포즈

        private Animator _animator;
        private bool     _on;
        private float    _current;
        private float    _addTime;   // additive 클립 재생 누적 시간 (수동 루프용)
        private float    _clipLen = 0.5f;
        private GUIStyle _style;

        // 격리 모드: 끄고 다시 켤 컴포넌트 목록 (FSM/이동 등 layer 0을 건드리는 것들)
        private bool _isolated;
        private readonly List<Behaviour> _suspended = new();

        private void Awake() => _animator = GetComponent<Animator>();

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.iKey.wasPressedThisFrame) ToggleIsolation();
                if (kb.tKey.wasPressedThisFrame)
                {
                    _on = !_on;
                    if (_on) _addTime = 0f;   // 켤 때 클립 처음부터
                }
                if (kb.leftBracketKey.wasPressedThisFrame)  AdjustManual(-0.1f);
                if (kb.rightBracketKey.wasPressedThisFrame) AdjustManual(+0.1f);
            }

            float target = _on ? 1f : 0f;
            _current = Mathf.MoveTowards(_current, target, _fadeSpeed * Time.deltaTime);
            _animator.SetLayerWeight(_layer, _current);

            // 격리 중엔 base가 다른 곳에서 안 바뀌므로 Idle을 계속 유지
            if (_isolated && !_animator.GetCurrentAnimatorStateInfo(0).IsName(_baseState))
                _animator.CrossFade(_baseState, 0.1f, 0);

            DriveAdditive();
        }

        // additive 레이어를 시간으로 직접 굴려 강제 루프시킨다.
        // (클립/State가 non-looping이어도 항상 반복 — Animator transition 설정에 의존 안 함)
        private void DriveAdditive()
        {
            if (_current <= 0.001f) return;   // 꺼져 있으면 굴릴 필요 없음

            // 현재 레이어가 additive State를 재생 중이면 실제 클립 길이를 갱신
            var st = _animator.GetCurrentAnimatorStateInfo(_layer);
            if (st.IsName(_additiveState) && st.length > 0.01f)
                _clipLen = st.length;

            _addTime += Time.deltaTime;
            float nt = (_addTime / Mathf.Max(0.01f, _clipLen)) % 1f;

            // normalizedTime을 직접 지정 → 끝에 도달하면 0으로 wrap되어 끊김 없이 반복
            _animator.Play(_additiveState, _layer, nt);
        }

        // FSM·이동·로코모션 컨트롤러를 멈춰 layer 0을 한 포즈로 고정한다.
        private void ToggleIsolation()
        {
            _isolated = !_isolated;

            if (_isolated)
            {
                Suspend(GetComponent<PlayerStateMachine>());
                Suspend(GetComponent<PlayerAnimatorController>());
                Suspend(GetComponent<PlayerController>());
                _animator.CrossFade(_baseState, 0.1f, 0);   // base를 Idle로 고정
            }
            else
            {
                foreach (var b in _suspended)
                    if (b != null) b.enabled = true;
                _suspended.Clear();
            }
        }

        private void Suspend(Behaviour b)
        {
            if (b == null || !b.enabled) return;
            b.enabled = false;
            _suspended.Add(b);
        }

        private void AdjustManual(float delta)
        {
            _on = true;
            _current = Mathf.Clamp01(_current + delta);
        }

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };

            string iso  = _isolated ? "<color=#6cf>격리 ON</color>" : "<color=#888>격리 OFF</color>";
            string addv = _on ? "<color=#6f6>ON</color>" : "<color=#888>OFF</color>";

            GUI.Box(new Rect(8, Screen.height - 44f, 720, 34), GUIContent.none);
            GUI.Label(new Rect(16, Screen.height - 40f, 700, 28),
                $"[Additive 비교]  I={iso}   T=additive({addv})   [ ]=미세조정   weight=<b>{_current:F2}</b>   L{_layer} '{System.IO.Path.GetFileName(_additiveState)}'",
                _style);
        }
    }
}
