using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using ZZZ;

using ZZZ.Combat;

namespace ZZZ.Monster
{
    [RequireComponent(typeof(MonsterMotor))]
    [RequireComponent(typeof(HitTarget))]
    [RequireComponent(typeof(CharacterAnimatorBridge))]
    [MovedFrom(true, "ZZZ.Monster", "Assembly-CSharp", "MonsterStateMachine")]
    public class MonsterActionController : MonoBehaviour, ICharacterActionSignals,
        ILiveMonitor, IHitSource, IHitLagTarget
    {
        [Header("State Configs")]
        [SerializeField] private AnimationConfig _idleConfig;
        [SerializeField] private AnimationConfig _hitConfig;
        [SerializeField] private AnimationConfig _attackConfig;
        [SerializeField] private AnimationConfig _walkBackConfig;

        [Header("Attack Sections")]
        [SerializeField] private string _largeAngleAttackSection =
            "Monster_Durahan_Ani_Attack_01_03";

        [Header("Hit Sections")]
        [SerializeField] private string _hitFrontSection = "Hit_Front";
        [SerializeField] private string _hitBackSection = "Hit_Back";

        [Header("Poise")]
        [SerializeField] private float _hitStunCooldown = 0.05f;

        [Header("Hit Debug")]
        [SerializeField] private bool _showHitGizmos;
        [SerializeField, Min(0f)] private float _hitGizmoDuration = 0.1f;

        private CharacterActionRunner _runner;
        private MonsterMotor _motor;
        private CharacterAnimatorBridge _animatorBridge;
        private HitTarget _hitTarget;
        private float _nextHitStunTime;
        private float _hitLagSpeed = 1f;

        public event Action<string> HitRequested;

        public MonsterConditionContext ConditionContext { get; private set; }
        public bool IsCurrentActionComplete =>
            _runner != null && _runner.IsCurrentSectionComplete;
        public bool Invulnerable { get; set; }
        public bool ParryActive { get; set; }
        public CombatTeam Team => CombatTeam.Enemy;
        public float HitLagSpeed => _hitLagSpeed;
        public void ConsumeInput() { }

        public AnimationConfig CurrentConfig => _runner?.CurrentConfig;
        public int CurrentClipIndex => _runner?.ActiveIndex ?? -1;
        public string CurrentSection => _runner?.ActiveSection;
        public float CurrentNormalizedTime => _runner?.CurrentNormalizedTime ?? 0f;
        public MoveDir CurrentMoveDir => _runner?.CurrentMoveDir ?? MoveDir.Any;

        private void Awake()
        {
            _motor = GetComponent<MonsterMotor>();
            _animatorBridge = GetComponent<CharacterAnimatorBridge>();
            var context = new CharacterActionContext
            {
                Mover = _motor,
                Animator = _animatorBridge,

                Transform = transform,
            };

            ConditionContext = new MonsterConditionContext();
            _runner = new CharacterActionRunner(
                context, this, ConditionContext, _idleConfig,
                _showHitGizmos, _hitGizmoDuration);

            _hitTarget = GetComponent<HitTarget>();
            _hitTarget.OnDamaged += OnDamaged;
            EffectOwnership.Register(
                this, _idleConfig, _hitConfig, _attackConfig, _walkBackConfig);
        }

        private void OnDestroy()
        {
            if (_hitTarget != null)
                _hitTarget.OnDamaged -= OnDamaged;
            EffectOwnership.Unregister(
                this, _idleConfig, _hitConfig, _attackConfig, _walkBackConfig);
        }

        private void Update()
        {
            _runner.SetHitDebug(_showHitGizmos, _hitGizmoDuration);
            _runner.Update(_hitLagSpeed);
        }

        public void SetHitLagSpeed(float speed)
        {
            _hitLagSpeed = Mathf.Max(0f, speed);
            _animatorBridge.ApplySpeedMultiplier(_hitLagSpeed);
            _motor.LocalTimeScale = _hitLagSpeed;
        }

        public bool TryPlayIdle()
        {
            return TryPlay(_idleConfig);
        }

        public bool TryPlayAttack(Transform target)
        {
            _motor.SetTarget(target);
            return TryPlay(_attackConfig);
        }

        public bool TryPlayLargeAngleAttack(Transform target)
        {
            if (_attackConfig == null
                || _attackConfig.IndexOfSection(_largeAngleAttackSection) < 0)
                return false;

            _motor.SetTarget(target);
            return TryPlay(_attackConfig, _largeAngleAttackSection);
        }

        public void SetTarget(Transform target)
        {
            if (_motor == null)
                _motor = GetComponent<MonsterMotor>();
            _motor.SetTarget(target);
        }

        public bool TryPlayWalkBack()
        {
            return TryPlay(_walkBackConfig);
        }

        public bool TryPlayHit(string section)
        {
            return TryPlay(_hitConfig, section);
        }

        private void OnDamaged(float damage, Vector3 hitPoint)
        {
            if (Invulnerable || _hitConfig == null) return;
            if (Time.time < _nextHitStunTime) return;

            _nextHitStunTime = Time.time + _hitStunCooldown;
            Vector3 toHit = hitPoint - transform.position;
            toHit.y = 0f;
            bool isFrontHit = Vector3.Dot(transform.forward, toHit) >= 0f;
            string hitSection = isFrontHit ? _hitFrontSection : _hitBackSection;
            HitRequested?.Invoke(hitSection);
        }

        private bool TryPlay(AnimationConfig config, string section = null)
        {
            if (config == null) return false;

            _runner.InterruptWith(config, section);
            return true;
        }
    }
}
