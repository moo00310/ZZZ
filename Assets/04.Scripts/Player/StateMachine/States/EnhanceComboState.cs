using UnityEngine;

namespace ZZZ.Player.StateMachine.States
{
    public class EnhanceComboState : StateBase
    {
        private int   _comboIndex;
        private float _comboTimer;
        private bool  _nextQueued;

        private const int   MaxCombo       = 3;
        private const float ComboWindowEnd = 0.7f;
        private const float ComboResetTime = 1.2f;

        public EnhanceComboState(PlayerStateContext ctx, PlayerStateMachine machine)
            : base(ctx, machine) { }

        public override void Enter()
        {
            _comboIndex = 1;
            _comboTimer = 0f;
            _nextQueued = false;
            Ctx.Animator.PlayEnhanceAttack(_comboIndex);
        }

        public override void Update()
        {
            _comboTimer += Time.deltaTime;

            float normalizedTime = Ctx.Animator.GetCurrentNormalizedTime();

            if (_nextQueued && normalizedTime >= ComboWindowEnd)
            {
                _nextQueued = false;

                if (_comboIndex >= MaxCombo)
                {
                    Machine.ChangeState<LocomotionState>();
                    return;
                }

                _comboIndex++;
                _comboTimer = 0f;
                Ctx.Animator.PlayEnhanceAttack(_comboIndex);
            }

            if (_comboTimer >= ComboResetTime && !_nextQueued)
                Machine.ChangeState<LocomotionState>();
        }

        public void QueueNext() => _nextQueued = true;

        public override void Exit()
        {
            _comboIndex = 0;
            _nextQueued = false;
        }
    }
}
