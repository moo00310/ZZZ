using UnityEngine;
using UnityEngine.InputSystem;

namespace ZZZ.Player
{
    using ZZZ;   // MoveDir

    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Locomotion")]
        [SerializeField] private float _moveSpeed     = 5f;
        [SerializeField] private float _sprintSpeed   = 7f;
        [SerializeField] private float _rotationSpeed = 15f;

        [Header("Root Motion")]
        [SerializeField] private Transform _rootBone;       // 이동량 추출 후 로컬 0으로 리셋
        [SerializeField] private Transform _bip001Bone;     // 메시 드리프트 방지용 XZ 리셋
        [SerializeField] private float     _rootMotionScale = 1f;

        [Header("Gravity")]
        [SerializeField] private float _gravity         = -20f;
        [SerializeField] private float _groundedGravity = -2f;

        private CharacterController _cc;
        private Animator            _animator;
        private Camera              _mainCamera;
        private float               _verticalVelocity;
        private Vector3             _prevRootPos;
        private bool                _flushRootPosPending;
        private float               _prevRootNormFrac;   // 직전 프레임 normalizedTime의 소수부 (루프 wrap 검출용)

        private Vector2 _moveInput;
        private bool    _isSprinting;
        private float   _currentSpeed;
        private Vector3 _moveDirection;

        // 시작 부스트 (클립 시작 시 진행 방향으로 짧게 가속 → 루트모션 워밍업 보완)
        private float _boostSpeed;
        private float _boostDuration;
        private float _boostTimeLeft;

        // 타겟 워프 — 공격 루트모션을 적 방향으로 재조준 (ConfigState가 설정)
        private Transform _warpTarget;
        private float     _warpStopDistance;
        private ZZZ.Combat.EnemySensor _enemySensor;

        public bool UseCodeMovement { get; set; } = true;
        public bool AllowRotation   { get; set; } = true;   // false면 이동 입력이 있어도 캐릭터 회전 안 함 (피격/경직 등)

        public float   CurrentSpeed  => _currentSpeed;
        public bool    IsSprinting   => _isSprinting;
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
        public bool  IsRootMotionActive => !UseCodeMovement && _rootBone != null;
        public float LastRootDelta      { get; private set; }

        public void FlushRootPos() => _flushRootPosPending = true;

        // ── 타겟 워프 API (ConfigState가 구동) ─────────────────────
        public ZZZ.Combat.EnemySensor EnemySensor => _enemySensor;
        public bool WarpWindowActive { get; set; }   // 트래킹 윈도우 안인지 — ConfigState가 매 프레임 갱신

        public void SetWarpTarget(Transform target, float stopDistance)
        {
            _warpTarget       = target;
            _warpStopDistance = stopDistance;
            WarpWindowActive  = false;
        }

        public void ClearWarpTarget()
        {
            _warpTarget      = null;
            WarpWindowActive = false;
        }

        // 즉시 회전 (공격 진입 시 타겟 방향 스냅)
        public void FaceToward(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(worldDir);
        }

        // 공격 연출용 추가 yaw(도) — bip001에 애니 포즈 위로 얹는다. ConfigState가 구동하며
        // 섹션 종료 시 0으로 복귀(임시 비주얼). 루트/카메라/이동 방향은 건드리지 않는다.
        private float _bodyYaw;
        public void SetBodyYaw(float degrees) => _bodyYaw = degrees;

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
            ApplyStartBoost();
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

            // 워프 타겟이 있으면 부스트도 타겟 방향 + StopDistance 클램프
            // (제자리 공격 클립의 흡착을 부스트가 담당)
            if (_warpTarget != null)
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
        private void OnSprint(InputValue value) => _isSprinting = value.isPressed;

        private void Move()
        {
            if (_moveInput.sqrMagnitude < k_moveThreshold)
            {
                _currentSpeed  = 0f;
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

                _moveDirection = (camForward * _moveInput.y + camRight * _moveInput.x).normalized;

                float speed = _isSprinting ? _sprintSpeed : _moveSpeed;
                _currentSpeed = speed;

                if (UseCodeMovement)
                    _cc.Move(_moveDirection * (speed * Time.deltaTime));

                if (AllowRotation)
                    RotateToward(_moveDirection, _rotationSpeed);
            }
        }

        private void LateUpdate()
        {
            // Bip001 XZ 리셋 — Y는 유지 (메시 수직 움직임 보존)
            if (_bip001Bone != null)
            {
                Vector3 local = _bip001Bone.localPosition;
                local.x = 0f;
                local.z = 0f;
                _bip001Bone.localPosition = local;

                // 공격 연출용 추가 yaw — 애니 포즈(클립 회전) 위에 엉덩이 피벗으로 얹는다.
                // Animator가 매 프레임 클립 값으로 덮으므로 누적되지 않고 오프셋만 더해진다.
                if (Mathf.Abs(_bodyYaw) > 1e-4f)
                    _bip001Bone.rotation = Quaternion.AngleAxis(_bodyYaw, Vector3.up) * _bip001Bone.rotation;
            }

            // Root 이동량 추출 → CharacterController에 적용 후 로컬 위치 리셋
            // 매 프레임 0 기준으로 읽으므로 루프/전환 튀는 현상 없음
            if (_rootBone != null && !UseCodeMovement)
            {
                Vector3 currentPos = _rootBone.localPosition;

                bool inTransition = _animator != null && _animator.IsInTransition(0);

                // 루프 클립이 끝(≈1)에서 처음(≈0)으로 되감기면 baked 루트 위치가 뒤로 점프한다.
                // normalizedTime 소수부가 줄어든 프레임 = wrap → 그 프레임 델타는 버린다.
                bool wrapped = false;
                if (_animator != null)
                {
                    float nt   = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    float frac = nt - Mathf.Floor(nt);
                    wrapped = frac + 0.0001f < _prevRootNormFrac;
                    _prevRootNormFrac = frac;
                }

                if (_flushRootPosPending || inTransition || wrapped)
                {
                    _prevRootPos         = currentPos;
                    _flushRootPosPending = false;
                }

                Vector3 deltaLocal      = currentPos - _prevRootPos;
                _prevRootPos            = currentPos;
                _rootBone.localPosition = Vector3.zero;

                Vector3 move = transform.TransformDirection(deltaLocal) * _rootMotionScale;
                WarpRootMotion(ref move);
                move.y = _verticalVelocity * Time.deltaTime;
                _cc.Move(move);
                LastRootDelta = deltaLocal.magnitude * _rootMotionScale;
            }
            else
            {
                LastRootDelta = 0f;
            }
        }

        // 루트모션 수평 이동을 타겟 방향으로 재조준하고 StopDistance 앞에서 멈춘다.
        // 크기는 원본 delta를 유지 → 애니메이션이 만든 속도감 보존, 방향만 휘어짐.
        private void WarpRootMotion(ref Vector3 move)
        {
            if (_warpTarget == null || !WarpWindowActive) return;

            Vector3 to = _warpTarget.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.001f) return;

            Vector3 dir    = to / dist;
            float   remain = Mathf.Max(0f, dist - _warpStopDistance);
            float   step   = Mathf.Min(new Vector3(move.x, 0f, move.z).magnitude, remain);
            move.x = dir.x * step;
            move.z = dir.z * step;
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
