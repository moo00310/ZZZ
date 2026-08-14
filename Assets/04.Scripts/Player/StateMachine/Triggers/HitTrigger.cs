using UnityEngine;
using ZZZ;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    // 피격 반응 트리거 — 외부 이벤트(충돌/적 공격)로 Hit config에 강제 진입한다.
    //   A. 재진입 가드 — 이미 피격 중이고 진행도가 임계값 미만이면 무시(연타 stunlock/프리즈 방지).
    //   escalation — 연속타 카운트로 강도 승격: 홀수타=L, 짝수타=H 교대.
    // 설정은 이 객체가 직접 들고, 런타임 의존은 Init으로 주입한다.
    // _parryPrefix(쳐냄 섹션 접두어)는 ParryTrigger와 한 곳에서만 정의하려고 Init으로 받는다.
    [System.Serializable]
    public class HitTrigger
    {
        [Tooltip("이미 피격 중일 때, 반응 진행도가 이 값을 넘어야 새 피격이 재시작 (재진입 가드)")]
        [SerializeField, Range(0f, 1f)] private float _reinterruptThreshold = 0.3f;
        [Tooltip("피격 진입 CrossFade(초). 전이 중 root motion이 버려지므로 작게 둘 것")]
        [SerializeField, Range(0f, 0.2f)] private float _entryBlend = 0.03f;

        // ── 런타임 의존 (직렬화 안 함, Init으로 주입) ──
        private PlayerActionController _controller;
        private ConfigState        _state;
        private ConfigRegistry     _registry;
        private string             _parryPrefix;  // 쳐냄 섹션 접두어 (Attack_ParryAid_ → +L/H) — ParryTrigger와 공유

        // 연속 피격 카운트 — hit이 아닌 상태에서 새로 맞으면 0으로 리셋.
        private int _comboHitCount;

        public void Init(PlayerActionController controller, ConfigState state,
            ConfigRegistry registry, string parryPrefix)
        {
            _controller  = controller;
            _state       = state;
            _registry    = registry;
            _parryPrefix = parryPrefix;
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
            if (_controller.Invulnerable) return;   // 회피 i-frame 중이면 피격 무시

            // 패링 활성 중이면 피격 대신 쳐냄(deflect)으로 분기 — 적 공격 강도로 L/H 결정.
            // 카운터 follow-up은 ParryAid_L/H config의 Link(Attack=Normal → Counter)가 처리한다.
            if (_controller.ParryActive && TryDeflect()) return;

            // 등록된 config 중 이번 피격 섹션(L/H 둘 다 같은 config에 있음)을 가진 것을 찾는다.
            AnimationConfig hitConfig = _registry.FindWithSection($"Hit_L_{direction}")
                                     ?? _registry.FindWithSection($"Hit_H_{direction}");
            if (hitConfig == null)
            {
                Debug.LogWarning($"[Hit] 'Hit_*_{direction}' 섹션을 가진 config가 없음 — PlayerActionController 인스펙터 'Configs' 리스트 확인", _controller);
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
            string strength = _controller.IncomingStrength == AttackStrength.Heavy ? "H" : "L";
            string section  = _parryPrefix + strength;
            var parryConfig = _registry.FindWithSection(section);
            if (parryConfig == null)
            {
                Debug.LogWarning($"[Parry] '{section}' 섹션을 가진 config가 없음 — PlayerActionController 'Configs' 리스트 확인", _controller);
                return false;
            }

            _comboHitCount = 0;   // 쳐냄은 피격 콤보가 아니므로 리셋
            _state.InterruptWith(parryConfig, section, _entryBlend);
            return true;
        }
    }
}
