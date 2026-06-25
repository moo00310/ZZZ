using UnityEngine;
using UnityEngine.InputSystem;

namespace ZZZ.Player
{
    using ZZZ;   // MoveDir

    // 플레이어의 현재 이동/회전 모드 스냅샷 — 개별 bool에서 매 프레임 파생(CurrentFlags).
    // 한 줄 로그/인스펙터/HUD 표시용. 상태 변경은 여전히 개별 bool로 한다(여기에 쓰지 않음).
    [System.Flags]
    public enum PlayerStateFlags
    {
        None           = 0,
        CodeMovement   = 1 << 0,   // UseCodeMovement (코드 이동)
        RootMotion     = 1 << 1,   // IsRootMotionActive (루트모션 구동 중)
        RotationLocked = 1 << 2,   // !AllowRotation (회전 잠금)
        WarpActive     = 1 << 3,   // WarpWindowActive (타겟 워프 윈도우)
    }

    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Locomotion")]
        [SerializeField] private float _rotationSpeed = 15f;
        // 회전 잠금(LockRotation) 해제 후 회전 속도를 0→최대로 올리는 이즈인 시간(초). 0이면 즉시(이즈 없음).
        [SerializeField] private float _rotationEaseTime = 0.2f;

        [Header("Root Motion")]
        // 위치 루트모션 추출원: 수평(X,Z) 델타→CC 이동, 메시는 X·Z 0 리셋 / Y(수직 바운스) 유지.
        [SerializeField] private Transform _bip001Bone;
        // 회전(턴) yaw 추출원 — Root 본. 여기 yaw 델타를 transform에 적용한다.
        [SerializeField] private Transform _rootBone;
        [SerializeField] private float     _rootMotionScale = 1f;

        [Header("Gravity")]
        [SerializeField] private float _gravity         = -20f;
        [SerializeField] private float _groundedGravity = -2f;

        private CharacterController _cc;
        private Animator            _animator;
        private Camera              _mainCamera;
        private float               _verticalVelocity;
        private Vector3             _prevRootPos;
        private Vector3             _loopVelLocal;       // 측정된 루프 전진 속도(로컬, units/s) — 0이면 아직 미측정
        private Vector3             _loopAccumLocal;     // 현재 루프 동안 누적한 수평 전진 델타(로컬)
        private float               _loopAccumTime;      // 현재 루프 동안 누적 시간(초)
        private bool                _flushRootPosPending;
        private bool                _wasInTransition;    // 직전 프레임이 전이 중이었는지 (전이 종료 직후 1프레임 스냅 검출용)
        private float               _prevRootNormFrac;   // 직전 프레임 normalizedTime의 소수부 (루프 wrap 검출용)
        private float               _prevRootYaw;        // 직전 프레임 Root 본 yaw(도) — transform 적용용
        private float               _rootYawComp;        // 섹션 시작 이후 transform에 적용한 누적 Root yaw(도) — 메시 카운터/위치 보정용
        private bool                _flushRootRotPending; // 섹션 진입 시 회전 추출 baseline/누적 리셋 대기
        private bool                _wasExtracting;      // 직전 프레임에 추출 중이었는지 (진입 baseline 검출용)

        private Vector2 _moveInput;
        private Vector3 _moveDirection;
        private float   _rotationEase;   // 회전 이즈인 0~1 — 잠금 중 0, 해제 후 _rotationEaseTime 동안 1로 램프

        // 시작 부스트 (클립 시작 시 진행 방향으로 짧게 가속 → 루트모션 워밍업 보완)
        private float _boostSpeed;
        private float _boostDuration;
        private float _boostTimeLeft;

        // 타겟 워프 — 공격 루트모션을 적 방향으로 재조준 (ConfigState가 설정).
        // 이동(translate)과 회전(faceTarget)은 독립 — 회전만 켜고 이동 워프는 끌 수 있다.
        private Transform _warpTarget;         // 이동/회전 공통 타겟 (같은 적)
        private float     _warpStopDistance;
        private bool      _warpTranslate;      // 루트모션 수평 이동을 적 방향으로 끌어올지 (false면 회전만, 이동 워프 없음)
        private bool      _faceEnabled;        // FaceWindow 동안 타겟 향해 회전(스냅/락온 통합)
        private float     _faceTurnSpeed;      // 회전 각속도(도/초). 0 = 즉시(스냅)
        private ZZZ.Combat.EnemySensor _enemySensor;

