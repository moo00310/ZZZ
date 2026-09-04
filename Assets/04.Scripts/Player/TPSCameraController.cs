using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZZZ.Player
{
    [RequireComponent(typeof(Camera))]
    public class TPSCameraController : MonoBehaviour, ICameraFeedbackReceiver
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
        private Camera _camera;
        private float _defaultFieldOfView;
        private float _shakeElapsed;
        private float _shakeDuration;
        private float _shakePositionAmplitude;
        private float _shakeRotationAmplitude;
        private float _shakeFrequency;
        private float _shakeNoiseSeed;
        private AnimationCurve _activeShakeEnvelope;
        private bool _shakeActive;
        private Transform _shotAnchor;
        private Vector3 _shotStartLocalPosition;
        private Quaternion _shotStartLocalRotation;
        private float _shotStartFieldOfView;
        private Vector3 _shotEndLocalPosition;
        private Quaternion _shotEndLocalRotation;
        private float _shotEndFieldOfView;
        private float _shotBlendIn;
        private float _shotMoveDuration;
        private float _shotHold;
        private float _shotBlendOut;
        private bool _shotReturnBehindTarget;
        private bool _shotReturnHeadingAligned;
        private float _shotElapsed;
        private AnimationCurve _shotBlendCurve;
        private AnimationCurve _shotMoveCurve;
        private bool _shotActive;
        private Transform _pathAnchor;
        private Quaternion _pathAnchorRotation;
        private Vector3[] _pathLocalPoints = Array.Empty<Vector3>();
        private float[] _pathPointTimes = Array.Empty<float>();
        private float[] _pathLookAtHeights = Array.Empty<float>();
        private float _pathStartFieldOfView;
        private float _pathEndFieldOfView;
        private float _pathBlendIn;
        private float _pathMoveDuration;
        private float _pathHold;
        private float _pathBlendOut;
        private bool _pathReturnBehindTarget;
        private bool _pathReturnHeadingAligned;
        private float _pathElapsed;
        private AnimationCurve _pathBlendCurve;
        private AnimationCurve _pathMoveCurve;
        private bool _pathActive;
        private bool _lookInputEnabled = true;

        public bool LookInputEnabled => _lookInputEnabled;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _defaultFieldOfView = _camera.fieldOfView;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            if (_target != null)
                _currentFollowPos = _target.position;
        }

        private void OnEnable()
        {
            CameraFeedbackService.Register(this);
        }

        private void OnDisable()
        {
            CameraFeedbackService.Unregister(this);
            _shakeActive = false;
            _shotActive = false;
            _pathActive = false;
            if (_camera != null) _camera.fieldOfView = _defaultFieldOfView;
        }

        public void SetTarget(Transform target, bool snap = false)
        {
            _target = target;
            if (snap && _target != null) _currentFollowPos = _target.position;
        }

        public void SetLookInputEnabled(bool enabled)
        {
            _lookInputEnabled = enabled;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            ReadLookInput();
            UpdatePosition();
        }

        private void ReadLookInput()
        {
            if (!_lookInputEnabled) return;

            Vector2 delta = Mouse.current.delta.ReadValue();
            _yaw   += delta.x * _sensitivityX;
            _pitch -= delta.y * _sensitivityY;
            _pitch  = Mathf.Clamp(_pitch, _pitchMin, _pitchMax);
        }

        private void UpdatePosition()
        {
            _currentFollowPos = Vector3.Lerp(
                _currentFollowPos,
                _target.position,
                _followSpeed * Time.deltaTime
            );

            AlignShotReturnHeading();
            AlignPathReturnHeading();

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            EvaluateCameraShake(
                out Vector3 positionShake,
                out Vector3 rotationShake);

            Vector3 followPosition = _currentFollowPos;
            float desiredDistance = Mathf.Max(_minDistance, _distance);
            Vector3 desiredOffset = rotation * Vector3.back * desiredDistance;

            float finalDistance = desiredDistance;
            if (Physics.SphereCast(
                    followPosition,
                    _collisionRadius,
                    desiredOffset.normalized,
                    out RaycastHit hit,
                    desiredDistance,
                    _collisionMask))
            {
                finalDistance = Mathf.Clamp(
                    hit.distance - _collisionRadius,
                    _minDistance,
                    desiredDistance);
            }

            Vector3 cameraPosition = followPosition
                + rotation * Vector3.back * finalDistance;
            Quaternion cameraRotation = rotation;
            float fieldOfView = _defaultFieldOfView;
            EvaluateCameraShot(
                ref cameraPosition, ref cameraRotation, ref fieldOfView);
            EvaluateCameraPath(
                ref cameraPosition, ref cameraRotation, ref fieldOfView);

            transform.position = cameraPosition + cameraRotation * positionShake;
            transform.rotation = cameraRotation * Quaternion.Euler(rotationShake);
            _camera.fieldOfView = fieldOfView;
        }

        public void PlayCameraShake(CameraShakeRequest request)
        {
            if (request.Duration <= 0f
                || request.PositionAmplitude <= 0f
                    && request.RotationAmplitude <= 0f)
                return;

            _shakeElapsed = 0f;
            _shakeDuration = request.Duration;
            _shakePositionAmplitude = request.PositionAmplitude;
            _shakeRotationAmplitude = request.RotationAmplitude;
            _shakeFrequency = request.Frequency;
            _activeShakeEnvelope = request.Envelope;
            _shakeNoiseSeed += 13.37f;
            _shakeActive = true;
        }

        public void PlayCameraShot(CameraShotRequest request)
        {
            float totalDuration = request.BlendIn + request.MoveDuration
                + request.Hold + request.BlendOut;
            if (request.Anchor == null || totalDuration <= 0f) return;

            _shotAnchor = request.Anchor;
            _shotStartLocalPosition = request.StartLocalPosition;
            _shotStartLocalRotation = request.StartLocalRotation;
            _shotStartFieldOfView = request.StartFieldOfView;
            _shotEndLocalPosition = request.EndLocalPosition;
            _shotEndLocalRotation = request.EndLocalRotation;
            _shotEndFieldOfView = request.EndFieldOfView;
            _shotBlendIn = request.BlendIn;
            _shotMoveDuration = request.MoveDuration;
            _shotHold = request.Hold;
            _shotBlendOut = request.BlendOut;
            _shotReturnBehindTarget = request.ReturnBehindTarget;
            _shotReturnHeadingAligned = false;
            _shotBlendCurve = request.BlendCurve;
            _shotMoveCurve = request.MoveCurve;
            _shotElapsed = 0f;
            _shotActive = true;
            _pathActive = false;
        }

        public void PlayCameraPath(CameraPathRequest request)
        {
            float totalDuration = request.BlendIn + request.MoveDuration
                + request.Hold + request.BlendOut;
            if (request.Anchor == null || request.LocalPoints == null
                || request.LocalPoints.Length < 2
                || totalDuration <= 0f)
                return;

            _pathAnchor = request.Anchor;
            _pathAnchorRotation = request.Anchor.rotation;
            _pathLocalPoints = request.LocalPoints;
            _pathPointTimes = request.PointTimes;
            _pathLookAtHeights = request.LookAtHeights;
            _pathStartFieldOfView = request.StartFieldOfView;
            _pathEndFieldOfView = request.EndFieldOfView;
            _pathBlendIn = request.BlendIn;
            _pathMoveDuration = request.MoveDuration;
            _pathHold = request.Hold;
            _pathBlendOut = request.BlendOut;
            _pathReturnBehindTarget = request.ReturnBehindTarget;
            _pathReturnHeadingAligned = false;
            _pathBlendCurve = request.BlendCurve;
            _pathMoveCurve = request.MoveCurve;
            _pathElapsed = 0f;
            _pathActive = true;
            _shotActive = false;
        }

        private void AlignShotReturnHeading()
        {
            if (!_shotActive || !_shotReturnBehindTarget
                || _shotReturnHeadingAligned || _target == null)
                return;

            float blendOutStart =
                _shotBlendIn + _shotMoveDuration + _shotHold;
            if (_shotElapsed + Time.unscaledDeltaTime < blendOutStart) return;

            _yaw = _target.eulerAngles.y;
            _shotReturnHeadingAligned = true;
        }

        private void AlignPathReturnHeading()
        {
            if (!_pathActive || !_pathReturnBehindTarget
                || _pathReturnHeadingAligned || _target == null)
                return;

            float blendOutStart =
                _pathBlendIn + _pathMoveDuration + _pathHold;
            if (_pathElapsed + Time.unscaledDeltaTime < blendOutStart) return;

            _yaw = _target.eulerAngles.y;
            _pathReturnHeadingAligned = true;
        }

        private void EvaluateCameraShot(ref Vector3 position,
            ref Quaternion rotation, ref float fieldOfView)
        {
            if (!_shotActive || _shotAnchor == null)
            {
                _shotActive = false;
                return;
            }

            _shotElapsed += Time.unscaledDeltaTime;
            float totalDuration = _shotBlendIn + _shotMoveDuration
                + _shotHold + _shotBlendOut;
            if (_shotElapsed >= totalDuration)
            {
                _shotActive = false;
                return;
            }

            float weight = EvaluateShotWeight(_shotElapsed);
            float moveWeight = EvaluateShotMoveWeight(_shotElapsed);
            Vector3 localPosition = Vector3.Lerp(
                _shotStartLocalPosition, _shotEndLocalPosition, moveWeight);
            Quaternion localRotation = Quaternion.Slerp(
                _shotStartLocalRotation, _shotEndLocalRotation, moveWeight);
            float shotFieldOfView = Mathf.Lerp(
                _shotStartFieldOfView, _shotEndFieldOfView, moveWeight);
            Vector3 shotPosition = _shotAnchor.TransformPoint(localPosition);
            Quaternion shotRotation = _shotAnchor.rotation * localRotation;
            position = Vector3.Lerp(position, shotPosition, weight);
            rotation = Quaternion.Slerp(rotation, shotRotation, weight);
            fieldOfView = Mathf.Lerp(
                _defaultFieldOfView, shotFieldOfView, weight);
        }

        private float EvaluateShotWeight(float elapsed)
        {
            if (_shotBlendIn > 0f && elapsed < _shotBlendIn)
                return EvaluateShotCurve(elapsed / _shotBlendIn);

            float blendOutStart =
                _shotBlendIn + _shotMoveDuration + _shotHold;
            if (elapsed <= blendOutStart) return 1f;
            if (_shotBlendOut <= 0f) return 0f;
            return 1f - EvaluateShotCurve(
                (elapsed - blendOutStart) / _shotBlendOut);
        }

        private float EvaluateShotMoveWeight(float elapsed)
        {
            if (elapsed <= _shotBlendIn) return 0f;
            if (_shotMoveDuration <= 0f) return 1f;

            float normalizedTime = Mathf.Clamp01(
                (elapsed - _shotBlendIn) / _shotMoveDuration);
            return _shotMoveCurve != null
                ? Mathf.Clamp01(_shotMoveCurve.Evaluate(normalizedTime))
                : normalizedTime;
        }

        private float EvaluateShotCurve(float normalizedTime)
        {
            float time = Mathf.Clamp01(normalizedTime);
            return _shotBlendCurve != null
                ? Mathf.Clamp01(_shotBlendCurve.Evaluate(time))
                : time;
        }

        private void EvaluateCameraPath(ref Vector3 position,
            ref Quaternion rotation, ref float fieldOfView)
        {
            if (!_pathActive || _pathAnchor == null
                || _pathLocalPoints.Length < 2)
            {
                _pathActive = false;
                return;
            }

            _pathElapsed += Time.unscaledDeltaTime;
            float totalDuration = _pathBlendIn + _pathMoveDuration
                + _pathHold + _pathBlendOut;
            if (_pathElapsed >= totalDuration)
            {
                _pathActive = false;
                return;
            }

            float weight = EvaluatePathWeight(_pathElapsed);
            float moveTime = EvaluatePathMoveWeight(_pathElapsed);
            float pathParameter = CameraPathUtility.RemapPointTime(
                _pathPointTimes, _pathLocalPoints.Length, moveTime);
            Vector3 localPosition = CameraPathUtility.Evaluate(
                _pathLocalPoints, pathParameter);
            Vector3 pathPosition = _pathAnchor.position
                + _pathAnchorRotation * localPosition;
            Vector3 up = _pathAnchorRotation * Vector3.up;
            float lookAtHeight = CameraPathUtility.EvaluateLinear(
                _pathLookAtHeights, pathParameter, 1f);
            Vector3 lookTarget = _pathAnchor.position + up * lookAtHeight;
            Vector3 lookDirection = lookTarget - pathPosition;
            Quaternion pathRotation = lookDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDirection, up)
                : rotation;
            float pathFieldOfView = Mathf.Lerp(
                _pathStartFieldOfView, _pathEndFieldOfView, moveTime);

            position = Vector3.Lerp(position, pathPosition, weight);
            rotation = Quaternion.Slerp(rotation, pathRotation, weight);
            fieldOfView = Mathf.Lerp(
                _defaultFieldOfView, pathFieldOfView, weight);
        }

        private float EvaluatePathWeight(float elapsed)
        {
            if (_pathBlendIn > 0f && elapsed < _pathBlendIn)
                return EvaluatePathCurve(elapsed / _pathBlendIn);

            float blendOutStart =
                _pathBlendIn + _pathMoveDuration + _pathHold;
            if (elapsed <= blendOutStart) return 1f;
            if (_pathBlendOut <= 0f) return 0f;
            return 1f - EvaluatePathCurve(
                (elapsed - blendOutStart) / _pathBlendOut);
        }

        private float EvaluatePathMoveWeight(float elapsed)
        {
            if (elapsed <= _pathBlendIn) return 0f;
            if (_pathMoveDuration <= 0f) return 1f;

            float normalizedTime = Mathf.Clamp01(
                (elapsed - _pathBlendIn) / _pathMoveDuration);
            return _pathMoveCurve != null
                ? Mathf.Clamp01(_pathMoveCurve.Evaluate(normalizedTime))
                : normalizedTime;
        }

        private float EvaluatePathCurve(float normalizedTime)
        {
            float time = Mathf.Clamp01(normalizedTime);
            return _pathBlendCurve != null
                ? Mathf.Clamp01(_pathBlendCurve.Evaluate(time))
                : time;
        }

        private void EvaluateCameraShake(
            out Vector3 positionShake,
            out Vector3 rotationShake)
        {
            positionShake = Vector3.zero;
            rotationShake = Vector3.zero;
            if (!_shakeActive) return;

            _shakeElapsed += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(_shakeElapsed / _shakeDuration);
            float shakeAmount = _activeShakeEnvelope != null
                ? Mathf.Clamp01(_activeShakeEnvelope.Evaluate(normalizedTime))
                : 1f - normalizedTime;
            float noiseTime = _shakeElapsed * _shakeFrequency;
            positionShake = new Vector3(
                SampleNoise(noiseTime, 0f),
                SampleNoise(noiseTime, 17f),
                SampleNoise(noiseTime, 31f) * 0.5f)
                * (_shakePositionAmplitude * shakeAmount);
            rotationShake = new Vector3(
                SampleNoise(noiseTime, 47f),
                SampleNoise(noiseTime, 61f),
                SampleNoise(noiseTime, 79f) * 0.5f)
                * (_shakeRotationAmplitude * shakeAmount);

            if (_shakeElapsed >= _shakeDuration) _shakeActive = false;
        }

        private float SampleNoise(float time, float channel)
        {
            return Mathf.PerlinNoise(time + _shakeNoiseSeed, channel + _shakeNoiseSeed)
                * 2f - 1f;
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
