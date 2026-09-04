using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;
using ZZZ.Agent;

namespace ZZZ.Player
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputRouter : MonoBehaviour
    {
        [SerializeField] private AgentActionController _initialTarget;

        private PlayerInput        _playerInput;
        private MonoBehaviour      _targetBehaviour;
        private IAgentInputTarget _target;
        private InputAction        _move;
        private InputAction        _attack;
        private InputAction        _dodge;
        private InputAction        _parry;
        private InputAction        _enhance;
        private InputAction        _previous;
        private InputAction        _next;
        private bool               _inputEnabled = true;

        public event Action OnPreviousRequested;
        public event Action OnNextRequested;

        public bool InputEnabled => _inputEnabled;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            InputActionMap actions = _playerInput.actions.FindActionMap("Player", true);
            _move     = actions.FindAction("Move", true);
            _attack   = actions.FindAction("Attack", true);
            _dodge    = actions.FindAction("Dodge", true);
            _parry    = actions.FindAction("Parry", true);
            _enhance  = actions.FindAction("Attack_Normal_Enhance", true);
            _previous = actions.FindAction("Previous", true);
            _next     = actions.FindAction("Next", true);
        }

        private void Start()
        {
            if (_target == null) SetTarget(_initialTarget);
        }

        private void OnEnable()
        {
            _move.performed     += OnMove;
            _move.canceled      += OnMove;
            _attack.performed   += OnAttack;
            _dodge.performed    += OnDodge;
            _parry.performed    += OnParry;
            _enhance.performed  += OnEnhance;
            _enhance.canceled   += OnEnhance;
            _previous.performed += OnPrevious;
            _next.performed     += OnNext;
        }

        private void OnDisable()
        {
            _move.performed     -= OnMove;
            _move.canceled      -= OnMove;
            _attack.performed   -= OnAttack;
            _dodge.performed    -= OnDodge;
            _parry.performed    -= OnParry;
            _enhance.performed  -= OnEnhance;
            _enhance.canceled   -= OnEnhance;
            _previous.performed -= OnPrevious;
            _next.performed     -= OnNext;
            ResetTargetInput();
        }

        public bool SetTarget(MonoBehaviour targetBehaviour)
        {
            if (targetBehaviour == null)
            {
                ClearTarget();
                return true;
            }

            if (!(targetBehaviour is IAgentInputTarget target))
            {
                Debug.LogError(
                    $"{targetBehaviour.name} does not implement {nameof(IAgentInputTarget)}.",
                    targetBehaviour);
                return false;
            }

            ResetTargetInput();
            _targetBehaviour = targetBehaviour;
            _target          = target;
            _target.ClearInput();
            return true;
        }

        public void ClearTarget()
        {
            ResetTargetInput();
            _targetBehaviour = null;
            _target          = null;
        }

        public void SetInputEnabled(bool enabled)
        {
            if (_inputEnabled == enabled) return;

            _inputEnabled = enabled;
            if (!enabled) ResetTargetInput();
        }

        private bool HasTarget => _targetBehaviour != null && _target != null;

        private void ResetTargetInput()
        {
            if (HasTarget) _target.ClearInput();
        }

        private void OnMove(InputAction.CallbackContext context)
        {
            if (!_inputEnabled) return;
            if (HasTarget) _target.SetMoveInput(context.ReadValue<Vector2>());
        }

        private void OnAttack(InputAction.CallbackContext context)
        {
            if (!_inputEnabled) return;
            if (!HasTarget) return;
            ComboInput input = context.interaction is HoldInteraction
                ? ComboInput.Strong
                : ComboInput.Normal;
            _target.BufferInput(input);
        }

        private void OnDodge(InputAction.CallbackContext context)
        {
            if (!_inputEnabled) return;
            if (HasTarget) _target.BufferInput(ComboInput.Dodge);
        }

        private void OnParry(InputAction.CallbackContext context)
        {
            if (!_inputEnabled) return;
            if (HasTarget) _target.BufferInput(ComboInput.Parry);
        }

        private void OnEnhance(InputAction.CallbackContext context)
        {
            if (!_inputEnabled) return;
            if (!HasTarget) return;
            bool held = context.ReadValueAsButton();
            _target.SetInputHeld(ComboInput.Enhance, held);
            if (held) _target.BufferInput(ComboInput.Enhance);
        }

        private void OnPrevious(InputAction.CallbackContext context)
        {
            if (_inputEnabled) OnPreviousRequested?.Invoke();
        }

        private void OnNext(InputAction.CallbackContext context)
        {
            if (_inputEnabled) OnNextRequested?.Invoke();
        }
    }
}
