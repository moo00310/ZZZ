using UnityEngine;
using ZZZ;

namespace ZZZ.Player.StateMachine
{
    // 플레이어 입력/방향을 LinkCondition(InputCondition 등)에 공급하는 어댑터.
    // ConfigState에 ILinkConditionContext로 주입된다. 몬스터는 별도 컨텍스트를 구현해 주입한다.
    public sealed class PlayerConditionContext : ILinkConditionContext
    {
        private readonly PlayerStateContext _ctx;
        private readonly PlayerStateMachine _machine;

        public PlayerConditionContext(PlayerStateContext ctx, PlayerStateMachine machine)
        {
            _ctx     = ctx;
            _machine = machine;
        }

        public bool       HasBufferedInput => _machine.HasBufferedInput;
        public ComboInput BufferedInput    => _machine.BufferedInput;
        public bool       IsHeld(ComboInput input) => _machine.IsInputHeld(input);
        public void       ConsumeInput()   => _machine.ConsumeInput();

        public MoveDir CurrentMoveDir => _ctx.Controller.CurrentMoveDir;
        public Vector3 InputDir       => _ctx.Controller.MoveDirection;
        public Vector3 Forward        => _ctx.Transform.forward;
    }
}
