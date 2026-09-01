using UnityEngine;
using ZZZ;

namespace ZZZ.Agent
{
    // 피격 반응 트리거 — 외부 이벤트(충돌/적 공격)로 Hit config에 강제 진입한다.
    //   A. 재진입 가드 — 이미 피격 중이고 진행도가 임계값 미만이면 무시(연타 stunlock/프리즈 방지).
    //   반응 선택 — 공격 강도와 공격자 방향으로 피격 섹션을 결정한다.
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
        private AgentActionController _controller;
        private CharacterActionRunner        _runner;
        private ConfigRegistry     _registry;
        private string             _parryPrefix;  // 쳐냄 섹션 접두어 (Attack_ParryAid_ → +L/H) — ParryTrigger와 공유

        public void Init(AgentActionController controller, CharacterActionRunner runner,
            ConfigRegistry registry, string parryPrefix)
        {
            _controller  = controller;
            _runner       = runner;
            _registry    = registry;
            _parryPrefix = parryPrefix;
        }

        // 충돌 검출에서 호출 — 공격자 위치로 Front/Back 판정 후 진입.
        public bool TriggerFrom(Vector3 attackerPos, Transform self)
        {
            Vector3 to = attackerPos - self.position;
            bool back = Vector3.Dot(self.forward, to) < 0f;   // 등 뒤에서 맞음
            return Trigger(back ? "Back" : "Front");
        }

        //   direction : 반응 방향("Front"/"Back") → 섹션 이름에 사용
        public bool Trigger(string direction = "Back")
        {
            if (_controller.Invulnerable) return false;   // 회피 i-frame 중이면 피격 무시

            // 패링 활성 중이면 피격 대신 쳐냄(deflect)으로 분기 — 적 공격 강도로 L/H 결정.
            // 카운터 follow-up은 ParryAid_L/H config의 Link(Attack=Normal → Counter)가 처리한다.
            if (_controller.ParryActive && TryDeflect()) return true;

            string section = ResolveHitSection(direction, out AnimationConfig hitConfig);
            if (hitConfig == null)
            {
                Debug.LogWarning(
                    $"[Hit] '{section}' 또는 대체 피격 섹션을 가진 config가 없음 — "
                    + "AgentActionController 인스펙터 'Configs' 리스트 확인",
                    _controller);
                return false;
            }

            bool inHit = _runner.CurrentConfig == hitConfig;

            // A. 재진입 가드: 피격 반응이 충분히 진행되기 전엔 새 피격 무시
            bool enteringHitFly = IsHitFlySection(section);
            bool alreadyInHitFly = IsHitFlySection(_runner.ActiveSection);
            if (inHit && _runner.CurrentNormalizedTime < _reinterruptThreshold
                && (!enteringHitFly || alreadyInHitFly))
                return false;

            _runner.InterruptWith(hitConfig, section, _entryBlend);
            return false;
        }

        private string ResolveHitSection(
            string direction, out AnimationConfig hitConfig)
        {
            bool heavy = _controller.IncomingStrength == AttackStrength.Heavy;
            string section = heavy
                ? $"HitFly_{direction}"
                : $"Hit_L_{direction}";
            hitConfig = _registry.FindWithSection(section);
            if (hitConfig != null) return section;

            // HitFly가 없는 캐릭터도 기존 피격 Config로 계속 동작하게 한다.
            section = $"Hit_H_{direction}";
            hitConfig = _registry.FindWithSection(section);
            if (hitConfig != null) return section;

            section = $"Hit_L_{direction}";
            hitConfig = _registry.FindWithSection(section);
            return section;
        }

        private static bool IsHitFlySection(string section) =>
            !string.IsNullOrEmpty(section) && section.StartsWith("HitFly_");

        // 패링 성공 — 적 공격 강도로 ParryAid_L/H 진입. 섹션 config가 없으면 false(일반 피격으로 폴백).
        private bool TryDeflect()
        {
            string strength = _controller.IncomingStrength == AttackStrength.Heavy ? "H" : "L";
            string section  = _parryPrefix + strength;
            var parryConfig = _registry.FindWithSection(section);
            if (parryConfig == null)
            {
                Debug.LogWarning($"[Parry] '{section}' 섹션을 가진 config가 없음 — AgentActionController 'Configs' 리스트 확인", _controller);
                return false;
            }

            _runner.InterruptWith(parryConfig, section, _entryBlend);
            return true;
        }
    }
}