        public bool UseCodeMovement { get; set; } = true;
        public bool AllowRotation   { get; set; } = true;   // false면 이동 입력이 있어도 캐릭터 회전 안 함 (피격/경직 등)
        public bool SmoothLoopSpeed { get; set; } = false;  // 루프 전진 평속화 (틱 제거). 기본 끔 — 섹션(TrackClip)별로 ConfigState가 설정
        public bool ExtractRootRotation { get; set; } = false;  // 이 섹션에서 Root 본 yaw를 transform에 적용 (턴 섹션). ConfigState가 설정
        public float BackMotionScale { get; set; } = 1f;        // 후진(-Z) 루트모션 증폭 배율. 섹션(TrackClip)별로 ConfigState가 설정

        // 실제 수평 이동 속도 (루트모션 포함) — 애니메이터 블렌드/HUD 표시용
        public float   CurrentSpeed  => new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude;
        public Vector3 MoveDirection => _moveDirection;

        // 원시 WASD 입력 기준 방향 (W=Forward) — 콤보 Link 조건 판정용
        public MoveDir CurrentMoveDir
        {
            get
            {
                if (_moveInput.sqrMagnitude < 0.01f) return MoveDir.Neutral;
                if (Mathf.Abs(_moveInput.y) >= Mathf.Abs(_moveInput.x))
                    return _moveInput.y > 0f ? MoveDir.Forward : MoveDir.Back;
                return _moveInput.x > 0f ? MoveDir.Right : MoveDir.Left;
            }
        }

        // 라이브 모니터용
        public bool  IsRootMotionActive => !UseCodeMovement && _bip001Bone != null;
        public float LastRootDelta      { get; private set; }

        // 개별 bool에서 파생된 모드 스냅샷 — 디버그/로그 표시용 (상태 변경엔 쓰지 않음)
        public PlayerStateFlags CurrentFlags
        {
            get
            {
                PlayerStateFlags f = PlayerStateFlags.None;
                if (UseCodeMovement)    f |= PlayerStateFlags.CodeMovement;
                if (IsRootMotionActive) f |= PlayerStateFlags.RootMotion;
                if (!AllowRotation)     f |= PlayerStateFlags.RotationLocked;
                if (WarpWindowActive)   f |= PlayerStateFlags.WarpActive;
                return f;
            }
        }

        // 섹션 진입 시 호출 — 다음 프레임 baseline을 리셋해 전환 시 점프 방지.
        public void FlushRootPos() => _flushRootPosPending = true;
        // 섹션 진입(재진입 포함) 시 호출 — 회전 추출 baseline/누적을 리셋. 턴→턴 재진입에서 _rootYawComp가
        // 안 리셋돼 카운터가 과하게 빼던 문제 방지.
        public void FlushRootRotation() => _flushRootRotPending = true;

        // ── 타겟 워프 API (ConfigState가 구동) ─────────────────────
        public ZZZ.Combat.EnemySensor EnemySensor => _enemySensor;
        public bool WarpWindowActive { get; set; }   // 이동 워프 윈도우 안인지 — ConfigState가 매 프레임 갱신
        public bool FaceWindowActive { get; set; }   // 타겟 조준 윈도우 안인지 — ConfigState가 매 프레임 갱신

        // translate = 이동 워프(EnableTracking), face = 타겟 조준(FaceTarget) — 둘은 독립. 같은 적 타겟 공유.
        public void SetWarpTarget(Transform target, float stopDistance, bool translate,
            bool face = false, float faceTurnSpeed = 720f)
        {
            _warpTarget       = target;
            _warpStopDistance = stopDistance;
            _warpTranslate    = translate;
            _faceEnabled      = face;
            _faceTurnSpeed    = faceTurnSpeed;
            WarpWindowActive  = false;
            FaceWindowActive  = false;
        }

        public void ClearWarpTarget()
        {
            _warpTarget      = null;
            _warpTranslate   = false;
            _faceEnabled     = false;
            WarpWindowActive = false;
            FaceWindowActive = false;
        }

