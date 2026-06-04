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

        public bool UseCodeMovement { get; set; } = true;

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

        private const float k_moveThreshold = 0.01f;

        private void Awake()
        {
            _cc         = GetComponent<CharacterController>();
            _animator   = GetComponentInChildren<Animator>();
            _mainCamera = Camera.main;
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
            _cc.Move(dir * (_boostSpeed * ramp * Time.deltaTime));
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
                move.y = _verticalVelocity * Time.deltaTime;
                _cc.Move(move);
                LastRootDelta = deltaLocal.magnitude * _rootMotionScale;
            }
            else
            {
                LastRootDelta = 0f;
            }
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
