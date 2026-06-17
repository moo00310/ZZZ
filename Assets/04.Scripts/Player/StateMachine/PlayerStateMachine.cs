using System.Collections.Generic;
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

        // 이벤트로 진입하는 추가 config들(Hit 등). 링크로 도달하지 않는 진입점만 여기 등록.
        // config가 새로 생기면 개별 필드를 추가할 필요 없이 이 리스트에 드롭만 하면 된다.
        // (어떤 config를 쓸지는 재생할 섹션 이름으로 자동 검색 — FindConfigWithSection)
        [SerializeField] private List<AnimationConfig> _configs = new List<AnimationConfig>();

        [Header("Dodge — 어떤 config에서든 회피 입력 시 강제 진입 (push)")]
        // 회피 config는 _configs에 드롭만 하면 됨. 섹션 이름 규약(접두어+방향)으로 검색해 진입한다.
        [SerializeField] private string _dodgePrefix     = "Evade_";  // 섹션 접두어 (Evade_Front 등)
        [SerializeField, Range(0f, 0.2f)] private float _dodgeBlend = 0.05f;
        [SerializeField, Range(0f, 1f)]   private float _dodgeReinterrupt = 0.3f;  // 회피 중 재입력 무시 임계

        [Header("Hit")]
        // 이미 피격 중일 때, 현재 반응 진행도가 이 값을 넘어야 새 피격이 재시작된다 (A. 재진입 가드).
        // 너무 낮으면 연타 시 frame0에서 덜덜 떨림, 너무 높으면 반응성 둔화.
        [SerializeField, Range(0f, 1f)] private float _hitReinterruptThreshold = 0.3f;

        // 피격 진입 CrossFade 시간. 전이 중에는 root motion이 버려지므로(점프 방지),
        // 이 값이 크면 클립 앞부분의 넉백 root motion이 먹힘 → 작게 둘 것.
        [SerializeField, Range(0f, 0.2f)] private float _hitEntryBlend = 0.03f;

        [Header("Input Buffer")]
        [SerializeField] private float _inputBufferWindow = 0.25f;  // 입력 버퍼 유효 시간

        // 연속 피격 카운트 — 1타=Light, 2타+=Heavy 로 반응 강도 승격(escalation).
        // hit이 아닌 상태에서 새로 맞으면 0으로 리셋됨.
        private int _comboHitCount;

        private ConfigState        _state;   // 단일 config 러너 — 전이는 config가 관리
        private PlayerStateContext _ctx;

        // ── 입력 버퍼 ──────────────────────────────────────────────
        private ComboInput _bufferedInput;
        private float      _bufferedTime = -10f;

        public bool HasBufferedInput => Time.time - _bufferedTime <= _inputBufferWindow;
        public ComboInput BufferedInput => _bufferedInput;
        public void ConsumeInput() => _bufferedTime = -10f;

        // 무적 — i-frame 윈도우 동안 ConfigState가 매 프레임 세팅. TriggerHit가 이 값을 보고 무시.
        public bool Invulnerable { get; set; }

        // ── 퍼펙트 회피 윈도우 ─────────────────────────────────────
        // 적이 "공격 적중 직전" 이 창을 열어두면, 그 사이 회피 = 퍼펙트(좌/우 회피 모션).
        // 적 공격 시스템이 생기면 공격 액티브 직전에 OpenIncomingAttack(window)를 호출하면 된다.
        private float _incomingAttackUntil = -1f;
        public bool IncomingAttackActive => Time.time <= _incomingAttackUntil;
        public void OpenIncomingAttack(float window) => _incomingAttackUntil = Time.time + window;

        // 테스트용 — 예고된 적 공격 시뮬레이션(K). 윈도우를 열고 끝나면 공격 적중.
        [SerializeField] private float _testTelegraphWindow = 0.4f;
        private float _testPendingHitAt = -1f;

        private void Awake()
        {
            var controller = GetComponent<PlayerController>();
            var animator   = GetComponent<PlayerAnimatorBridge>();
            var cc         = GetComponent<CharacterController>();

            _ctx = new PlayerStateContext(controller, animator, cc, transform);

            _state = new ConfigState(_ctx, this, _startConfig);
        }

        // Start는 모든 Awake가 끝난 뒤 실행 → PlayerAnimatorBridge._animator 초기화 보장
        private void Start() => _state.Enter();

        private void Update()
        {
            // ── 테스트용 피격 트리거 ── H=등 뒤(Back), J=정면(Front). 연속타는 L↔H 교대
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.hKey.wasPressedThisFrame) TriggerHit("Back");
                if (kb.jKey.wasPressedThisFrame) TriggerHit("Front");

                // K = 예고된 적 공격 — 윈도우를 열고 끝에 적중. 그 사이 회피하면 퍼펙트.
                if (kb.kKey.wasPressedThisFrame)
                {
                    OpenIncomingAttack(_testTelegraphWindow);
                    _testPendingHitAt = Time.time + _testTelegraphWindow;
                }
            }

            if (_testPendingHitAt > 0f && Time.time >= _testPendingHitAt)
            {
                _testPendingHitAt = -1f;
                TriggerHit("Front");   // 적중 (i-frame 중이면 자동 무시 = 회피 성공)
            }

            // 회피는 링크 평가 전에 — 콤보보다 우선(공격 중 캔슬)
            if (HasBufferedInput && _bufferedInput == ComboInput.Dodge) TriggerDodge();
            _state.Update();
        }

        // 충돌 검출에서 호출 — 공격자 위치로 Front/Back 판정 후 진입.
        public void TriggerHitFrom(Vector3 attackerPos)
        {
            Vector3 to = attackerPos - transform.position;
            bool back = Vector3.Dot(transform.forward, to) < 0f;   // 등 뒤에서 맞음
            TriggerHit(back ? "Back" : "Front");
        }

        // 외부 이벤트(피격)로 Hit config 진입.
        //   direction : 반응 방향("Front"/"Back") → 섹션 이름에 사용
        //   A. 재진입 가드 — 이미 피격 중이고 진행도가 임계값 미만이면 무시(연타 stunlock/프리즈 방지)
        //   escalation — 연속타 카운트로 강도 승격: 1타=L, 2타+=H
        public void TriggerHit(string direction = "Back")
        {
            if (Invulnerable) return;   // 회피 i-frame 중이면 피격 무시
            var cs = _state;

            // 등록된 config 중 이번 피격 섹션(L/H 둘 다 같은 config에 있음)을 가진 것을 찾는다.
            AnimationConfig hitConfig = FindConfigWithSection($"Hit_L_{direction}")
                                     ?? FindConfigWithSection($"Hit_H_{direction}");
            if (hitConfig == null)
            {
                Debug.LogWarning($"[Hit] 'Hit_*_{direction}' 섹션을 가진 config가 없음 — PlayerStateMachine 인스펙터 'Configs' 리스트 확인", this);
                return;
            }

            bool inHit = cs.CurrentConfig == hitConfig;

            // A. 재진입 가드: 피격 반응이 충분히 진행되기 전엔 새 피격 무시
            if (inHit && cs.CurrentNormalizedTime < _hitReinterruptThreshold)
                return;

            // 새 피격(걷기 등에서 진입)이면 콤보 리셋, 연속타면 누적
            if (!inHit) _comboHitCount = 0;
            _comboHitCount++;

            string strength = (_comboHitCount % 2 == 1) ? "L" : "H";   // 홀수타=L, 짝수타=H 교대 → 연출 풍부
            cs.InterruptWith(hitConfig, $"Hit_{strength}_{direction}", _hitEntryBlend);
        }

        // 회피 진입 (push) — 버퍼에 Dodge가 있으면 호출. Hit과 같은 방식:
        // 입력 상태로 섹션 이름(접두어+방향)을 만들고 _configs에서 검색해 강제 진입한다.
        private void TriggerDodge()
        {
            string section = _dodgePrefix + DodgeSuffix();
            var cfg = FindConfigWithSection(section);
            if (cfg == null)
            {
                Debug.LogWarning($"[Dodge] '{section}' 섹션을 가진 config가 없음 — PlayerStateMachine 'Configs' 리스트와 섹션 이름 확인", this);
                return;
            }

            // 재진입 가드 — 이미 회피 중이고 진행도가 낮으면 무시(연타 프리즈 방지)
            if (_state.CurrentConfig == cfg && _state.CurrentNormalizedTime < _dodgeReinterrupt)
                return;

            ConsumeInput();
            _state.InterruptWith(cfg, section, _dodgeBlend);
        }

        // 회피 섹션 선택:
        //   무입력/아래        → Back  (회전 없이 백스텝)
        //   방향(W/A/D) 일반   → Front (진입 시 FaceInputOnEnter로 입력 방향 회전)
        //   방향 + 퍼펙트 타이밍 → Left/Right (좌우 회피 모션 — 입력 좌=Left, 그 외=Right)
        private string DodgeSuffix()
        {
            MoveDir d = CurrentMoveDir;
            bool directional = d == MoveDir.Forward || d == MoveDir.Left || d == MoveDir.Right;
            if (!directional) return "Back";

            if (IncomingAttackActive)               // 적 공격 윈도우 안 → 퍼펙트
                return d == MoveDir.Left ? "Left" : "Right";   // W(정면)은 Right로

            return "Front";
        }

        // 등록된 config(시작 config + Configs 리스트)에서 해당 섹션을 가진 첫 config 반환 (없으면 null)
        private AnimationConfig FindConfigWithSection(string section)
        {
            if (_startConfig != null && _startConfig.IndexOfSection(section) >= 0) return _startConfig;
            foreach (var c in _configs)
                if (c != null && c.IndexOfSection(section) >= 0) return c;
            return null;
        }

        // ── 에디터/HUD 라이브 모니터용 ──
        public AnimationConfig CurrentConfig         => _state?.CurrentConfig;
        public int             CurrentClipIndex      => _state?.ActiveIndex ?? -1;
        public string          CurrentSection        => _state?.ActiveSection;
        public float           CurrentNormalizedTime => _state?.CurrentNormalizedTime ?? 0f;
        public MoveDir         CurrentMoveDir         => _state?.CurrentMoveDir ?? MoveDir.Any;

        // ── 입력 콜백 (PlayerInput SendMessages) ──────────────────
        private void OnAttack(InputValue value)  { if (value.isPressed) BufferInput(ComboInput.Normal); }
        private void OnDodge(InputValue value)    { if (value.isPressed) BufferInput(ComboInput.Dodge); }

        private void BufferInput(ComboInput input)
        {
            // 입력만 버퍼링 — 실제 콤보 진입은 config의 Input 링크(TargetConfig=콤보)가 처리
            _bufferedInput = input;
            _bufferedTime  = Time.time;
        }
    }
}
