namespace ZZZ.Player.StateMachine
{
    public abstract class StateBase : IState
    {
        protected readonly PlayerStateContext Ctx;
        protected readonly PlayerStateMachine Machine;

        protected StateBase(PlayerStateContext ctx, PlayerStateMachine machine)
        {
            Ctx     = ctx;
            Machine = machine;
        }

        public virtual void Enter()       { }
        public virtual void Update()      { }
        public virtual void FixedUpdate() { }
        public virtual void Exit()        { }
    }
}
