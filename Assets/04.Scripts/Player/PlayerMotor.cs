using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace ZZZ.Player
{
    using ZZZ;

    [System.Flags]
    public enum PlayerMotorFlags
    {
        None           = 0,
        CodeMovement   = 1 << 0,
        RootMotion     = 1 << 1,
        RotationLocked = 1 << 2,
        WarpActive     = 1 << 3,
    }

    [RequireComponent(typeof(CharacterController), typeof(Animator))]
    [MovedFrom(true, "ZZZ.Player", "Assembly-CSharp", "PlayerController")]
    public class PlayerMotor : MonoBehaviour, IConfigMover
    {
        [Header("Locomotion")]
        [SerializeField] private float _rotationSpeed = 15f;
        [SerializeField] private float _rotationEaseTime = 0.2f;

        [Header("Rig")]
        [SerializeField] private Transform _bip001Bone;
        [SerializeField] private Transform _rootBone;

        [Header("Gravity")]
        [SerializeField] private float _gravity = -20f;
        [SerializeField] private float _groundedGravity = -2f;

        private CharacterController _cc;
        private Animator _animator;
        private Camera _mainCamera;
        private ZZZ.Combat.EnemySensor _enemySensor;

        private Vector2 _moveInput;
        private Vector3 _moveDirection;
        private Vector3 _pendingMovement;
        private float _verticalVelocity;
        private float _rotationEase;
        private float _sectionTurnAppliedAngle;
        private Quaternion _sectionTurnCharacterBaselineRotation = Quaternion.identity;
        private Quaternion _previousSectionTurnBip001Rotation = Quaternion.identity;
        private Quaternion _previousSectionTurnRootRotation = Quaternion.identity;
        private bool _flushSectionTurnPending;
        private bool _sectionTurnSourceResolved;
        private bool _sectionTurnUsesRootBone;
        private bool _sectionTurnWindowWasActive;

        private float _boostSpeed;
        private float _boostDuration;
        private float _boostTimeLeft;

        private Transform _warpTarget;
        private float _warpStopDistance;
        private bool _warpTranslate;
        private bool _faceEnabled;
        private float _faceTurnSpeed;

        public bool UseCodeMovement { get; set; } = true;
        public bool AllowRotation { get; set; } = true;
        public bool ExtractRootRotation { get; set; }
        public bool RootRotationWindowActive { get; set; } = true;
        public RootMotionRotationAxis RootRotationSourceAxis { get; set; }
        public float RootRotationScale { get; set; } = 1f;
        public float RootRotationTargetAngle { get; set; }
        public bool KillRootRotation { get; set; }
        public float BackMotionScale { get; set; } = 1f;
        public bool WarpWindowActive { get; set; }
        public bool FaceWindowActive { get; set; }

        public float CurrentSpeed => new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude;
        public Vector3 MoveDirection => _moveDirection;
        public ZZZ.Combat.EnemySensor EnemySensor => _enemySensor;
        public bool IsRootMotionActive => !UseCodeMovement && _animator != null;
        public float LastRootDelta { get; private set; }

        public Vector3 ViewForward
        {
            get
            {
                Vector3 forward = _mainCamera != null
                    ? _mainCamera.transform.forward
                    : transform.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 0.0001f
                    ? forward.normalized
                    : transform.forward;
            }
        }

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

        public PlayerMotorFlags CurrentFlags
        {
            get
            {
                PlayerMotorFlags flags = PlayerMotorFlags.None;
                if (UseCodeMovement) flags |= PlayerMotorFlags.CodeMovement;
                if (IsRootMotionActive) flags |= PlayerMotorFlags.RootMotion;
                if (!AllowRotation) flags |= PlayerMotorFlags.RotationLocked;
                if (WarpWindowActive) flags |= PlayerMotorFlags.WarpActive;
                return flags;
            }
        }

        private const float MOVE_THRESHOLD = 0.01f;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
            _mainCamera = Camera.main;
            _enemySensor = GetComponent<ZZZ.Combat.EnemySensor>();

            if (_bip001Bone == null)
                _bip001Bone = FindBone("Bip001");
            if (_rootBone == null)
                _rootBone = FindBone("Root");

            // OnAnimatorMove가 모든 이동을 CharacterController로 전달하도록 루트 모션 평가를 켠다.
            _animator.applyRootMotion = true;
        }

        private void OnDisable()
        {
            _pendingMovement = Vector3.zero;
            _boostTimeLeft = 0f;
        }

        private void Update()
        {
            UpdateGravity();
            UpdateMoveDirection();
            QueueStartBoost();
        }

        private void OnAnimatorMove()
        {
            if (_animator == null || _cc == null) return;

            Vector3 movement = _pendingMovement;
            _pendingMovement = Vector3.zero;

            if (!UseCodeMovement)
            {
                Vector3 rootDelta = _animator.deltaPosition;
                rootDelta.y = 0f;
                ApplyBackMotionScale(ref rootDelta);
                WarpRootMotion(ref rootDelta);
                movement += rootDelta;
                LastRootDelta = rootDelta.magnitude;
            }
            else
            {
                LastRootDelta = 0f;
            }

            movement.y += _verticalVelocity * Time.deltaTime;
            _cc.Move(movement);

            ApplyRotation(_animator.deltaRotation);
        }

        private void LateUpdate()
        {
            // Animator가 최종 본 포즈를 쓴 뒤 Bip001의 중복 수평 이동을 제거한다.
            SuppressBip001HorizontalMotion();
            ApplySectionTurnFromRootBone();
            CounterRotateSectionTurnBone();
        }

        public void SetMoveInput(Vector2 input) => _moveInput = input;

        public void FaceToward(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(worldDir);
        }

        public void MoveBy(Vector3 worldDelta)
        {
            worldDelta.y = 0f;
            _pendingMovement += worldDelta;
        }

        public void FlushRootRotation()
        {
            _sectionTurnAppliedAngle = 0f;
            _sectionTurnCharacterBaselineRotation = transform.rotation;
            _flushSectionTurnPending = true;
            _sectionTurnSourceResolved = false;
            _sectionTurnUsesRootBone = false;
            _sectionTurnWindowWasActive = false;
        }

        public void SetWarpTranslationTarget(Transform target, float stopDistance)
        {
            _warpTarget = target;
            _warpStopDistance = stopDistance;
            _warpTranslate = target != null;
            WarpWindowActive = false;
        }

        public void SetFacingTarget(Transform target, float faceTurnSpeed)
        {
            _warpTarget = target;
            _faceEnabled = target != null;
            _faceTurnSpeed = faceTurnSpeed;
            FaceWindowActive = false;
        }

        public void ClearWarpTarget()
        {
            _warpTarget = null;
            _warpTranslate = false;
            _faceEnabled = false;
            WarpWindowActive = false;
            FaceWindowActive = false;
        }

        public void AddStartBoost(float speed, float duration)
        {
            if (speed <= 0f || duration <= 0f)
            {
                _boostTimeLeft = 0f;
                return;
            }

            _boostSpeed = speed;
            _boostDuration = duration;
            _boostTimeLeft = duration;
        }

        private void UpdateMoveDirection()
        {
            _rotationEase = AllowRotation
                ? (_rotationEaseTime > 0f
                    ? Mathf.MoveTowards(_rotationEase, 1f,
                        Time.deltaTime / _rotationEaseTime)
                    : 1f)
                : 0f;

            if (_moveInput.sqrMagnitude < MOVE_THRESHOLD)
            {
                _moveDirection = Vector3.zero;
                return;
            }

            Vector3 camForward = _mainCamera != null
                ? _mainCamera.transform.forward
                : transform.forward;
            Vector3 camRight = _mainCamera != null
                ? _mainCamera.transform.right
                : transform.right;
            camForward.y = 0f;
            camRight.y = 0f;
            camForward.Normalize();
            camRight.Normalize();
            _moveDirection = (camForward * _moveInput.y + camRight * _moveInput.x).normalized;
        }

        private void QueueStartBoost()
        {
            if (_boostTimeLeft <= 0f) return;

            _boostTimeLeft -= Time.deltaTime;
            float ramp = Mathf.Clamp01(_boostTimeLeft / _boostDuration);
            Vector3 direction = _moveDirection.sqrMagnitude > MOVE_THRESHOLD
                ? _moveDirection
                : transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) direction.Normalize();

            float step = _boostSpeed * ramp * Time.deltaTime;
            if (_warpTarget != null && _warpTranslate)
            {
                Vector3 to = _warpTarget.position - transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance > 0.001f)
                {
                    direction = to / distance;
                    step = Mathf.Min(step, Mathf.Max(0f, distance - _warpStopDistance));
                }
            }

            _pendingMovement += direction * step;
        }

        private void ApplyRotation(Quaternion rootDeltaRotation)
        {
            if (UseCodeMovement)
            {
                ApplyInputRotation();
                return;
            }

            if (KillRootRotation) return;

            if (ExtractRootRotation)
            {
                return;
            }

            if (_faceEnabled && _warpTarget != null && FaceWindowActive)
            {
                RotateTowardTarget();
                return;
            }

            ApplyRootYaw(rootDeltaRotation, 1f);
            ApplyInputRotation();
        }

        private void ApplySectionTurnFromRootBone()
        {
            if (!ExtractRootRotation || KillRootRotation || _rootBone == null)
                return;

            Quaternion bip001Rotation = _bip001Bone.localRotation;
            Quaternion rootRotation = _rootBone.localRotation;
            if (_flushSectionTurnPending)
            {
                _previousSectionTurnBip001Rotation = bip001Rotation;
                _previousSectionTurnRootRotation = rootRotation;
                _flushSectionTurnPending = false;
                return;
            }
            if (!RootRotationWindowActive)
            {
                FinalizeSectionTurnAngle();
                return;
            }
            _sectionTurnWindowWasActive = true;

            Quaternion bip001FrameDelta = bip001Rotation
                * Quaternion.Inverse(_previousSectionTurnBip001Rotation);
            Quaternion rootFrameDelta = rootRotation
                * Quaternion.Inverse(_previousSectionTurnRootRotation);
            float bip001Delta = RootTurnAngleOf(
                bip001FrameDelta, RootRotationSourceAxis);
            float rootDelta = RootTurnAngleOf(
                rootFrameDelta, RootRotationSourceAxis);
            _previousSectionTurnBip001Rotation = bip001Rotation;
            _previousSectionTurnRootRotation = rootRotation;

            if (!_sectionTurnSourceResolved)
            {
                if (Mathf.Abs(rootDelta) > 0.05f)
                {
                    _sectionTurnUsesRootBone = true;
                    _sectionTurnSourceResolved = true;
                }
                else if (Mathf.Abs(bip001Delta) > 0.05f)
                {
                    _sectionTurnUsesRootBone = false;
                    _sectionTurnSourceResolved = true;
                }
            }

            if (!_sectionTurnSourceResolved) return;

            float sourceDelta = _sectionTurnUsesRootBone
                ? rootDelta
                : bip001Delta;
            _sectionTurnAppliedAngle += sourceDelta * RootRotationScale;
            if (RootRotationTargetAngle > 0f)
            {
                _sectionTurnAppliedAngle = Mathf.Clamp(_sectionTurnAppliedAngle,
                    -RootRotationTargetAngle, RootRotationTargetAngle);
            }

            transform.rotation = Quaternion.AngleAxis(
                _sectionTurnAppliedAngle, Vector3.up)
                * _sectionTurnCharacterBaselineRotation;
        }

        private void FinalizeSectionTurnAngle()
        {
            if (!_sectionTurnWindowWasActive) return;
            _sectionTurnWindowWasActive = false;
            if (RootRotationTargetAngle <= 0f
                || Mathf.Abs(_sectionTurnAppliedAngle) <= 1e-5f) return;

            _sectionTurnAppliedAngle = Mathf.Sign(_sectionTurnAppliedAngle)
                * RootRotationTargetAngle;
            transform.rotation = Quaternion.AngleAxis(
                _sectionTurnAppliedAngle, Vector3.up)
                * _sectionTurnCharacterBaselineRotation;
        }

        private void CounterRotateSectionTurnBone()
        {
            if (!ExtractRootRotation || KillRootRotation || _bip001Bone == null)
                return;
            if (Mathf.Abs(_sectionTurnAppliedAngle) <= 1e-5f) return;

            _bip001Bone.rotation = Quaternion.AngleAxis(
                -_sectionTurnAppliedAngle, Vector3.up)
                * _bip001Bone.rotation;
        }

        private void RotateTowardTarget()
        {
            Vector3 to = _warpTarget.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(to);
            transform.rotation = _faceTurnSpeed > 0f
                ? Quaternion.RotateTowards(transform.rotation, target,
                    _faceTurnSpeed * Time.deltaTime)
                : target;
        }

        private void ApplyInputRotation()
        {
            if (!AllowRotation || _moveDirection.sqrMagnitude < MOVE_THRESHOLD) return;

            Quaternion target = Quaternion.LookRotation(_moveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, target,
                _rotationSpeed * _rotationEase * Time.deltaTime);
        }

        private void ApplyBackMotionScale(ref Vector3 rootDelta)
        {
            if (Mathf.Approximately(BackMotionScale, 1f)) return;

            Vector3 localDelta = transform.InverseTransformDirection(rootDelta);
            if (localDelta.z >= 0f) return;

            localDelta.z *= BackMotionScale;
            rootDelta = transform.TransformDirection(localDelta);
        }

        private void WarpRootMotion(ref Vector3 move)
        {
            if (_warpTarget == null || !WarpWindowActive || !_warpTranslate) return;

            Vector3 to = _warpTarget.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance < 0.001f) return;

            Vector3 direction = to / distance;
            Vector3 horizontal = new Vector3(move.x, 0f, move.z);
            if (Vector3.Dot(horizontal, direction) <= 0f) return;

            float remaining = distance - _warpStopDistance;
            if (remaining <= 0f) return;

            float step = Mathf.Min(horizontal.magnitude, remaining);
            move.x = direction.x * step;
            move.z = direction.z * step;
        }

        private void ApplyRootYaw(Quaternion rootDeltaRotation, float scale)
        {
            float deltaYaw = RootTurnAngleOf(
                rootDeltaRotation, RootMotionRotationAxis.Auto) * scale;
            if (Mathf.Abs(deltaYaw) > 1e-5f)
                transform.rotation = Quaternion.AngleAxis(deltaYaw, Vector3.up)
                    * transform.rotation;
        }

        private void SuppressBip001HorizontalMotion()
        {
            if (_bip001Bone == null) return;

            Vector3 localPosition = _bip001Bone.localPosition;
            localPosition.x = 0f;
            localPosition.z = 0f;
            _bip001Bone.localPosition = localPosition;
        }

        private Transform FindBone(string boneName)
        {
            Transform[] bones = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i].name == boneName) return bones[i];
            return null;
        }

        private static float RootTurnAngleOf(Quaternion rotation,
            RootMotionRotationAxis sourceAxis)
        {
            float x = rotation.x;
            float y = rotation.y;
            float z = rotation.z;
            float w = rotation.w;
            if (w < 0f)
            {
                x = -x;
                y = -y;
                z = -z;
                w = -w;
            }

            float component;
            switch (sourceAxis)
            {
                case RootMotionRotationAxis.X:
                    component = x;
                    break;
                case RootMotionRotationAxis.Y:
                    component = y;
                    break;
                case RootMotionRotationAxis.Z:
                    component = z;
                    break;
                default:
                    float absX = Mathf.Abs(x);
                    float absY = Mathf.Abs(y);
                    float absZ = Mathf.Abs(z);
                    component = absX >= absY && absX >= absZ
                        ? x
                        : absY >= absZ ? y : z;
                    break;
            }

            if (component * component + w * w < 1e-12f) return 0f;
            return 2f * Mathf.Atan2(component, w) * Mathf.Rad2Deg;
        }

        private void UpdateGravity()
        {
            if (_cc.isGrounded)
                _verticalVelocity = _groundedGravity;
            else
                _verticalVelocity += _gravity * Time.deltaTime;
        }
    }
}
