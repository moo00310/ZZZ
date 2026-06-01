using System;
using System.Collections.Generic;

namespace ZZZ.Player.StateMachine
{
    public class StateMachine
    {
        private IState _currentState;
        private IState _previousState;

        private readonly Dictionary<Type, IState> _states = new();

        public IState CurrentState  => _currentState;
        public IState PreviousState => _previousState;

        public void AddState<T>(T state) where T : IState
        {
            _states[typeof(T)] = state;
        }

        public void ChangeState<T>() where T : IState
        {
            if (!_states.TryGetValue(typeof(T), out IState next)) return;
            if (next == _currentState) return;

            _previousState = _currentState;
            _currentState?.Exit();
            _currentState = next;
            _currentState.Enter();
        }

        public void Update()      => _currentState?.Update();
        public void FixedUpdate() => _currentState?.FixedUpdate();
    }
}
