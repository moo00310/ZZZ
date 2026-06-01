using UnityEngine;

namespace ZZZ.Player.StateMachine.States
{
    public class RushState : StateBase
    {
        private float _timer;
        private const float MaxDuration = 2f;

        public RushState(PlayerStateContext ctx, PlayerStateMachine machine)
            : base(ctx, machine) { }

        public override void Enter()
        {
            _timer = 0f;
            Ctx.Animator.PlayRush();
        }

        public override void Update()
        {
            _timer += Time.deltaTime;

            float normalizedTime = Ctx.Animator.GetCurrentNormalizedTime();

            // 클립이 끝나거나 타임아웃 시 복귀
            if (normalizedTime >= 0.95f || _timer >= MaxDuration)
                Machine.ChangeState<LocomotionState>();
        }
    }
}
