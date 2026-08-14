using UnityEngine;
using ZZZ;

namespace ZZZ.Player.StateMachine
{
    // 플레이어 입력/방향을 LinkCondition(InputCondition 등)에 공급하는 어댑터.
    // ConfigState에 ILinkConditionContext로 주입된다. 몬스터는 별도 컨텍스트를 구현해 주입한다.
    public sealed class PlayerConditionContext : ILinkConditionContext
    {
        private readonly PlayerMotor _motor;
        private readonly Transform _transform;
        private readonly PlayerActionController _controller;

        public PlayerConditionContext(
            PlayerMotor motor, Transform transform, PlayerActionController controller)
        {
            _motor = motor;
            _transform = transform;
            _controller = controller;
        }

        public bool       HasBufferedInput => _controller.HasBufferedInput;
        public ComboInput BufferedInput    => _controller.BufferedInput;
        public bool       IsHeld(ComboInput input) => _controller.IsInputHeld(input);
        public void       ConsumeInput()   => _controller.ConsumeInput();

        public MoveDir CurrentMoveDir => _motor.CurrentMoveDir;
        public Vector3 InputDir       => _motor.MoveDirection;
        public Vector3 Forward        => _transform.forward;
    }
}
