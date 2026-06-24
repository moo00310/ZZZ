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

        [Header("Parry — 어떤 config에서든 패링 입력 시 스탠스 진입 (push)")]
        [SerializeField] private string _parryPrefix     = "Attack_ParryAid_";  // 섹션 접두어 (Attack_ParryAid_Start 등)
        [SerializeField, Range(0f, 0.2f)] private float _parryBlend = 0.05f;
        [SerializeField, Range(0f, 1f)]   private float _parryReinterrupt = 0.3f;  // 스탠스 중 재입력 무시 임계

        [Header("Attack_Normal_Enhance — 전용 키 입력 시 강화 공격 진입 (콤보 링크가 못 받은 경우의 전역 폴백)")]
        [SerializeField] private string _attackNormalEnhanceSection = "Attack_Normal_Enhance_01";  // 강화 공격 config 진입 섹션
        [SerializeField, Range(0f, 0.2f)] private float _attackNormalEnhanceBlend = 0.05f;
        [SerializeField, Range(0f, 1f)]   private float _attackNormalEnhanceReinterrupt = 0.3f;  // 시전 중 재입력 무시 임계

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
        private ParryTrigger       _parry;
        private Attack_Normal_EnhanceTrigger _attackNormalEnhance;

        // ── 입력 버퍼 facade (ConfigState/HUD/에디터 툴이 사용) ───────
        public bool       HasBufferedInput => _input.HasInput;
        public ComboInput BufferedInput    => _input.Current;
        public void       ConsumeInput()   => _input.Consume();

        // 키 홀드 facade — OnRelease 링크(홀드 차지 → 릴리스)가 "아직 누르고 있나"를 판정
        public bool IsInputHeld(ComboInput input) => _input.IsHeld(input);

        // 무적 — i-frame 윈도우 동안 IFrameModule이 매 프레임 세팅. HitTrigger가 이 값을 보고 무시.
        public bool Invulnerable { get; set; }

        // 패링 활성 — ParryAid_Start의 활성 구간 동안 ParryModule이 매 프레임 세팅.
        // HitTrigger가 이 값을 보고 피격 대신 쳐냄(ParryAid_L/H)으로 분기한다.
        public bool ParryActive { get; set; }

        // ── 퍼펙트 회피 / 패링 윈도우 ──────────────────────────────
        // 적이 "공격 적중 직전" 이 창을 열어두면, 그 사이 회피 = 퍼펙트(좌/우 회피 모션).
        // 동시에 강도(IncomingStrength)를 실어, 패링 성공 시 쳐냄 반응(ParryAid_L/H)을 결정한다.
        // 적 공격 시스템이 생기면 공격 액티브 직전에 OpenIncomingAttack(window, strength)를 호출하면 된다.
        private float _incomingAttackUntil = -1f;
        public bool           IncomingAttackActive => Time.time <= _incomingAttackUntil;
        public AttackStrength IncomingStrength { get; private set; }
        public void OpenIncomingAttack(float window, AttackStrength strength = AttackStrength.Light)
        {
            _incomingAttackUntil = Time.time + window;
            IncomingStrength     = strength;
        }

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
            _hit   = new HitTrigger(this, _state, registry, _hitReinterruptThreshold, _hitEntryBlend, _parryPrefix);
            _dodge = new DodgeTrigger(this, _state, registry, _input, resources, _dodgePrefix, _dodgeBlend, _dodgeReinterrupt);
            _parry = new ParryTrigger(this, _state, registry, _input, _parryPrefix, _parryBlend, _parryReinterrupt);
            _attackNormalEnhance = new Attack_Normal_EnhanceTrigger(this, _state, registry, _input, _attackNormalEnhanceSection, _attackNormalEnhanceBlend, _attackNormalEnhanceReinterrupt);
        }

        // Start는 모든 Awake가 끝난 뒤 실행 → PlayerAnimatorBridge._animator 초기화 보장
        private void Start() => _state.Enter();

        private void Update()
        {
            // 회피/패링은 링크 평가 전에 — 콤보보다 우선(공격 중 캔슬)
            if (HasBufferedInput && BufferedInput == ComboInput.Dodge) _dodge.Trigger();
            if (HasBufferedInput && BufferedInput == ComboInput.Parry) _parry.Trigger();
            _state.Update();
            // 강화 공격은 콤보 링크(Attack=Attack_Normal_Enhance) 평가 이후 폴백 — 콤보 중엔 섹션 링크가 입력을 소비해
            // 진입하고(우선), 링크가 없는 상태(걷기/Idle)에선 입력이 남아 이 트리거가 강제 진입시킨다.
            if (HasBufferedInput && BufferedInput == ComboInput.Attack_Normal_Enhance) _attackNormalEnhance.Trigger();
        }

        // ── 피격 facade (충돌 검출 / 적 공격 시스템 / 테스트 트리거가 호출) ──
        public void TriggerHitFrom(Vector3 attackerPos) => _hit.TriggerFrom(attackerPos, transform);
        public void TriggerHit(string direction = "Back") => _hit.Trigger(direction);

        // 패링 스탠스 강제 진입 facade (테스트 트리거가 호출 — 실제 플레이는 OnParry 입력으로 진입)
        public void TriggerParry() => _parry.Trigger();

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
        private void OnParry(InputValue value)   { if (value.isPressed) _input.Buffer(ComboInput.Parry); }
        // 누름 = 버퍼링(콤보 진입) + 홀드 ON, 뗌 = 홀드 OFF(OnRelease 링크가 이 전환을 보고 발사)
        private void OnAttack_Normal_Enhance(InputValue value)
        {
            _input.SetHeld(ComboInput.Attack_Normal_Enhance, value.isPressed);
            if (value.isPressed) _input.Buffer(ComboInput.Attack_Normal_Enhance);
        }
    }
}
