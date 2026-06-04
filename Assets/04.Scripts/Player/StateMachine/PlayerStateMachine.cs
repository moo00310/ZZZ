using UnityEngine;
using UnityEngine.InputSystem;
using ZZZ;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(PlayerAnimatorBridge))]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerStateMachine : MonoBehaviour
    {
        [Header("Animation Config")]
        [SerializeField] private AnimationConfig _startConfig;   // 시작/기본(걷기) config. 콤보 등은 링크의 TargetConfig로 연결

        [Header("Input Buffer")]
        [SerializeField] private float _inputBufferWindow = 0.25f;  // 입력 버퍼 유효 시간

        private StateMachine       _machine;
        private PlayerStateContext _ctx;

        // ── 입력 버퍼 ──────────────────────────────────────────────
        private ComboInput _bufferedInput;
        private float      _bufferedTime = -10f;

        public bool HasBufferedInput => Time.time - _bufferedTime <= _inputBufferWindow;
        public ComboInput BufferedInput => _bufferedInput;
        public void ConsumeInput() => _bufferedTime = -10f;

        private void Awake()
        {
            var controller = GetComponent<PlayerController>();
            var animator   = GetComponent<PlayerAnimatorBridge>();
            var cc         = GetComponent<CharacterController>();
            var config     = controller.Config;

            _ctx = new PlayerStateContext(controller, animator, cc, transform, config);

            _machine = new StateMachine();
            _machine.AddState(new ConfigState(_ctx, this, _startConfig));
            _machine.AddState(new EnhanceComboState(_ctx, this));
            _machine.AddState(new RushState(_ctx, this));
            _machine.AddState(new SpecialState(_ctx, this));
        }

        // Start는 모든 Awake가 끝난 뒤 실행 → PlayerAnimatorBridge._animator 초기화 보장
        private void Start() => _machine.ChangeState<ConfigState>();

        private void Update()      => _machine.Update();
        private void FixedUpdate() => _machine.FixedUpdate();

        public void ChangeState<T>() where T : IState => _machine.ChangeState<T>();

        public string CurrentStateName => _machine.CurrentState?.GetType().Name ?? "-";

        // ── 에디터 라이브 모니터용 (현재 State가 ConfigState일 때만 유효) ──
        private ConfigState ActiveConfigState => _machine.CurrentState as ConfigState;
        public AnimationConfig CurrentConfig         => ActiveConfigState?.CurrentConfig;
        public int             CurrentClipIndex      => ActiveConfigState?.ActiveIndex ?? -1;
        public string          CurrentSection        => ActiveConfigState?.ActiveSection;
        public float           CurrentNormalizedTime => ActiveConfigState?.CurrentNormalizedTime ?? 0f;
        public MoveDir         CurrentMoveDir         => ActiveConfigState?.CurrentMoveDir ?? MoveDir.Any;

        // ── 입력 콜백 (PlayerInput SendMessages) ──────────────────
        private void OnAttack(InputValue value)  { if (value.isPressed) BufferInput(ComboInput.Normal); }
        private void OnEnhanced(InputValue value) { if (value.isPressed) BufferInput(ComboInput.Enhanced); }
        private void OnSpecial(InputValue value)  { if (value.isPressed) BufferInput(ComboInput.Special); }
        private void OnDodge(InputValue value)    { if (value.isPressed) BufferInput(ComboInput.Dodge); }

        private void BufferInput(ComboInput input)
        {
            // 입력만 버퍼링 — 실제 콤보 진입은 config의 Input 링크(TargetConfig=콤보)가 처리
            _bufferedInput = input;
            _bufferedTime  = Time.time;
        }
    }
}
