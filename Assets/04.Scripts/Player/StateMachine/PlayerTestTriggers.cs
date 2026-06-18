using UnityEngine;
using UnityEngine.InputSystem;

namespace ZZZ.Player.StateMachine
{
    // 테스트용 트리거 — 실제 적/공격 시스템이 없는 동안 키보드로 피격/예고공격을 시뮬레이션한다.
    //   H = 등 뒤(Back) 피격,  J = 정면(Front) 피격
    //   K = 예고된 적 공격 — 윈도우를 열고 끝에 적중. 그 사이 회피하면 퍼펙트(i-frame 중 자동 무시).
    // 프로덕션에서는 이 컴포넌트를 제거하거나 비활성화하면 된다 (PlayerStateMachine은 안 건드림).
    [RequireComponent(typeof(PlayerStateMachine))]
    public class PlayerTestTriggers : MonoBehaviour
    {
        [SerializeField] private float _telegraphWindow = 0.4f;

        private PlayerStateMachine _machine;
        private float              _pendingHitAt = -1f;

        private void Awake() => _machine = GetComponent<PlayerStateMachine>();

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.hKey.wasPressedThisFrame) _machine.TriggerHit("Back");
                if (kb.jKey.wasPressedThisFrame) _machine.TriggerHit("Front");

                if (kb.kKey.wasPressedThisFrame)
                {
                    _machine.OpenIncomingAttack(_telegraphWindow);
                    _pendingHitAt = Time.time + _telegraphWindow;
                }
            }

            if (_pendingHitAt > 0f && Time.time >= _pendingHitAt)
            {
                _pendingHitAt = -1f;
                _machine.TriggerHit("Front");   // 적중 (i-frame 중이면 자동 무시 = 회피 성공)
            }
        }
    }
}
