using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ZZZ;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    // 얇은 코디네이터 — config 러너(ConfigState)를 소유·구동하고, 협력 객체(입력 버퍼/트리거)를 조립한다.
    // 실제 로직은 InputBuffer / HitTrigger / DodgeTrigger / ConfigRegistry로 분리. 여기서는 조립 + facade만.
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(PlayerAnimatorBridge))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerResources))]
    public class PlayerStateMachine : MonoBehaviour
    {
        [Header("Animation Config")]
        [SerializeField] private AnimationConfig _startConfig;   // 시작/기본(걷기) config. 콤보 등은 링크의 TargetConfig로 연결

        // 이벤트로 진입하는 추가 config들(Hit/Evade 등). 링크로 도달하지 않는 진입점만 여기 등록.
        // config가 새로 생기면 이 리스트에 드롭만 하면 됨 (재생할 섹션 이름으로 자동 검색 — ConfigRegistry).
        [SerializeField] private List<AnimationConfig> _configs = new List<AnimationConfig>();

        [Header("Dodge — 어떤 config에서든 회피 입력 시 강제 진입 (push)")]
        [SerializeField] private string _dodgePrefix     = "Evade_";  // 섹션 접두어 (Evade_Front 등)
        [SerializeField, Range(0f, 0.2f)] private float _dodgeBlend = 0.05f;
        [SerializeField, Range(0f, 1f)]   private float _dodgeReinterrupt = 0.3f;  // 회피 중 재입력 무시 임계

        [Header("Hit")]
        // 이미 피격 중일 때, 현재 반응 진행도가 이 값을 넘어야 새 피격이 재시작된다 (재진입 가드).
        [SerializeField, Range(0f, 1f)] private float _hitReinterruptThreshold = 0.3f;
        // 피격 진입 CrossFade 시간. 전이 중 root motion이 버려지므로(점프 방지) 작게 둘 것.
        [SerializeField, Range(0f, 0.2f)] private float _hitEntryBlend = 0.03f;

        [Header("Input Buffer")]
        [SerializeField] private float _inputBufferWindow = 0.25f;  // 입력 버퍼 유효 시간

        private ConfigState        _state;   // 단일 config 러너 — 전이는 config가 관리
        private PlayerStateContext _ctx;
        private InputBuffer        _input;
        private HitTrigger         _hit;
        private DodgeTrigger       _dodge;

        // ── 입력 버퍼 facade (ConfigState/HUD/에디터 툴이 사용) ───────
        public bool       HasBufferedInput => _input.HasInput;
        public ComboInput BufferedInput    => _input.Current;
        public void       ConsumeInput()   => _input.Consume();

        // 무적 — i-frame 윈도우 동안 IFrameModule이 매 프레임 세팅. HitTrigger가 이 값을 보고 무시.
        public bool Invulnerable { get; set; }

        // ── 퍼펙트 회피 윈도우 ─────────────────────────────────────
        // 적이 "공격 적중 직전" 이 창을 열어두면, 그 사이 회피 = 퍼펙트(좌/우 회피 모션).
        // 적 공격 시스템이 생기면 공격 액티브 직전에 OpenIncomingAttack(window)를 호출하면 된다.
        private float _incomingAttackUntil = -1f;
        public bool IncomingAttackActive => Time.time <= _incomingAttackUntil;
        public void OpenIncomingAttack(float window) => _incomingAttackUntil = Time.time + window;

        private void Awake()
        {
            var controller = GetComponent<PlayerController>();
            var animator   = GetComponent<PlayerAnimatorBridge>();
            var cc         = GetComponent<CharacterController>();
            var resources  = GetComponent<PlayerResources>();

            _ctx   = new PlayerStateContext(controller, animator, cc, transform);
            _state = new ConfigState(_ctx, this, _startConfig);

            // 협력 객체 조립 — 로직은 각자 담당, 머신은 조립과 구동 순서만 책임진다.
            var registry = new ConfigRegistry(_startConfig, _configs);
            _input = new InputBuffer(_inputBufferWindow);
            _hit   = new HitTrigger(this, _state, registry, _hitReinterruptThreshold, _hitEntryBlend);
            _dodge = new DodgeTrigger(this, _state, registry, _input, resources, _dodgePrefix, _dodgeBlend, _dodgeReinterrupt);
        }

        // Start는 모든 Awake가 끝난 뒤 실행 → PlayerAnimatorBridge._animator 초기화 보장
        private void Start() => _state.Enter();

        private void Update()
        {
            // 회피는 링크 평가 전에 — 콤보보다 우선(공격 중 캔슬)
            if (HasBufferedInput && BufferedInput == ComboInput.Dodge) _dodge.Trigger();
            _state.Update();
        }

        // ── 피격 facade (충돌 검출 / 적 공격 시스템 / 테스트 트리거가 호출) ──
        public void TriggerHitFrom(Vector3 attackerPos) => _hit.TriggerFrom(attackerPos, transform);
        public void TriggerHit(string direction = "Back") => _hit.Trigger(direction);

        // ── 에디터/HUD 라이브 모니터용 ──
        public AnimationConfig CurrentConfig         => _state?.CurrentConfig;
        public int             CurrentClipIndex      => _state?.ActiveIndex ?? -1;
        public string          CurrentSection        => _state?.ActiveSection;
        public float           CurrentNormalizedTime => _state?.CurrentNormalizedTime ?? 0f;
        public MoveDir         CurrentMoveDir         => _state?.CurrentMoveDir ?? MoveDir.Any;

        // ── 입력 콜백 (PlayerInput SendMessages) ──────────────────
        // 입력만 버퍼링 — 실제 콤보 진입은 config의 Input 링크(TargetConfig=콤보)가 처리
        private void OnAttack(InputValue value) { if (value.isPressed) _input.Buffer(ComboInput.Normal); }
        private void OnDodge(InputValue value)   { if (value.isPressed) _input.Buffer(ComboInput.Dodge); }
    }
}