        // 즉시 회전 (공격 진입 시 타겟 방향 스냅)
        public void FaceToward(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(worldDir);
        }

        private const float k_moveThreshold = 0.01f;

        private void Awake()
        {
            _cc          = GetComponent<CharacterController>();
            _animator    = GetComponentInChildren<Animator>();
            _mainCamera  = Camera.main;
            _enemySensor = GetComponent<ZZZ.Combat.EnemySensor>();
        }

        private void Update()
        {
            ApplyGravity();
            Move();
            UpdateWarpFacing();
            ApplyStartBoost();
        }

        // 타겟 조준 facing — FaceWindow 동안 매 프레임 타겟을 향해 transform을 회전한다.
        // FaceWindow[0,0](진입 1회)면 스냅, 넓히면 적이 움직여도 따라붙는 락온. TurnSpeed 0 = 즉시.
        // 입력 회전 잠금(LockRotation)과 무관하게 회전 권한을 갖는다. 단, 섹션 턴(ExtractRootRotation)이 회전을 소유하면 양보.
        private void UpdateWarpFacing()
        {
            if (!_faceEnabled || _warpTarget == null || !FaceWindowActive) return;
            if (ExtractRootRotation) return;

            Vector3 to = _warpTarget.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(to);
            transform.rotation = _faceTurnSpeed > 0f
                ? Quaternion.RotateTowards(transform.rotation, target, _faceTurnSpeed * Time.deltaTime)
                : target;
        }

        // 클립 시작 시 진행 방향으로 짧게 이동 보강 (ConfigState가 섹션 진입 때 호출)
        public void AddStartBoost(float speed, float duration)
        {
            if (speed <= 0f || duration <= 0f) { _boostTimeLeft = 0f; return; }
            _boostSpeed    = speed;
            _boostDuration = duration;
            _boostTimeLeft = duration;
        }

        // 진행 방향(입력 있으면 입력 방향, 없으면 바라보는 방향)으로 부스트를 적용하며 0까지 감쇠
        private void ApplyStartBoost()
        {
            if (_boostTimeLeft <= 0f) return;
            _boostTimeLeft -= Time.deltaTime;
            float ramp = Mathf.Clamp01(_boostTimeLeft / _boostDuration);   // 1 → 0

            Vector3 dir = _moveDirection.sqrMagnitude > k_moveThreshold
                ? _moveDirection : transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) dir.Normalize();

            float step = _boostSpeed * ramp * Time.deltaTime;

