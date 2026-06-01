using UnityEngine;
using UnityEngine.InputSystem;

namespace ZZZ.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private LocomotionConfig _config;

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

        private Vector2 _moveInput;
        private bool    _isSprinting;
        private float   _currentSpeed;
        private Vector3 _moveDirection;

        public bool UseCodeMovement { get; set; } = true;

        public float            CurrentSpeed  => _currentSpeed;
        public bool             IsSprinting   => _isSprinting;
        public Vector3          MoveDirection => _moveDirection;
        public LocomotionConfig Config        => _config;

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

                float speed = _config != null
                    ? (_isSprinting ? _config.SprintSpeed : _config.MoveSpeed)
                    : (_isSprinting ? 7f : 4f);
                _currentSpeed = speed;

                if (UseCodeMovement)
                    _cc.Move(_moveDirection * (speed * Time.deltaTime));

                float rotSpeed = _config != null ? _config.RotationSpeed : 15f;
                RotateToward(_moveDirection, rotSpeed);
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

                if (_flushRootPosPending || inTransition)
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
