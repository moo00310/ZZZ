using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting.APIUpdating;

namespace ZZZ.Agent
{
    // 테스트용 트리거 — 실제 적/공격 시스템이 없는 동안 키보드로 피격/예고공격을 시뮬레이션한다.
    //   H = 등 뒤(Back) 피격,  J = 정면(Front) 피격
    //   K = 예고된 약공(Light) — 윈도우를 열고 끝에 적중. 그 사이 회피=퍼펙트 / 패링 스탠스 중이면 쳐냄(ParryAid_L).
    //   L = 예고된 강공(Heavy) — 위와 같으나 패링 시 ParryAid_H로 쳐냄.
    //   P = 패링 스탠스 진입(ParryAid_Start) — 실제 플레이는 Parry 입력 버튼으로 진입.
    // 프로덕션에서는 이 컴포넌트를 제거하거나 비활성화하면 된다.
    [RequireComponent(typeof(AgentActionController))]
    [MovedFrom(true, "ZZZ.Player.StateMachine", "Assembly-CSharp", "PlayerTestTriggers")]
    public class AgentTestTriggers : MonoBehaviour
    {
        [SerializeField] private float _telegraphWindow = 0.4f;

        private AgentActionController _controller;
        private float              _pendingHitAt = -1f;

        private void Awake() => _controller = GetComponent<AgentActionController>();

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.hKey.wasPressedThisFrame) _controller.TriggerHit("Back");
                if (kb.jKey.wasPressedThisFrame) _controller.TriggerHit("Front");
                if (kb.pKey.wasPressedThisFrame) _controller.TriggerParry();

                if (kb.kKey.wasPressedThisFrame) TelegraphAttack(AttackStrength.Light);
                if (kb.lKey.wasPressedThisFrame) TelegraphAttack(AttackStrength.Heavy);
            }

            if (_pendingHitAt > 0f && Time.time >= _pendingHitAt)
            {
                _pendingHitAt = -1f;
                // 적중 — i-frame 중이면 무시(회피 성공) / 패링 활성 중이면 쳐냄(deflect).
                _controller.TriggerHit("Front");
            }
        }

        private void TelegraphAttack(AttackStrength strength)
        {
            _controller.OpenIncomingAttack(_telegraphWindow, strength);
            _pendingHitAt = Time.time + _telegraphWindow;
        }
    }
}
