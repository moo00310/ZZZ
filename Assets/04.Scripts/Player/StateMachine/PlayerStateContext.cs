using UnityEngine;

namespace ZZZ.Player.StateMachine
{
    // 플레이어 내부에서 공유하는 데이터/컴포넌트 참조 (PlayerConditionContext 등이 사용).
    // ConfigState에 넘기는 엔진용 묶음은 별도 ConfigContext로 Awake에서 만든다.
    public class PlayerStateContext
    {
        public PlayerController      Controller     { get; }
        public AnimatorBridge  Animator       { get; }
        public CharacterController   CC             { get; }
        public Transform             Transform      { get; }

        // 입력 상태 (PlayerController에서 업데이트)
        public bool    IsGrounded    => CC.isGrounded;
        public Vector3 MoveDirection => Controller.MoveDirection;

        public PlayerStateContext(
            PlayerController     controller,
            AnimatorBridge animator,
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
