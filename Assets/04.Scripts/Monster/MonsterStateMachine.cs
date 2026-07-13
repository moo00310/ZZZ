using UnityEngine;
using ZZZ;
using ZZZ.Combat;
using ZZZ.Player.StateMachine.States;   // ConfigState (현재 위치 — 공유 엔진. 추후 중립 네임스페이스로 이전 가능)

namespace ZZZ.Monster
{
    // 몬스터판 PlayerStateMachine — 입력 없는 얇은 코디네이터.
    // ConfigState(공유 엔진)를 소유·구동하고, 피격 이벤트로 Hit config에 인터럽트한다.
    // 부품(Controller/AnimatorBridge/Context)만 몬스터 것이고 ConfigState 본체는 플레이어와 동일.
    [RequireComponent(typeof(MonsterController))]
    [RequireComponent(typeof(HitTarget))]
    public class MonsterStateMachine : MonoBehaviour, IConfigSignals, ILiveMonitor
    {
        [Header("Configs")]
        [SerializeField] private AnimationConfig _idleConfig;   // 시작/복귀 기본 (home)
        [SerializeField] private AnimationConfig _hitConfig;    // 피격 반응 (OnEnd로 Idle 복귀하게 작성)

        [Header("Hit 섹션 이름 (Hit config의 섹션과 일치해야 함)")]
        [SerializeField] private string _hitFrontSection = "Hit_Front";
        [SerializeField] private string _hitBackSection  = "Hit_Back";

        [Header("경직 (poise) — A안: 히트 쿨다운")]
        // 인터럽트 직후 이 시간 동안은 Hit 모션을 재시작하지 않는다(무한 경직 락 방지).
        // 데미지(HP 감소)는 매 히트 적용되고, '경직 모션 리셋'만 throttle한다.
        // 후속: C안(Hit config 구간별 슈퍼아머 윈도우)으로 확장 가능 — todo-monster-poise 참고.
        [SerializeField] private float _hitStunCooldown = 0.3f;

        private ConfigState _state;
        private HitTarget   _hitTarget;
        private float       _nextHitStunTime;

        // ── IConfigSignals ── 몬스터는 입력 버퍼/패링 개념이 (아직) 없다.
        public bool Invulnerable { get; set; }
        public bool ParryActive  { get; set; }
        public void ConsumeInput() { }   // 입력 버퍼 없음 → no-op

        // ── ILiveMonitor ── 에디터 라이브 모니터용 읽기 전용 노출 (플레이어와 동일 패턴).
        // 입력 개념이 없으므로 IInputMonitor는 구현하지 않는다 → 라이브바가 입력 행을 자동 생략.
        public AnimationConfig CurrentConfig         => _state?.CurrentConfig;
        public int             CurrentClipIndex      => _state?.ActiveIndex ?? -1;
        public string          CurrentSection        => _state?.ActiveSection;
        public float           CurrentNormalizedTime => _state?.CurrentNormalizedTime ?? 0f;
        public MoveDir         CurrentMoveDir         => _state?.CurrentMoveDir ?? MoveDir.Any;

        private void Awake()
        {
            var controller = GetComponent<MonsterController>();
            // Play/ApplyAnimatorSpeed만 쓰므로 AnimatorBridge를 그대로 붙여 재사용 가능
            // (인터페이스로 받음 — 추후 MonsterAnimatorBridge로 교체해도 무영향).
            var animator   = GetComponent<IAnimatorBridge>();

            var ctx = new ConfigContext
            {
                Mover      = controller,
                Animator   = animator,
                Transform  = transform,
                GameObject = gameObject,
            };
            var condCtx = new MonsterConditionContext();
            _state = new ConfigState(ctx, this, condCtx, _idleConfig);

            _hitTarget = GetComponent<HitTarget>();
            _hitTarget.OnDamaged += OnDamaged;

            // 이 몬스터가 config에서 참조하는 이펙트 프리팹의 소유권 등록 → 전역 풀 프리웜(파괴 시 해제).
            EffectOwnership.Register(this, _idleConfig, _hitConfig);
        }

        private void OnDestroy()
        {
            if (_hitTarget != null) _hitTarget.OnDamaged -= OnDamaged;
            EffectOwnership.Unregister(this, _idleConfig, _hitConfig);
        }

        // Start는 모든 Awake 이후 → AnimatorBridge 초기화 보장
        private void Start()  => _state.Enter();
        private void Update() => _state.Update();

        // 피격 — 맞은 지점이 앞/뒤인지로 Hit 섹션 분기 후 Hit config로 인터럽트.
        // Hit config가 OnEnd Link로 Idle에 복귀하면 자연히 돌아온다.
        private void OnDamaged(float damage, Vector3 hitPoint)
        {
            if (Invulnerable || _hitConfig == null) return;

            // A안 경직 쿨다운: 창이 안 지났으면 데미지만 들어가고 모션 인터럽트는 건너뛴다.
            if (Time.time < _nextHitStunTime) return;
            _nextHitStunTime = Time.time + _hitStunCooldown;

            Vector3 to = hitPoint - transform.position;
            to.y = 0f;
            bool front = Vector3.Dot(transform.forward, to) >= 0f;
            _state.InterruptWith(_hitConfig, front ? _hitFrontSection : _hitBackSection);
        }
    }
}
