using UnityEngine;
using ZZZ;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    // 피격 반응 트리거 — 외부 이벤트(충돌/적 공격)로 Hit config에 강제 진입한다.
    //   A. 재진입 가드 — 이미 피격 중이고 진행도가 임계값 미만이면 무시(연타 stunlock/프리즈 방지).
    //   escalation — 연속타 카운트로 강도 승격: 홀수타=L, 짝수타=H 교대.
    public class HitTrigger
    {
        private readonly PlayerStateMachine _machine;
        private readonly ConfigState        _state;
        private readonly ConfigRegistry     _registry;
        private readonly float              _reinterruptThreshold;
        private readonly float              _entryBlend;   // 진입 CrossFade(초). 크면 클립 앞부분 넉백 root motion이 먹힘 → 작게.
        private readonly string             _parryPrefix;  // 쳐냄 섹션 접두어 (Attack_ParryAid_ → +L/H)

        // 연속 피격 카운트 — hit이 아닌 상태에서 새로 맞으면 0으로 리셋.
        private int _comboHitCount;

        public HitTrigger(PlayerStateMachine machine, ConfigState state, ConfigRegistry registry,
            float reinterruptThreshold, float entryBlend, string parryPrefix)
        {
            _machine              = machine;
            _state                = state;
            _registry             = registry;
            _reinterruptThreshold = reinterruptThreshold;
            _entryBlend           = entryBlend;
            _parryPrefix          = parryPrefix;
        }

        // 충돌 검출에서 호출 — 공격자 위치로 Front/Back 판정 후 진입.
        public void TriggerFrom(Vector3 attackerPos, Transform self)
        {
            Vector3 to = attackerPos - self.position;
            bool back = Vector3.Dot(self.forward, to) < 0f;   // 등 뒤에서 맞음
            Trigger(back ? "Back" : "Front");
        }

        //   direction : 반응 방향("Front"/"Back") → 섹션 이름에 사용
        public void Trigger(string direction = "Back")
        {
            if (_machine.Invulnerable) return;   // 회피 i-frame 중이면 피격 무시

            // 패링 활성 중이면 피격 대신 쳐냄(deflect)으로 분기 — 적 공격 강도로 L/H 결정.
            // 카운터 follow-up은 ParryAid_L/H config의 Link(Attack=Normal → Counter)가 처리한다.
            if (_machine.ParryActive && TryDeflect()) return;

            // 등록된 config 중 이번 피격 섹션(L/H 둘 다 같은 config에 있음)을 가진 것을 찾는다.
            AnimationConfig hitConfig = _registry.FindWithSection($"Hit_L_{direction}")
                                     ?? _registry.FindWithSection($"Hit_H_{direction}");
            if (hitConfig == null)
            {
                Debug.LogWarning($"[Hit] 'Hit_*_{direction}' 섹션을 가진 config가 없음 — PlayerStateMachine 인스펙터 'Configs' 리스트 확인", _machine);
                return;
            }

            bool inHit = _state.CurrentConfig == hitConfig;

            // A. 재진입 가드: 피격 반응이 충분히 진행되기 전엔 새 피격 무시
            if (inHit && _state.CurrentNormalizedTime < _reinterruptThreshold)
                return;

            // 새 피격(걷기 등에서 진입)이면 콤보 리셋, 연속타면 누적
            if (!inHit) _comboHitCount = 0;
            _comboHitCount++;

            string strength = (_comboHitCount % 2 == 1) ? "L" : "H";   // 홀수타=L, 짝수타=H 교대 → 연출 풍부
            _state.InterruptWith(hitConfig, $"Hit_{strength}_{direction}", _entryBlend);
        }

        // 패링 성공 — 적 공격 강도로 ParryAid_L/H 진입. 섹션 config가 없으면 false(일반 피격으로 폴백).
        private bool TryDeflect()
        {
            string strength = _machine.IncomingStrength == AttackStrength.Heavy ? "H" : "L";
            string section  = _parryPrefix + strength;
            var parryConfig = _registry.FindWithSection(section);
            if (parryConfig == null)
            {
                Debug.LogWarning($"[Parry] '{section}' 섹션을 가진 config가 없음 — PlayerStateMachine 'Configs' 리스트 확인", _machine);
                return false;
            }

            _comboHitCount = 0;   // 쳐냄은 피격 콤보가 아니므로 리셋
            _state.InterruptWith(parryConfig, section, _entryBlend);
            return true;
        }
    }
}
