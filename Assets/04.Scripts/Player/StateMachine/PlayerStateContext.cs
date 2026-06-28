using UnityEngine;

namespace ZZZ.Player.StateMachine
{
    // 모든 State가 공유하는 데이터/컴포넌트 참조. ConfigState에는 IConfigContext로 넘어간다.
    public class PlayerStateContext : IConfigContext
    {
        public PlayerController      Controller     { get; }
        public PlayerAnimatorBridge  Animator       { get; }
        public CharacterController   CC             { get; }
        public Transform             Transform      { get; }

        // IConfigContext 구현 — ConfigState가 구상 타입 대신 이 추상 표면을 본다.
        IConfigMover    IConfigContext.Mover      => Controller;
        IAnimatorBridge IConfigContext.Animator   => Animator;
        GameObject      IConfigContext.GameObject => Controller.gameObject;
        // Transform은 동일 타입이라 public Transform 프로퍼티가 IConfigContext.Transform을 그대로 만족.

        // 입력 상태 (PlayerController에서 업데이트)
        public bool    IsGrounded    => CC.isGrounded;
        public Vector3 MoveDirection => Controller.MoveDirection;

        public PlayerStateContext(
            PlayerController     controller,
            PlayerAnimatorBridge animator,
            CharacterController  cc,
            Transform            transform)
        {
            Controller = controller;
            Animator   = animator;
            CC         = cc;
            Transform  = transform;
        }
    }
}
