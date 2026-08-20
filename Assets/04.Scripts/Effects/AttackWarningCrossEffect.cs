using UnityEngine;
using UnityEngine.UI;

namespace ZZZ.Effects
{
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class AttackWarningCrossEffect : MonoBehaviour, IEffectPlaybackListener
    {
        public const float DEFAULT_DURATION = 1.5f;

        [Header("References")]
        [SerializeField] private Camera _worldCamera;
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private RectTransform _rightRay;
        [SerializeField] private RectTransform _downRay;
        [SerializeField] private RectTransform _leftRay;
        [SerializeField] private RectTransform _upRay;

        [Header("Appearance")]
        [SerializeField] private Color _color = new Color(1f, 0.24f, 0.015f, 1f);
        [SerializeField, Min(1f)] private float _maximumThickness = 84f;
        [SerializeField, Min(0f)] private float _minimumThickness = 2f;
        [SerializeField, Min(0f)] private float _minimumRayLength = 8f;
        [SerializeField, Min(0f)] private float _centerOverlap = 12f;
        [SerializeField, Min(0f)] private float _edgePadding = 48f;

        [Header("Timing")]
        [SerializeField, Min(0.01f)] private float _duration = DEFAULT_DURATION;
        [SerializeField] private AnimationCurve _alphaOverLifetime = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.12f, 1f),
            new Keyframe(1f, 0f));
        [SerializeField] private AnimationCurve _thicknessOverLifetime = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.18f, 0.45f),
            new Keyframe(1f, 0f));
        [SerializeField] private AnimationCurve _lengthOverLifetime = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.3f, 1f),
            new Keyframe(1f, 1f));

        private RectTransform _canvasRect;
        private RawImage[] _rayImages;
        private float _elapsed;
        private float _currentAlpha;
        private float _lengthFactor;
        private bool _isPlaying;

        public float Duration => _duration;

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            CacheReferences();
            BeginPlayback();
        }

        private void Update()
        {
            if (!_isPlaying) return;

            _elapsed += Time.deltaTime;
            ApplyAppearance();
            if (_elapsed < _duration) return;

            _isPlaying = false;
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        }

        private void LateUpdate()
        {
            if (!_isPlaying || _canvasRect == null || _canvasGroup == null) return;

            if (_worldCamera == null) _worldCamera = Camera.main;
            if (_worldCamera == null)
            {
                _canvasGroup.alpha = 0f;
                return;
            }

            Vector3 screenPoint = _worldCamera.WorldToScreenPoint(transform.position);
            if (screenPoint.z <= 0f
                || screenPoint.x < 0f || screenPoint.x > Screen.width
                || screenPoint.y < 0f || screenPoint.y > Screen.height)
            {
                _canvasGroup.alpha = 0f;
                return;
            }

            Camera eventCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screenPoint, eventCamera, out Vector2 localPoint))
            {
                _canvasGroup.alpha = 0f;
                return;
            }

            LayoutRays(localPoint);
            _canvasGroup.alpha = _currentAlpha;
        }

        public void OnEffectPlay(EffectPlayContext context)
        {
            BeginPlayback();
        }

        public void OnEffectStop()
        {
            _isPlaying = false;
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        }

        public void SetWorldCamera(Camera worldCamera)
        {
            _worldCamera = worldCamera;
        }

        private void OnDisable()
        {
            _isPlaying = false;
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
        }

        private void OnValidate()
        {
            _maximumThickness = Mathf.Max(1f, _maximumThickness);
            _minimumThickness = Mathf.Clamp(
                _minimumThickness, 0f, _maximumThickness);
            _minimumRayLength = Mathf.Max(0f, _minimumRayLength);
            _centerOverlap = Mathf.Max(0f, _centerOverlap);
            _edgePadding = Mathf.Max(0f, _edgePadding);
            _duration = Mathf.Max(0.01f, _duration);
            _rayImages = null;
            CacheReferences();
            ApplyRayColor();
        }

        private void BeginPlayback()
        {
            if (_worldCamera == null) _worldCamera = Camera.main;
            _elapsed = 0f;
            _isPlaying = true;
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            ApplyRayColor();
            ApplyAppearance();
        }

        private void CacheReferences()
        {
            if (_canvas != null) _canvasRect = _canvas.transform as RectTransform;
            if (_rayImages != null) return;

            _rayImages = new[]
            {
                GetImage(_rightRay),
                GetImage(_downRay),
                GetImage(_leftRay),
                GetImage(_upRay),
            };
        }

        private void ApplyAppearance()
        {
            if (_canvasGroup == null) return;

            float normalizedTime = Mathf.Clamp01(_elapsed / _duration);
            _currentAlpha = Mathf.Clamp01(
                _alphaOverLifetime.Evaluate(normalizedTime));

            float thicknessFactor = Mathf.Clamp01(
                _thicknessOverLifetime.Evaluate(normalizedTime));
            float thickness = Mathf.Lerp(
                _minimumThickness, _maximumThickness, thicknessFactor);
            _lengthFactor = Mathf.Clamp01(
                _lengthOverLifetime.Evaluate(normalizedTime));
            SetThickness(_rightRay, thickness);
            SetThickness(_downRay, thickness);
            SetThickness(_leftRay, thickness);
            SetThickness(_upRay, thickness);
        }

        private void LayoutRays(Vector2 localPoint)
        {
            Rect canvasBounds = _canvasRect.rect;
            float rightLength = canvasBounds.xMax - localPoint.x + _edgePadding;
            float downLength = localPoint.y - canvasBounds.yMin + _edgePadding;
            float leftLength = localPoint.x - canvasBounds.xMin + _edgePadding;
            float upLength = canvasBounds.yMax - localPoint.y + _edgePadding;

            SetRayLayout(
                _rightRay,
                localPoint + Vector2.left * _centerOverlap,
                AnimatedLength(rightLength + _centerOverlap));
            SetRayLayout(
                _downRay,
                localPoint + Vector2.up * _centerOverlap,
                AnimatedLength(downLength + _centerOverlap));
            SetRayLayout(
                _leftRay,
                localPoint + Vector2.right * _centerOverlap,
                AnimatedLength(leftLength + _centerOverlap));
            SetRayLayout(
                _upRay,
                localPoint + Vector2.down * _centerOverlap,
                AnimatedLength(upLength + _centerOverlap));
        }

        private float AnimatedLength(float fullLength)
        {
            return Mathf.Lerp(_minimumRayLength, fullLength, _lengthFactor);
        }

        private void ApplyRayColor()
        {
            if (_rayImages == null) return;
            for (int i = 0; i < _rayImages.Length; i++)
                if (_rayImages[i] != null) _rayImages[i].color = _color;
        }

        private static RawImage GetImage(RectTransform ray)
        {
            return ray != null ? ray.GetComponent<RawImage>() : null;
        }

        private static void SetThickness(RectTransform ray, float thickness)
        {
            if (ray == null) return;
            Vector2 size = ray.sizeDelta;
            size.y = thickness;
            ray.sizeDelta = size;
        }

        private static void SetRayLayout(
            RectTransform ray, Vector2 localPoint, float length)
        {
            if (ray == null) return;
            ray.anchoredPosition = localPoint;
            Vector2 size = ray.sizeDelta;
            size.x = Mathf.Max(0f, length);
            ray.sizeDelta = size;
        }
    }
}
