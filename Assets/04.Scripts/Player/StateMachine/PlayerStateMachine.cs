using UnityEngine;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(PlayerAnimatorBridge))]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerStateMachine : MonoBehaviour
    {
        private StateMachine       _machine;
        private PlayerStateContext _ctx;

        private void Awake()
        {
            var controller = GetComponent<PlayerController>();
            var animator   = GetComponent<PlayerAnimatorBridge>();
            var cc         = GetComponent<CharacterController>();
            var config     = controller.Config;

            _ctx = new PlayerStateContext(controller, animator, cc, transform, config);

            _machine = new StateMachine();
            _machine.AddState(new LocomotionState(_ctx, this));
            _machine.AddState(new NormalComboState(_ctx, this));
            _machine.AddState(new EnhanceComboState(_ctx, this));
            _machine.AddState(new RushState(_ctx, this));
            _machine.AddState(new SpecialState(_ctx, this));
        }

        // Start는 모든 Awake가 끝난 뒤 실행 → PlayerAnimatorBridge._animator 초기화 보장
        private void Start() => _machine.ChangeState<LocomotionState>();

        private void Update()      => _machine.Update();
        private void FixedUpdate() => _machine.FixedUpdate();

        public void ChangeState<T>() where T : IState => _machine.ChangeState<T>();

        public string CurrentStateName => _machine.CurrentState?.GetType().Name ?? "-";
        public LocomotionState LocomotionState =>
            _machine.CurrentState as LocomotionState;
    }
}
