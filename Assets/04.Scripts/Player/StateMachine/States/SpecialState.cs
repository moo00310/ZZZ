using UnityEngine;

namespace ZZZ.Player.StateMachine.States
{
    public class SpecialState : StateBase
    {
        private float _timer;
        private const float MaxDuration = 3f;

        public SpecialState(PlayerStateContext ctx, PlayerStateMachine machine)
            : base(ctx, machine) { }

        public override void Enter()
        {
            _timer = 0f;
            Ctx.Animator.PlaySpecial();
        }

        public override void Update()
        {
            _timer += Time.deltaTime;

            float normalizedTime = Ctx.Animator.GetCurrentNormalizedTime();

            if (normalizedTime >= 0.95f || _timer >= MaxDuration)
                Machine.ChangeState<LocomotionState>();
        }
    }
}
