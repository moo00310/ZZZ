using UnityEngine;

namespace ZZZ.Player.StateMachine
{
    // 모든 State가 공유하는 데이터/컴포넌트 참조
    public class PlayerStateContext
    {
        public PlayerController      Controller     { get; }
        public PlayerAnimatorBridge  Animator       { get; }
        public CharacterController   CC             { get; }
        public Transform             Transform      { get; }

        // 입력 상태 (PlayerController에서 업데이트)
        public float   MoveSpeed     => Controller.CurrentSpeed;
        public bool    IsSprinting   => Controller.IsSprinting;
        public bool    IsGrounded    => CC.isGrounded;
        public Vector3 MoveDirection => Controller.MoveDirection;

        public LocomotionConfig Config { get; }

        public PlayerStateContext(
            PlayerController     controller,
            PlayerAnimatorBridge animator,
            CharacterController  cc,
            Transform            transform,
            LocomotionConfig     config = null)
        {
            Controller = controller;
            Animator   = animator;
            CC         = cc;
            Transform  = transform;
            Config     = config;
        }
    }
}
