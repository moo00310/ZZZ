using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using ZZZ;

using ZZZ.Combat;
using ZZZ.Effects;
using ZZZ.Player.StateMachine.States;

namespace ZZZ.Player.StateMachine
{
    // 얇은 코디네이터 — config 러너(ConfigState)를 소유·구동하고, 협력 객체(입력 버퍼/트리거)를 조립한다.
    // 실제 로직은 InputBuffer / HitTrigger / DodgeTrigger / ConfigRegistry로 분리. 여기서는 조립 + facade만.
    [RequireComponent(typeof(PlayerMotor))]
    [RequireComponent(typeof(CharacterAnimatorBridge))]
    [RequireComponent(typeof(PlayerResources))]
    [MovedFrom(true, "ZZZ.Player.StateMachine", "Assembly-CSharp", "PlayerStateMachine")]
    public class PlayerActionController : MonoBehaviour, IConfigSignals, ILiveMonitor,
        IInputMonitor, IPlayerInputTarget, IHittable, IHitSource,
        IReactionTargetProvider, IParryWarningReceiver
    {
        [Header("Animation Config")]
        [SerializeField] private AnimationConfig _startConfig;   // 시작/기본(걷기) config. 콤보 등은 링크의 TargetConfig로 연결

        // 이벤트로 진입하는 추가 config들(Hit/Evade 등). 링크로 도달하지 않는 진입점만 여기 등록.
        // config가 새로 생기면 이 리스트에 드롭만 하면 됨 (재생할 섹션 이름으로 자동 검색 — ConfigRegistry).
        [SerializeField] private List<AnimationConfig> _configs = new List<AnimationConfig>();

        // ── 외부 키/이벤트 진입 트리거 — 각 트리거가 자기 설정을 보유(인스펙터에서 폴드로 편집).
        // 런타임 의존(상태/레지스트리/입력 등)은 Awake에서 Init으로 주입한다.
        [Header("Triggers")]
        [Tooltip("어떤 config에서든 회피 입력 시 강제 진입 (push) — 콤보보다 우선")]
        [SerializeField] private DodgeTrigger _dodge = new DodgeTrigger();
        [Tooltip("어떤 config에서든 패링 입력 시 스탠스 진입 (push)")]
        [SerializeField] private ParryTrigger _parry = new ParryTrigger();
        [Tooltip("E 입력 시 강화 공격 진입 (콤보 링크가 못 받은 경우의 전역 폴백). 방향/거리로 섹션 선택")]
        [SerializeField] private Attack_Normal_EnhanceTrigger _attackNormalEnhance = new Attack_Normal_EnhanceTrigger();
        [Tooltip("외부 이벤트(충돌/적 공격)로 피격 반응 진입")]
        [SerializeField] private HitTrigger _hit = new HitTrigger();

        [Header("Input Buffer")]
        [SerializeField] private float _inputBufferWindow = 0.25f;  // 입력 버퍼 유효 시간

        [Header("Hit Debug")]
        [Tooltip("Play 중 실제 Hit 판정과 Sweep 이동 구간을 Scene/Game View에 표시합니다. Game View의 Gizmos 버튼도 켜야 합니다.")]
        [SerializeField] private bool _showHitGizmos;
        [Tooltip("단발 Hit 선이 화면에 유지되는 시간입니다.")]
        [SerializeField, Min(0f)] private float _hitGizmoDuration = 0.1f;

        private ConfigState        _state;   // 단일 config 러너 — 전이는 config가 관리
        private InputBuffer        _input;
        private PlayerMotor        _motor;
        private bool               _isRunning;
        private Transform          _incomingAttackSource;
        private bool               _perfectDodgePending;

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

        public CombatTeam Team => CombatTeam.Player;
        public Transform HitTransform => transform;
        public Transform ReactionTarget { get; private set; }
        public bool PerfectDodgeCandidate =>
            _perfectDodgePending && _dodge.IsDodging;
        public event Action<HitContext> ParrySucceeded;
        public event Action<Transform> PerfectDodgeSucceeded;
        public event Action<HitContext> ParryWarningReceived;

        // ── 퍼펙트 회피 / 패링 윈도우 ──────────────────────────────
        // 적이 "공격 적중 직전" 이 창을 열어두면, 그 사이 회피 = 퍼펙트(좌/우 회피 모션).
        // 동시에 강도(IncomingStrength)를 실어, 패링 성공 시 쳐냄 반응(ParryAid_L/H)을 결정한다.
        // 적 공격 시스템이 생기면 공격 액티브 직전에 OpenIncomingAttack(window, strength)를 호출하면 된다.
        private float _incomingAttackUntil = -1f;
        public bool           IncomingAttackActive => Time.time <= _incomingAttackUntil;
        public AttackStrength IncomingStrength { get; private set; }
        public void OpenIncomingAttack(float window,
            AttackStrength strength = AttackStrength.Light,
            Transform source = null)
        {
            _incomingAttackUntil = Time.time + window;
            IncomingStrength     = strength;
            _incomingAttackSource = source;
            _perfectDodgePending = false;
        }

        private void Awake()
        {
            _motor         = GetComponent<PlayerMotor>();
            var animator   = GetComponent<CharacterAnimatorBridge>();
            var resources  = GetComponent<PlayerResources>();
            var sensor     = GetComponent<ZZZ.Combat.EnemySensor>();

            var condCtx = new PlayerConditionContext(_motor, transform, this);
            var cfgCtx  = new ConfigContext
            {
                Mover      = _motor,
                Animator   = animator,

                Transform  = transform,
            };
            _state = new ConfigState(
                cfgCtx, this, condCtx, _startConfig,
                _showHitGizmos, _hitGizmoDuration);

            // 협력 객체 조립 — 트리거는 인스펙터에서 만들어진 인스턴스에 런타임 의존만 주입(Init).
            var registry = new ConfigRegistry(_startConfig, _configs);

            // 이 캐릭터가 config에서 참조하는 이펙트 프리팹의 소유권 등록 → 전역 풀 프리웜(파괴 시 해제).
            EffectOwnership.Register(this, OwnedConfigs());

            _input = new InputBuffer(_inputBufferWindow);
            _dodge.Init(this, _state, registry, _input, resources);
            _parry.Init(this, _state, registry, _input);
            _attackNormalEnhance.Init(this, _state, registry, _input, sensor);
            _hit.Init(this, _state, registry, _parry.Prefix);   // 쳐냄 섹션 접두어는 ParryTrigger에서 단일 정의
        }

        // 이펙트 소유권 해제 — 이 프리팹들의 마지막 소유자였다면 전역 풀이 회수된다(모바일 상주 방지).
        private void OnDestroy()
        {
            DeactivateCharacter();
            EffectOwnership.Unregister(this, OwnedConfigs());
        }

        // 이 캐릭터가 쓰는 config 전체(시작 + 이벤트 진입용) — 이펙트 소유권 유도에 사용.
        private IEnumerable<AnimationConfig> OwnedConfigs()
        {
            yield return _startConfig;
            if (_configs != null)
                foreach (var c in _configs) yield return c;
        }

        // Start는 모든 Awake가 끝난 뒤 실행 → CharacterAnimatorBridge 초기화 보장
        private void Start() => ActivateCharacter();

        private void Update()
        {
            if (!_isRunning) return;

            // 회피/패링은 링크 평가 전에 — 콤보보다 우선(공격 중 캔슬)
            if (HasBufferedInput && BufferedInput == ComboInput.Dodge) _dodge.Trigger();
            if (HasBufferedInput && BufferedInput == ComboInput.Parry) _parry.Trigger();

            _state.SetHitDebug(_showHitGizmos, _hitGizmoDuration);
            _state.Update();   // 콤보/섹션 링크 평가 — Attack_Normal_Enhance 링크가 E를 먼저 소비할 기회

            // 강화 공격은 링크가 못 받은 경우의 전역 폴백(after) — E 링크를 가진 섹션(콤보·Rush 등)에선
            // 그 섹션이 직접 E를 처리하므로 폴백을 억제한다. 안 그러면 링크 윈도우가 열리기 전에 폴백이 E를
            // 가로채 일반 강화로 새버린다(예: Rush 윈도우 전 E → RushToEnhance 대신 일반 강화). E 링크가 없는
            // idle/walk 등에서만 이 트리거가 받는다.
            if (HasBufferedInput && BufferedInput == ComboInput.Enhance
                && !_state.ActiveSectionHandles(ComboInput.Enhance)
                && !_state.ActiveSectionBlocks(ComboInput.Enhance))
                _attackNormalEnhance.Trigger();
        }

        // ── 피격 facade (충돌 검출 / 적 공격 시스템 / 테스트 트리거가 호출) ──
        public void TriggerHitFrom(Vector3 attackerPos) => _hit.TriggerFrom(attackerPos, transform);
        public void TriggerHit(string direction = "Back") => _hit.Trigger(direction);

        public HitResult ReceiveHit(in HitContext context)
        {
            if (CanPerfectDodge(context.Source))
            {
                if (context.Source != null)
                    _incomingAttackSource = context.Source;
                NotifyPerfectDodgeSucceeded();
                return HitResult.Ignored;
            }

            if (Invulnerable) return HitResult.Ignored;

            IncomingStrength = context.Definition != null
                ? context.Definition.Strength
                : AttackStrength.Light;
            CloseIncomingAttack();
            Vector3 sourcePosition = context.Source != null
                ? context.Source.position
                : context.HitPoint;
            ReactionTarget = context.Source;
            bool parried = _hit.TriggerFrom(sourcePosition, transform);
            ReactionTarget = null;
            if (parried)
                ParrySucceeded?.Invoke(context);
            return parried ? HitResult.Parried : HitResult.Accepted;
        }

        public void ReceiveParryWarning(in HitContext context, float duration)
        {
            if (context.Definition == null) return;

            OpenIncomingAttack(
                duration, context.Definition.Strength, context.Source);
            ParryWarningReceived?.Invoke(context);
        }

        public void ReceiveParryImpact(in HitContext context)
        {
            if (CanPerfectDodge(context.Source))
            {
                if (context.Source != null)
                    _incomingAttackSource = context.Source;
                NotifyPerfectDodgeSucceeded();
                return;
            }

            CloseIncomingAttack();
        }

        internal void MarkPerfectDodgeCandidate()
        {
            _perfectDodgePending = true;
        }

        private void NotifyPerfectDodgeSucceeded()
        {
            Transform source = _incomingAttackSource;
            CloseIncomingAttack();
            PerfectDodgeSucceeded?.Invoke(source);
        }

        private void CloseIncomingAttack()
        {
            _incomingAttackUntil = -1f;
            _incomingAttackSource = null;
            _perfectDodgePending = false;
        }

        private bool CanPerfectDodge(Transform source)
        {
            if (!_dodge.IsDodging
                || (!_perfectDodgePending && !IncomingAttackActive))
                return false;
            return _incomingAttackSource == null || source == null
                || _incomingAttackSource == source;
        }

        // 패링 스탠스 강제 진입 facade (테스트 트리거가 호출 — 실제 플레이는 OnParry 입력으로 진입)
        public void TriggerParry() => _parry.Trigger();

        // ── 에디터/HUD 라이브 모니터용 ──
        public AnimationConfig CurrentConfig         => _state?.CurrentConfig;
        public int             CurrentClipIndex      => _state?.ActiveIndex ?? -1;
        public string          CurrentSection        => _state?.ActiveSection;
        public float           CurrentNormalizedTime => _state?.CurrentNormalizedTime ?? 0f;
        public MoveDir         CurrentMoveDir         => _state?.CurrentMoveDir ?? MoveDir.Any;

        public void SetMoveInput(Vector2 input)
        {
            if (_motor != null) _motor.SetMoveInput(input);
        }

        public void BufferInput(ComboInput input)
        {
            _input?.Buffer(input);
        }

        public void SetInputHeld(ComboInput input, bool held)
        {
            _input?.SetHeld(input, held);
        }

        public void ClearInput()
        {
            _input?.Clear();
            if (_motor != null) _motor.SetMoveInput(Vector2.zero);
        }

        public void ActivateCharacter()
        {
            if (_isRunning) return;
            _isRunning = true;
            _state.Enter();
        }

        public void DeactivateCharacter()
        {
            if (!_isRunning) return;
            ClearInput();
            _state.Exit();
            _isRunning = false;
        }
    }
}
