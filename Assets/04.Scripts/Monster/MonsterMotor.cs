using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using ZZZ;

namespace ZZZ.Monster
{
    [RequireComponent(typeof(Animator))]
    [MovedFrom(true, "ZZZ.Monster", "Assembly-CSharp", "MonsterController")]
    public class MonsterMotor : MonoBehaviour, ICharacterMover
    {
        [Header("Rig")]
        [SerializeField] private Transform _bip001Bone;

        private Animator _animator;
        private Transform _target;
        private Transform _facingTarget;
        private float _faceTurnSpeed;
        private bool _faceEnabled;

        public Vector3 ViewForward => transform.forward;
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
        public float LocalTimeScale { get; set; } = 1f;

        public Vector3 MoveDirection => Vector3.zero;
        public MoveDir CurrentMoveDir => MoveDir.Neutral;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_bip001Bone == null)
                _bip001Bone = FindBone("Bip001");

            _animator.applyRootMotion = true;
        }

        private void OnAnimatorMove()
        {
            if (_animator == null || UseCodeMovement) return;

            Vector3 rootDelta = _animator.deltaPosition;
            rootDelta.y = 0f;
            if (!Mathf.Approximately(BackMotionScale, 1f))
            {
                Vector3 localDelta = transform.InverseTransformDirection(rootDelta);
                if (localDelta.z < 0f)
                    localDelta.z *= BackMotionScale;
                rootDelta = transform.TransformDirection(localDelta);
            }
            transform.position += rootDelta;

            if (!AllowRotation || KillRootRotation || ExtractRootRotation) return;

            if (_faceEnabled && _facingTarget != null && FaceWindowActive)
                RotateTowardTarget();
            else
                transform.rotation = _animator.deltaRotation * transform.rotation;
        }

        private void LateUpdate()
        {
            SuppressBip001HorizontalMotion();
        }

        public void FaceToward(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(worldDir);
        }

        public void MoveBy(Vector3 worldDelta)
        {
            worldDelta.y = 0f;
            transform.position += worldDelta;
        }

        public Transform FindTarget()
        {
            return _target;
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            if (_faceEnabled)
                _facingTarget = target;
        }

        public void FlushRootRotation() { }
        public void ClearWarpTarget()
        {
            _facingTarget = null;
            _faceEnabled = false;
            WarpWindowActive = false;
            FaceWindowActive = false;
        }

        public void SetWarpTranslationTarget(Transform target, float stopDistance) { }

        public void SetFacingTarget(Transform target, float faceTurnSpeed)
        {
            _facingTarget = target;
            _faceEnabled = target != null;
            _faceTurnSpeed = faceTurnSpeed;
            FaceWindowActive = false;
        }

        public void AddStartBoost(float speed, float duration) { }

        private void RotateTowardTarget()
        {
            Vector3 toTarget = _facingTarget.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(toTarget);
            transform.rotation = _faceTurnSpeed > 0f
                ? Quaternion.RotateTowards(transform.rotation, targetRotation,
                    _faceTurnSpeed * Time.deltaTime * LocalTimeScale)
                : targetRotation;
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
    }
}
