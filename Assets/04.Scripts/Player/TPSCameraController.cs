using UnityEngine;
using UnityEngine.InputSystem;

namespace ZZZ.Player
{
    public class TPSCameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform _target;

        [Header("Distance")]
        [SerializeField] private float _distance        = 4f;
        [SerializeField] private float _minDistance     = 1f;

        [Header("Sensitivity")]
        [SerializeField] private float _sensitivityX    = 0.2f;
        [SerializeField] private float _sensitivityY    = 0.15f;

        [Header("Pitch Clamp")]
        [SerializeField] private float _pitchMin        = -20f;
        [SerializeField] private float _pitchMax        =  60f;

        [Header("Smoothing")]
        [SerializeField] private float _followSpeed     = 15f;

        [Header("Collision")]
        [SerializeField] private float     _collisionRadius = 0.2f;
        [SerializeField] private LayerMask _collisionMask;

        private float _yaw;
        private float _pitch = 20f;
        private Vector3 _currentFollowPos;

        private void Awake()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            if (_target != null)
                _currentFollowPos = _target.position;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            ReadLookInput();
            UpdatePosition();
        }

        private void ReadLookInput()
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            _yaw   += delta.x * _sensitivityX;
            _pitch -= delta.y * _sensitivityY;
            _pitch  = Mathf.Clamp(_pitch, _pitchMin, _pitchMax);
        }

        private void UpdatePosition()
        {
            // 타겟 위치 스무스 추적
            _currentFollowPos = Vector3.Lerp(
                _currentFollowPos,
                _target.position,
                _followSpeed * Time.deltaTime
            );

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 desiredOffset = rotation * Vector3.back * _distance;

            // 충돌 처리: 타겟에서 카메라 방향으로 SphereCast
            float finalDistance = _distance;
            if (Physics.SphereCast(
                    _currentFollowPos,
                    _collisionRadius,
                    desiredOffset.normalized,
                    out RaycastHit hit,
                    _distance,
                    _collisionMask))
            {
                finalDistance = Mathf.Clamp(hit.distance - _collisionRadius, _minDistance, _distance);
            }

            transform.position = _currentFollowPos + rotation * Vector3.back * finalDistance;
            transform.rotation = rotation;
        }

        // ESC로 커서 잠금 해제 (에디터 작업 편의용)
        private void Update()
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible   = locked;
            }
        }
    }
}
