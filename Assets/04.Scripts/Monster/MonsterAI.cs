using UnityEngine;

namespace ZZZ.Monster
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MonsterActionController))]
    public sealed class MonsterAI : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private TargetProvider _targetProvider;
        [SerializeField] private Transform _target;
        [SerializeField, Min(0f)] private float _attackRange = 3f;
        [SerializeField, Range(0f, 180f)] private float _largeAngleAttackThreshold = 60f;

        [Header("Decision")]
        [SerializeField, Min(0.01f)] private float _decisionInterval = 0.15f;
        [SerializeField, Min(0f)] private float _initialAttackDelay = 1f;
        [SerializeField, Min(0f)] private float _attackCooldown = 2f;

        private MonsterActionController _actions;
        private MonsterRegistry _registry;
        private MonsterFsm _fsm;
        private bool _decisionEnabled = true;

        public Transform Target => _target;
        public MonsterActionState CurrentState => _fsm?.CurrentState
            ?? MonsterActionState.Idle;
        public bool DecisionEnabled => _decisionEnabled;

        private void Awake()
        {
            _actions = GetComponent<MonsterActionController>();
        }

        private void OnEnable()
        {
            _actions.HitRequested += OnHitRequested;
            if (_targetProvider != null)
            {
                _targetProvider.TargetChanged += SetTarget;
                _target = _targetProvider.CurrentTarget;
            }
            SetTarget(_target);
            RegisterWithRegistry();
        }

        private void OnDisable()
        {
            if (_registry != null)
                _registry.Unregister(this);

            _actions.HitRequested -= OnHitRequested;
            if (_targetProvider != null)
                _targetProvider.TargetChanged -= SetTarget;
        }

        private void Start()
        {
            _fsm = new MonsterFsm(
                _actions,
                _actions.ConditionContext,
                _attackRange,
                _largeAngleAttackThreshold,
                _decisionInterval,
                _initialAttackDelay,
                _attackCooldown);
            _fsm.SetTarget(_target);
            _fsm.Enter();
            RegisterWithRegistry();
        }

        private void LateUpdate()
        {
            if (!_decisionEnabled) return;
            _fsm?.Tick(Time.deltaTime * _actions.HitLagSpeed);
        }

        public void SetDecisionEnabled(bool enabled)
        {
            _decisionEnabled = enabled;
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            _actions.SetTarget(target);
            _fsm?.SetTarget(target);
        }

        private void OnHitRequested(string section)
        {
            _fsm?.InterruptWithHit(section);
        }

        private void RegisterWithRegistry()
        {
            if (_registry == null)
                _registry = FindFirstObjectByType<MonsterRegistry>();

            if (_registry != null)
                _registry.Register(this);
        }
    }
}