            // 이동 워프가 켜졌고 타겟이 있으면 부스트도 타겟 방향 + StopDistance 클램프
            // (제자리 공격 클립의 흡착을 부스트가 담당). 회전만 켠 경우(_warpTranslate=false)엔 원본 방향 유지.
            if (_warpTarget != null && _warpTranslate)
            {
                Vector3 to = _warpTarget.position - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > 0.001f)
                {
                    dir  = to / dist;
                    step = Mathf.Min(step, Mathf.Max(0f, dist - _warpStopDistance));
                }
            }
            _cc.Move(dir * step);
        }

        private void OnMove(InputValue value)   => _moveInput   = value.Get<Vector2>();

        private void Move()
        {
            // 회전 이즈인 — 잠금(LockRotation) 동안 회전 각속도가 0이다가 해제 순간 Slerp가
            // 최대 각속도로 튀는 것("툭")을 막는다. 해제 후 _rotationEaseTime 동안 0→1로 램프.
            // 잠금이 없으면 항상 1이라 일반 보행 회전감은 그대로.
            _rotationEase = AllowRotation
                ? (_rotationEaseTime > 0f
                    ? Mathf.MoveTowards(_rotationEase, 1f, Time.deltaTime / _rotationEaseTime)
                    : 1f)
                : 0f;

            if (_moveInput.sqrMagnitude < k_moveThreshold)
            {
                _moveDirection = Vector3.zero;
            }
            else
            {
                Vector3 camForward = _mainCamera.transform.forward;
                Vector3 camRight   = _mainCamera.transform.right;
                camForward.y = 0f;
                camRight.y   = 0f;
                camForward.Normalize();
                camRight.Normalize();

                // 입력 방향(카메라 기준)만 계산 — 실제 이동은 루트모션이 담당.
                // 이 방향은 회전/타겟 워프/부스트/콤보 방향 판정에 쓰인다.
                _moveDirection = (camForward * _moveInput.y + camRight * _moveInput.x).normalized;

                if (AllowRotation)
                    RotateToward(_moveDirection, _rotationSpeed * _rotationEase);
            }
        }

        private void LateUpdate()
        {
            if (_bip001Bone == null) { LastRootDelta = 0f; return; }

            bool transitioning = UpdateTransitionFlags();
            UpdateRootRotation(transitioning);   // Root yaw → transform + 메시 카운터
            ApplyRootMotion(transitioning);      // Bip001 수평 델타 → CharacterController
        }

        // 전이(CrossFade) 중인가 + 전이가 막 끝난 첫 프레임인가. 회전·위치 양쪽이 같은 가드를 쓴다.
        private bool UpdateTransitionFlags()
        {
            bool inTransition  = _animator != null && _animator.IsInTransition(0);
            bool transitioning = inTransition || _wasInTransition;
            _wasInTransition   = inTransition;
            return transitioning;
        }

        // ── 회전: Root yaw를 transform에 적용 + 메시에서는 그만큼 되돌림 ──
        // Root 본의 깨끗한 yaw를 transform이 흡수하고(이동/다음 섹션 facing용), 그 회전이 메시(Bip001
        // 이하)에 이중(≈360)으로 안 돌도록 transform에 넣은 누적분(_rootYawComp)만큼 Bip001을 월드에서
        // 되돌린다 → 메시는 애니 원본 재생(자연 sway 유지). 측정은 Root(깨끗), 카운터 대상은 Bip001.
        private void UpdateRootRotation(bool transitioning)
        {
            // 추출 — 진입/전이 프레임은 Root 값이 두 클립 블렌드라 오염 → baseline만 갱신, 추출 스킵
            // (안 그러면 블렌드 변화가 _rootYawComp에 쌓여 재진입 시 이동이 틀어짐).
            // Bip001(골반)은 기울기/sway로 측정이 흔들려 못 쓰고, up축 twist 분해로 Root를 잰다(강건).
            if (ExtractRootRotation && _rootBone != null)
            {
                float rootCur = TwistYawOf(_rootBone.localRotation);
                bool enter = !_wasExtracting || _flushRootRotPending;   // 섹션 진입(타 섹션 OR 같은 턴 재진입)
                if (enter || transitioning)
                {
                    _prevRootYaw = rootCur;
                    if (enter) _rootYawComp = 0f;
                }
                else
                {
                    float rootDelta = Mathf.DeltaAngle(_prevRootYaw, rootCur);
                    _rootYawComp += rootDelta;
                    _prevRootYaw  = rootCur;
                    if (Mathf.Abs(rootDelta) > 1e-5f)
                        transform.rotation = Quaternion.AngleAxis(rootDelta, Vector3.up) * transform.rotation;
                }
                _wasExtracting       = true;
                _flushRootRotPending = false;
            }
            else _wasExtracting = false;

            // 카운터 — 즉시 전환(페이드 없음). 턴 힙(+180)·run 힙(+0)이 ~180 달라 블렌드로 섞으면 Bip001
            // yaw가 slerp로 비선형 휘청 → 카운터(선형)가 못 따라간다. 그래서 턴→run 링크 BlendDuration=0
            // (하드컷) + 카운터도 턴 벗어나는 즉시 0. 양쪽 프레임 모두 mesh=transform facing이라 연속.
            float counterYaw = ExtractRootRotation ? _rootYawComp : 0f;
            if (!ExtractRootRotation) _rootYawComp = 0f;
            if (_rootBone != null && Mathf.Abs(counterYaw) > 1e-5f)
                _bip001Bone.rotation = Quaternion.AngleAxis(-counterYaw, Vector3.up) * _bip001Bone.rotation;
        }

        // ── 위치 루트모션 — Bip001의 수평(X,Z) 델타를 CharacterController 이동으로 ──
        // 이동/스웨이/바운스가 전부 Bip001에 구워져 있다(별도 Root 노드엔 position 커브 없음).
        // 수평만 뽑아 이동에 쓰고, 메시에선 X·Z를 0으로 죽여 드리프트/발 미끄러짐 방지(Y 바운스는 유지).
        private void ApplyRootMotion(bool transitioning)
        {
            Vector3 currentPos = _bip001Bone.localPosition;

            if (!UseCodeMovement)
            {
                Vector3 deltaLocal = ComputeRootDeltaLocal(currentPos, transitioning);

                // 후진 루트모션 증폭 — 캐릭터 기준 뒤로(-Z) 가는 성분만 배율 적용(전진/측면 불변).
                // 스텝백·recoil 모션을 강조하고 싶을 때 섹션별로 BackMotionScale을 키운다.
                if (BackMotionScale != 1f && deltaLocal.z < 0f)
                    deltaLocal.z *= BackMotionScale;

                // 턴 섹션에선 deltaLocal에 이미 회전이 들어있는데 transform도 Root yaw만큼 돌아가므로,
                // 그대로 변환하면 이중 적용돼 거꾸로/사선으로 간다 → 누적 Root yaw를 빼 섹션 시작 회전 기준으로.
                Vector3 worldDelta = transform.TransformDirection(deltaLocal);
                if (ExtractRootRotation)
                    worldDelta = Quaternion.AngleAxis(-_rootYawComp, Vector3.up) * worldDelta;
                Vector3 move = worldDelta * _rootMotionScale;

                // 전이 구간(+종료 직후 1프레임)은 두 클립 포즈의 가중 평균이라 그 프레임 델타는 실제 이동이
                // 아니라 블렌딩 아티팩트("스냅") → 수평 통째로 버린다. (전이 종료 후 baseline 재설정으로 재개)
                if (transitioning) { move.x = 0f; move.z = 0f; }

                WarpRootMotion(ref move);
                move.y = _verticalVelocity * Time.deltaTime;
                _cc.Move(move);
                LastRootDelta = new Vector3(deltaLocal.x, 0f, deltaLocal.z).magnitude * _rootMotionScale;
            }
            else
            {
                LastRootDelta = 0f;
            }

            // 메시 드리프트 방지: 수평(X,Z)은 0으로 죽이고 Y(수직 바운스)는 유지.
            currentPos.x = 0f;
            currentPos.z = 0f;
            _bip001Bone.localPosition = currentPos;
        }

        // Bip001 수평 델타 계산 — 루프 wrap/섹션 진입 baseline 처리 + 루프 전진 평속화(SmoothLoopSpeed).
        private Vector3 ComputeRootDeltaLocal(Vector3 currentPos, bool transitioning)
        {
            // 루프 클립이 끝(≈1)에서 처음(≈0)으로 되감기면 baked 위치가 뒤로 점프 → 그 프레임은 버린다.
            bool wrapped = false;
            if (_animator != null)
            {
                float nt   = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                float frac = nt - Mathf.Floor(nt);
                wrapped = frac + 0.0001f < _prevRootNormFrac;
                _prevRootNormFrac = frac;
            }

            // 섹션 진입 첫 프레임만 baseline을 잡고(점프 방지), 루프 wrap 프레임도 baseline 재설정.
            bool flushing = _flushRootPosPending;
            if (_flushRootPosPending || wrapped)
            {
                _prevRootPos         = currentPos;
                _flushRootPosPending = false;
            }
            if (flushing)   // 새 섹션 진입 → 이전 클립의 루프 측정값 폐기
            {
                _loopVelLocal   = Vector3.zero;
                _loopAccumLocal = Vector3.zero;
                _loopAccumTime  = 0f;
            }

            Vector3 rawDelta = currentPos - _prevRootPos;
            _prevRootPos     = currentPos;
            rawDelta.y = 0f;   // 수평만 — Y 바운스는 메시에 남기고 캐릭터 수직은 중력 담당
            if (flushing || wrapped) rawDelta = Vector3.zero;   // baseline/되감기 프레임은 위치 점프라 이동 0

            // ── 루프 전진 평속화 (틱 제거) ────────────────────────────
            // 루프 전진(Z) 커브가 끝에서 한 프레임 멈췄다 되감겨 본체만 멈칫("틱")한다. 한 루프 평균
            // 전진속도를 재서 다음 루프부터 일정 전진 → 멈칫 제거(평균이라 안 빨라지고 보폭 유지).
            // 비루프 클립(공격 런지 등)은 원본 델타 그대로.
            bool isLooping = _animator != null && _animator.GetCurrentAnimatorStateInfo(0).loop;
            if (SmoothLoopSpeed && isLooping && !transitioning)
            {
                if (!flushing && !wrapped)
                {
                    _loopAccumLocal += rawDelta;
                    _loopAccumTime  += Time.deltaTime;
                }
                if (wrapped && _loopAccumTime > 0.01f)   // 한 루프 끝 → 평균속도 갱신
                {
                    _loopVelLocal   = _loopAccumLocal / _loopAccumTime;
                    _loopAccumLocal = Vector3.zero;
                    _loopAccumTime  = 0f;
                }
                return _loopVelLocal.sqrMagnitude > 1e-10f
                    ? _loopVelLocal * Time.deltaTime   // 측정된 평속으로 일정 전진
                    : rawDelta;                        // 첫 루프: 아직 평속 미측정 → 원본
            }

            _loopVelLocal   = Vector3.zero;   // 루프 벗어남 → 측정값 폐기
            _loopAccumLocal = Vector3.zero;
            _loopAccumTime  = 0f;
            return rawDelta;
        }

        // 루트모션 수평 이동을 타겟 방향으로 재조준하고 StopDistance 앞에서 멈춘다.
        // 크기는 원본 delta를 유지 → 애니메이션이 만든 속도감 보존, 방향만 휘어짐.
        private void WarpRootMotion(ref Vector3 move)
        {
            if (_warpTarget == null || !WarpWindowActive || !_warpTranslate) return;

            Vector3 to = _warpTarget.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.001f) return;

            Vector3 dir   = to / dist;
            Vector3 horiz = new Vector3(move.x, 0f, move.z);

            // 타겟에서 멀어지는(뒤로 빠지는) 모션은 재조준하지 않고 원본 그대로 통과 → 스텝백·recoil 보존.
            // (BackMotionScale로 키운 후진 모션이 워프에 빨려 타겟 쪽으로 뒤집히던 문제 방지)
            if (Vector3.Dot(horiz, dir) <= 0f) return;

            // 이미 StopDistance 안쪽이면 재조준/클램프 없이 원본 루트모션을 그대로 통과시킨다.
            // (코앞 공격에서 remain=0이라 lunge가 통째로 0이 되던 문제 방지 — 라운지 보존)
            float remain = dist - _warpStopDistance;
            if (remain <= 0f) return;

            float step = Mathf.Min(horiz.magnitude, remain);
            move.x = dir.x * step;
            move.z = dir.z * step;
        }

        // 쿼터니언의 up축 yaw(도) — forward 투영 기준. DeltaAngle 누적에 안정적(eulerAngles 점프 회피).
        // up축(0,1,0) swing-twist 분해의 twist 각도(도). forward 투영(YawOf 옛 방식)은 본이 기울면
        // forward가 거의 수직이 돼 heading이 요동치지만, twist 분해는 up축 회전만 깨끗이 분리해
        // 기울어진 골반(Bip001)에서도 안정적이다. q와 −q는 같은 회전이라 부호만 통일하면 됨.
        private static float TwistYawOf(Quaternion q)
        {
            float y = q.y, w = q.w;
            if (w < 0f) { y = -y; w = -w; }            // 반구 통일 (부호 점프 방지)
            if (y * y + w * w < 1e-12f) return 0f;     // up축 거의 90° swing → twist 정의 불가, 0 처리
            return 2f * Mathf.Atan2(y, w) * Mathf.Rad2Deg;
        }

        private void RotateToward(Vector3 direction, float speed)
        {
            Quaternion target = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, speed * Time.deltaTime);
        }

        private void ApplyGravity()
        {
            if (_cc.isGrounded)
                _verticalVelocity = _groundedGravity;
            else
                _verticalVelocity += _gravity * Time.deltaTime;

            if (UseCodeMovement)
                _cc.Move(Vector3.up * (_verticalVelocity * Time.deltaTime));
        }
    }
}
