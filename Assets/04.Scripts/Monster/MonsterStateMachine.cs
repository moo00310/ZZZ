using UnityEngine;

namespace ZZZ.Monster
{
    public enum MonsterActionState
    {
        Idle,
        Hit,
        Attack,
        WalkBack,
    }

    public sealed class MonsterFsm
    {
        private readonly MonsterActionController _actions;
        private readonly MonsterConditionContext _conditionContext;
        private readonly float _attackRangeSqr;
        private readonly float _decisionInterval;
        private readonly float _initialAttackDelay;
        private readonly float _attackCooldown;

        private bool _hasCurrentState;
        private Transform _target;
        private float _decisionTime;
        private float _attackCooldownTime;

        public MonsterActionState CurrentState { get; private set; }
        public Transform Target => _target;

        public MonsterFsm(
            MonsterActionController actions,
            MonsterConditionContext conditionContext,
            float attackRange,
            float decisionInterval,
            float initialAttackDelay,
            float attackCooldown)
        {
            _actions = actions;
            _conditionContext = conditionContext;
            _attackRangeSqr = attackRange * attackRange;
            _decisionInterval = Mathf.Max(0.01f, decisionInterval);
            _initialAttackDelay = Mathf.Max(0f, initialAttackDelay);
            _attackCooldown = Mathf.Max(0f, attackCooldown);
        }

        public bool Enter()
        {
            _decisionTime = 0f;
            _attackCooldownTime = _initialAttackDelay;
            _conditionContext.ClearDecision();
            return ChangeState(MonsterActionState.Idle);
        }

        public void SetTarget(Transform target)
        {
            _target = target;
        }

        public void Tick(float deltaTime)
        {
            _conditionContext.Tick(deltaTime);
            _attackCooldownTime -= deltaTime;

            TickActionFlow();
            if (!_hasCurrentState) return;

            _decisionTime -= deltaTime;
            if (_decisionTime > 0f) return;

            _decisionTime = _decisionInterval;
            EvaluateState();
        }

        public bool InterruptWithHit(string section)
        {
            return ChangeState(MonsterActionState.Hit, section, true);
        }

        private void TickActionFlow()
        {
            if (!_hasCurrentState || !_actions.IsCurrentActionComplete) return;

            switch (CurrentState)
            {
                case MonsterActionState.Hit:
                    ChangeState(MonsterActionState.Idle);
                    break;

                case MonsterActionState.Attack:
                    if (!ChangeState(MonsterActionState.WalkBack))
                        ChangeState(MonsterActionState.Idle);
                    break;

                case MonsterActionState.WalkBack:
                    ChangeState(MonsterActionState.Idle);
                    break;
            }
        }

        private void EvaluateState()
        {
            switch (CurrentState)
            {
                case MonsterActionState.Idle:
                    TryStartAttack();
                    break;

                case MonsterActionState.Attack:
                    EvaluateAttackDecision();
                    break;
            }
        }

        private void TryStartAttack()
        {
            if (_attackCooldownTime > 0f || !IsTargetWithinAttackRange())
                return;

            if (ChangeState(MonsterActionState.Attack))
                _attackCooldownTime = _attackCooldown;
        }

        private void EvaluateAttackDecision()
        {
            if (!IsTargetWithinAttackRange()) return;

            float bufferDuration = Mathf.Max(0.1f, _decisionInterval * 2f);
            _conditionContext.BufferDecision(
                MonsterDecision.ContinueCombo, bufferDuration);
        }

        private bool IsTargetWithinAttackRange()
        {
            if (_target == null) return false;

            Vector3 toTarget = _target.position - _actions.transform.position;
            toTarget.y = 0f;
            return toTarget.sqrMagnitude <= _attackRangeSqr;
        }

        private bool ChangeState(
            MonsterActionState nextState,
            string section = null,
            bool force = false)
        {
            if (!force && _hasCurrentState && CurrentState == nextState)
                return false;

            bool entered = nextState switch
            {
                MonsterActionState.Idle => _actions.TryPlayIdle(),
                MonsterActionState.Hit => _actions.TryPlayHit(section),
                MonsterActionState.Attack => _actions.TryPlayAttack(_target),
                MonsterActionState.WalkBack => _actions.TryPlayWalkBack(),
                _ => false,
            };
            if (!entered) return false;

            CurrentState = nextState;
            _hasCurrentState = true;
            if (nextState != MonsterActionState.Attack)
                _conditionContext.ClearDecision();
            return true;
        }
    }
}
