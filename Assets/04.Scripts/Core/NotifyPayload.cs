using System;
using System.Collections.Generic;
using UnityEngine;

using ZZZ.Audio;
using ZZZ.Effects;

namespace ZZZ
{
    public enum HitNotifyAction
    {
        Damage,
        ParryWarning
    }

    public enum ConfigEventType
    {
        None,
        HitShake
    }

    public enum CameraNotifyMode
    {
        Shake,
        Shot,
        Path
    }

    public readonly struct CameraShakeRequest
    {
        public float Duration { get; }
        public float PositionAmplitude { get; }
        public float RotationAmplitude { get; }
        public float Frequency { get; }
        public AnimationCurve Envelope { get; }

        public CameraShakeRequest(float duration, float positionAmplitude,
            float rotationAmplitude, float frequency,
            AnimationCurve envelope = null)
        {
            Duration = Mathf.Max(0f, duration);
            PositionAmplitude = Mathf.Max(0f, positionAmplitude);
            RotationAmplitude = Mathf.Max(0f, rotationAmplitude);
            Frequency = Mathf.Max(0f, frequency);
            Envelope = envelope;
        }
    }

    public readonly struct CameraShotRequest
    {
        public Transform Anchor { get; }
        public Vector3 StartLocalPosition { get; }
        public Quaternion StartLocalRotation { get; }
        public float StartFieldOfView { get; }
        public Vector3 EndLocalPosition { get; }
        public Quaternion EndLocalRotation { get; }
        public float EndFieldOfView { get; }
        public float BlendIn { get; }
        public float MoveDuration { get; }
        public float Hold { get; }
        public float BlendOut { get; }
        public bool ReturnBehindTarget { get; }
        public AnimationCurve BlendCurve { get; }
        public AnimationCurve MoveCurve { get; }

        public CameraShotRequest(Transform anchor, Vector3 startLocalPosition,
            Quaternion startLocalRotation, float startFieldOfView,
            Vector3 endLocalPosition, Quaternion endLocalRotation,
            float endFieldOfView, float blendIn, float moveDuration,
            float hold, float blendOut, bool returnBehindTarget,
            AnimationCurve blendCurve, AnimationCurve moveCurve)
        {
            Anchor = anchor;
            StartLocalPosition = startLocalPosition;
            StartLocalRotation = startLocalRotation;
            StartFieldOfView = Mathf.Clamp(startFieldOfView, 1f, 179f);
            EndLocalPosition = endLocalPosition;
            EndLocalRotation = endLocalRotation;
            EndFieldOfView = Mathf.Clamp(endFieldOfView, 1f, 179f);
            BlendIn = Mathf.Max(0f, blendIn);
            MoveDuration = Mathf.Max(0f, moveDuration);
            Hold = Mathf.Max(0f, hold);
            BlendOut = Mathf.Max(0f, blendOut);
            ReturnBehindTarget = returnBehindTarget;
            BlendCurve = blendCurve;
            MoveCurve = moveCurve;
        }
    }

    public readonly struct CameraPathRequest
    {
        public Transform Anchor { get; }
        public Vector3[] LocalPoints { get; }
        public float[] PointTimes { get; }
        public float[] LookAtHeights { get; }
        public float StartFieldOfView { get; }
        public float EndFieldOfView { get; }
        public float BlendIn { get; }
        public float MoveDuration { get; }
        public float Hold { get; }
        public float BlendOut { get; }
        public bool ReturnBehindTarget { get; }
        public AnimationCurve BlendCurve { get; }
        public AnimationCurve MoveCurve { get; }

        public CameraPathRequest(Transform anchor, Vector3[] localPoints,
            float[] pointTimes, float[] lookAtHeights,
            float startFieldOfView, float endFieldOfView, float blendIn,
            float moveDuration, float hold, float blendOut,
            bool returnBehindTarget, AnimationCurve blendCurve,
            AnimationCurve moveCurve)
        {
            Anchor = anchor;
            LocalPoints = localPoints ?? Array.Empty<Vector3>();
            PointTimes = pointTimes ?? Array.Empty<float>();
            LookAtHeights = lookAtHeights ?? Array.Empty<float>();
            StartFieldOfView = Mathf.Clamp(startFieldOfView, 1f, 179f);
            EndFieldOfView = Mathf.Clamp(endFieldOfView, 1f, 179f);
            BlendIn = Mathf.Max(0f, blendIn);
            MoveDuration = Mathf.Max(0f, moveDuration);
            Hold = Mathf.Max(0f, hold);
            BlendOut = Mathf.Max(0f, blendOut);
            ReturnBehindTarget = returnBehindTarget;
            BlendCurve = blendCurve;
            MoveCurve = moveCurve;
        }
    }

    public interface ICameraFeedbackReceiver
    {
        void PlayCameraShake(CameraShakeRequest request);
        void PlayCameraShot(CameraShotRequest request);
        void PlayCameraPath(CameraPathRequest request);
    }

    public static class CameraFeedbackService
    {
        private static ICameraFeedbackReceiver s_receiver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_receiver = null;
        }

        public static void Register(ICameraFeedbackReceiver receiver)
        {
            s_receiver = receiver;
        }

        public static void Unregister(ICameraFeedbackReceiver receiver)
        {
            if (object.ReferenceEquals(s_receiver, receiver)) s_receiver = null;
        }

        public static void PlayShake(CameraShakeRequest request)
        {
            if (s_receiver is UnityEngine.Object receiverObject
                && receiverObject == null)
            {
                s_receiver = null;
                return;
            }

            s_receiver?.PlayCameraShake(request);
        }

        public static void PlayShot(CameraShotRequest request)
        {
            if (s_receiver is UnityEngine.Object receiverObject
                && receiverObject == null)
            {
                s_receiver = null;
                return;
            }

            s_receiver?.PlayCameraShot(request);
        }

        public static void PlayPath(CameraPathRequest request)
        {
            if (s_receiver is UnityEngine.Object receiverObject
                && receiverObject == null)
            {
                s_receiver = null;
                return;
            }

            s_receiver?.PlayCameraPath(request);
        }
    }

    [Serializable]
    public abstract class NotifyPayload
    {
        public abstract NotifyType Type { get; }
    }

    [Serializable]
    public sealed class EffectNotifyPayload : NotifyPayload
    {
        [SerializeField] private CompositeEffect _effect;
        [SerializeReference] private HitData _hit;
        [SerializeField] private EffectTransitionMode _transitionMode =
            EffectTransitionMode.Keep;
        [SerializeField] private string _nextSection = "";

        public override NotifyType Type => NotifyType.Effect;
        public CompositeEffect Effect { get => _effect; set => _effect = value; }
        public HitData Hit { get => _hit; set => _hit = value; }
        public EffectTransitionMode TransitionMode
        {
            get => _transitionMode;
            set => _transitionMode = value;
        }
        public string NextSection
        {
            get => _nextSection;
            set => _nextSection = value ?? "";
        }

        public EffectNotifyPayload()
        {
        }

        public EffectNotifyPayload(CompositeEffect effect, HitData hit,
            EffectTransitionMode transitionMode, string nextSection)
        {
            _effect = effect;
            _hit = hit;
            _transitionMode = transitionMode;
            _nextSection = nextSection ?? "";
        }
    }

    [Serializable]
    public sealed class HitNotifyPayload : NotifyPayload
    {
        [SerializeField] private HitData _hit = new HitData();
        [SerializeField] private HitNotifyAction _action;
        [SerializeField, Min(0f)] private float _warningDuration = 0.3f;
        [SerializeField] private bool _syncWithEffect;

        public override NotifyType Type => NotifyType.Hit;
        public HitData Hit
        {
            get => _hit ??= new HitData();
            set => _hit = value ?? new HitData();
        }
        public bool SyncWithEffect
        {
            get => _syncWithEffect;
            set => _syncWithEffect = value;
        }
        public HitNotifyAction Action
        {
            get => _action;
            set => _action = value;
        }
        public float WarningDuration
        {
            get => _warningDuration;
            set => _warningDuration = Mathf.Max(0f, value);
        }

        public HitNotifyPayload()
        {
        }

        public HitNotifyPayload(HitData hit, bool syncWithEffect = false)
        {
            _hit = hit ?? new HitData();
            _syncWithEffect = syncWithEffect;
        }
    }

    [Serializable]
    public sealed class CameraNotifyPayload : NotifyPayload
    {
        [SerializeField] private CameraNotifyMode _mode;

        [Header("Shake")]
        [SerializeField, Min(0f)] private float _duration = 0.12f;
        [SerializeField, Min(0f)] private float _positionAmplitude = 0.04f;
        [SerializeField, Min(0f)] private float _rotationAmplitude = 0.6f;
        [SerializeField, Min(0f)] private float _frequency = 30f;
        [SerializeField] private AnimationCurve _envelope = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.35f, 0.45f),
            new Keyframe(1f, 0f));

        [Header("Shot")]
        [SerializeField] private Vector3 _shotPosition =
            new Vector3(0f, 1.5f, -3.5f);
        [SerializeField] private Vector3 _shotEulerAngles;
        [SerializeField, Range(1f, 179f)] private float _shotFieldOfView = 60f;
        [SerializeField] private Vector3 _shotEndPosition =
            new Vector3(0f, 1.5f, -3.5f);
        [SerializeField] private Vector3 _shotEndEulerAngles;
        [SerializeField, Range(1f, 179f)] private float _shotEndFieldOfView = 60f;
        [SerializeField, Min(0f)] private float _shotBlendIn = 0.08f;
        [SerializeField, Min(0f)] private float _shotMoveDuration = 0.2f;
        [SerializeField, Min(0f)] private float _shotHold = 0.08f;
        [SerializeField, Min(0f)] private float _shotBlendOut = 0.2f;
        [SerializeField] private bool _shotPreserveReturnHeading;
        [SerializeField] private AnimationCurve _shotBlendCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve _shotMoveCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Path")]
        [SerializeField] private List<Vector3> _pathPoints =
            new List<Vector3>
            {
                new Vector3(0.8f, 0.4f, -0.8f),
                new Vector3(1.2f, 0.8f, 0f),
                new Vector3(0.5f, 1.4f, 1.2f),
                new Vector3(-1.2f, 1.8f, 0.4f),
            };
        [SerializeField] private List<float> _pathPointTimes =
            new List<float> { 0f, 0.3333f, 0.6667f, 1f };
        [SerializeField] private List<float> _pathLookAtHeights =
            new List<float> { 0.8f, 1.0333f, 1.2667f, 1.5f };
        [SerializeField] private bool _pathPointMetadataInitialized;
        [SerializeField] private float _pathStartLookAtHeight = 0.8f;
        [SerializeField] private float _pathEndLookAtHeight = 1.5f;
        [SerializeField, Range(1f, 179f)] private float _pathStartFieldOfView = 60f;
        [SerializeField, Range(1f, 179f)] private float _pathEndFieldOfView = 60f;
        [SerializeField, Min(0f)] private float _pathBlendIn = 0.08f;
        [SerializeField, Min(0f)] private float _pathMoveDuration = 2.5f;
        [SerializeField, Min(0f)] private float _pathHold = 0.08f;
        [SerializeField, Min(0f)] private float _pathBlendOut = 0.4f;
        [SerializeField] private bool _pathPreserveReturnHeading;
        [SerializeField] private AnimationCurve _pathBlendCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve _pathMoveCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public override NotifyType Type => NotifyType.Camera;
        public CameraNotifyMode Mode
        {
            get => _mode;
            set => _mode = value;
        }
        public float Duration
        {
            get => _duration;
            set => _duration = Mathf.Max(0f, value);
        }
        public float PositionAmplitude
        {
            get => _positionAmplitude;
            set => _positionAmplitude = Mathf.Max(0f, value);
        }
        public float RotationAmplitude
        {
            get => _rotationAmplitude;
            set => _rotationAmplitude = Mathf.Max(0f, value);
        }
        public float Frequency
        {
            get => _frequency;
            set => _frequency = Mathf.Max(0f, value);
        }
        public AnimationCurve Envelope
        {
            get => _envelope;
            set => _envelope = value;
        }
        public Vector3 ShotPosition
        {
            get => _shotPosition;
            set => _shotPosition = value;
        }
        public Vector3 ShotEulerAngles
        {
            get => _shotEulerAngles;
            set => _shotEulerAngles = value;
        }
        public float ShotFieldOfView
        {
            get => _shotFieldOfView;
            set => _shotFieldOfView = Mathf.Clamp(value, 1f, 179f);
        }
        public Vector3 ShotEndPosition
        {
            get => _shotEndPosition;
            set => _shotEndPosition = value;
        }
        public Vector3 ShotEndEulerAngles
        {
            get => _shotEndEulerAngles;
            set => _shotEndEulerAngles = value;
        }
        public float ShotEndFieldOfView
        {
            get => _shotEndFieldOfView;
            set => _shotEndFieldOfView = Mathf.Clamp(value, 1f, 179f);
        }
        public float ShotBlendIn
        {
            get => _shotBlendIn;
            set => _shotBlendIn = Mathf.Max(0f, value);
        }
        public float ShotMoveDuration
        {
            get => _shotMoveDuration;
            set => _shotMoveDuration = Mathf.Max(0f, value);
        }
        public float ShotHold
        {
            get => _shotHold;
            set => _shotHold = Mathf.Max(0f, value);
        }
        public float ShotBlendOut
        {
            get => _shotBlendOut;
            set => _shotBlendOut = Mathf.Max(0f, value);
        }
        public bool ShotReturnBehindTarget
        {
            get => !_shotPreserveReturnHeading;
            set => _shotPreserveReturnHeading = !value;
        }
        public AnimationCurve ShotBlendCurve
        {
            get => _shotBlendCurve;
            set => _shotBlendCurve = value;
        }
        public AnimationCurve ShotMoveCurve
        {
            get => _shotMoveCurve;
            set => _shotMoveCurve = value;
        }
        private List<Vector3> PathPointList =>
            _pathPoints ??= new List<Vector3>();

        public IReadOnlyList<Vector3> PathPoints => PathPointList;
        public float GetPathPointTime(int index)
        {
            if (index < 0 || index >= PathPointList.Count) return 0f;
            if (_pathPointMetadataInitialized
                && _pathPointTimes != null
                && _pathPointTimes.Count == PathPointList.Count)
                return Mathf.Clamp01(_pathPointTimes[index]);

            return PathPointList.Count > 1
                ? (float)index / (PathPointList.Count - 1)
                : 0f;
        }

        public float GetPathPointLookAtHeight(int index)
        {
            if (index < 0 || index >= PathPointList.Count) return 0f;
            if (_pathPointMetadataInitialized
                && _pathLookAtHeights != null
                && _pathLookAtHeights.Count == PathPointList.Count)
                return _pathLookAtHeights[index];

            float normalizedTime = PathPointList.Count > 1
                ? (float)index / (PathPointList.Count - 1)
                : 0f;
            return Mathf.Lerp(
                _pathStartLookAtHeight,
                _pathEndLookAtHeight,
                normalizedTime);
        }
        public float PathStartLookAtHeight
        {
            get => _pathStartLookAtHeight;
            set => _pathStartLookAtHeight = value;
        }
        public float PathEndLookAtHeight
        {
            get => _pathEndLookAtHeight;
            set => _pathEndLookAtHeight = value;
        }
        public float PathStartFieldOfView
        {
            get => _pathStartFieldOfView;
            set => _pathStartFieldOfView = Mathf.Clamp(value, 1f, 179f);
        }
        public float PathEndFieldOfView
        {
            get => _pathEndFieldOfView;
            set => _pathEndFieldOfView = Mathf.Clamp(value, 1f, 179f);
        }
        public float PathBlendIn
        {
            get => _pathBlendIn;
            set => _pathBlendIn = Mathf.Max(0f, value);
        }
        public float PathMoveDuration
        {
            get => _pathMoveDuration;
            set => _pathMoveDuration = Mathf.Max(0f, value);
        }
        public float PathHold
        {
            get => _pathHold;
            set => _pathHold = Mathf.Max(0f, value);
        }
        public float PathBlendOut
        {
            get => _pathBlendOut;
            set => _pathBlendOut = Mathf.Max(0f, value);
        }
        public bool PathReturnBehindTarget
        {
            get => !_pathPreserveReturnHeading;
            set => _pathPreserveReturnHeading = !value;
        }
        public AnimationCurve PathBlendCurve
        {
            get => _pathBlendCurve;
            set => _pathBlendCurve = value;
        }
        public AnimationCurve PathMoveCurve
        {
            get => _pathMoveCurve;
            set => _pathMoveCurve = value;
        }

        public void SetPathPoints(IEnumerable<Vector3> points)
        {
            PathPointList.Clear();
            if (points != null) PathPointList.AddRange(points);
            ResetPathPointMetadata();
        }

        public void SetPathPointData(IReadOnlyList<Vector3> points,
            IReadOnlyList<float> pointTimes,
            IReadOnlyList<float> lookAtHeights)
        {
            PathPointList.Clear();
            _pathPointTimes ??= new List<float>();
            _pathLookAtHeights ??= new List<float>();
            _pathPointTimes.Clear();
            _pathLookAtHeights.Clear();

            int pointCount = points?.Count ?? 0;
            for (int i = 0; i < pointCount; i++)
            {
                float fallbackTime = pointCount > 1
                    ? (float)i / (pointCount - 1)
                    : 0f;
                PathPointList.Add(points[i]);
                _pathPointTimes.Add(pointTimes != null
                        && i < pointTimes.Count
                    ? Mathf.Clamp01(pointTimes[i])
                    : fallbackTime);
                _pathLookAtHeights.Add(lookAtHeights != null
                        && i < lookAtHeights.Count
                    ? lookAtHeights[i]
                    : Mathf.Lerp(
                        _pathStartLookAtHeight,
                        _pathEndLookAtHeight,
                        fallbackTime));
            }

            _pathPointMetadataInitialized = true;
            SortPathPointData();
        }

        public void SetPathPoint(int index, Vector3 point)
        {
            if (index < 0 || index >= PathPointList.Count) return;
            PathPointList[index] = point;
        }

        public int SetPathPointTime(int index, float normalizedTime)
        {
            EnsurePathPointMetadata();
            if (index < 0 || index >= PathPointList.Count) return -1;

            float time = Mathf.Clamp01(normalizedTime);
            Vector3 point = PathPointList[index];
            float lookAtHeight = _pathLookAtHeights[index];
            PathPointList.RemoveAt(index);
            _pathPointTimes.RemoveAt(index);
            _pathLookAtHeights.RemoveAt(index);

            int insertIndex = 0;
            while (insertIndex < _pathPointTimes.Count
                && _pathPointTimes[insertIndex] <= time)
                insertIndex++;

            PathPointList.Insert(insertIndex, point);
            _pathPointTimes.Insert(insertIndex, time);
            _pathLookAtHeights.Insert(insertIndex, lookAtHeight);
            return insertIndex;
        }

        public void SetPathPointLookAtHeight(int index, float height)
        {
            EnsurePathPointMetadata();
            if (index < 0 || index >= PathPointList.Count) return;
            _pathLookAtHeights[index] = height;
        }

        public void AddPathPoint(Vector3 point)
        {
            EnsurePathPointMetadata();
            int previousCount = PathPointList.Count;
            if (previousCount >= 2
                && Mathf.Approximately(
                    _pathPointTimes[previousCount - 1], 1f))
            {
                float previousTime = _pathPointTimes[previousCount - 2];
                _pathPointTimes[previousCount - 1] =
                    Mathf.Lerp(previousTime, 1f, 0.5f);
            }

            float lookAtHeight = previousCount > 0
                ? _pathLookAtHeights[previousCount - 1]
                : _pathStartLookAtHeight;
            PathPointList.Add(point);
            _pathPointTimes.Add(previousCount == 0 ? 0f : 1f);
            _pathLookAtHeights.Add(lookAtHeight);
        }

        public void RemovePathPointAt(int index)
        {
            EnsurePathPointMetadata();
            if (index < 0 || index >= PathPointList.Count) return;
            PathPointList.RemoveAt(index);
            _pathPointTimes.RemoveAt(index);
            _pathLookAtHeights.RemoveAt(index);
        }

        private void EnsurePathPointMetadata()
        {
            if (_pathPointMetadataInitialized
                && _pathPointTimes != null
                && _pathPointTimes.Count == PathPointList.Count
                && _pathLookAtHeights != null
                && _pathLookAtHeights.Count == PathPointList.Count)
                return;

            ResetPathPointMetadata();
        }

        private void ResetPathPointMetadata()
        {
            _pathPointTimes ??= new List<float>();
            _pathLookAtHeights ??= new List<float>();
            _pathPointTimes.Clear();
            _pathLookAtHeights.Clear();
            _pathPointMetadataInitialized = true;

            int pointCount = PathPointList.Count;
            for (int i = 0; i < pointCount; i++)
            {
                float normalizedTime = pointCount > 1
                    ? (float)i / (pointCount - 1)
                    : 0f;
                _pathPointTimes.Add(normalizedTime);
                _pathLookAtHeights.Add(Mathf.Lerp(
                    _pathStartLookAtHeight,
                    _pathEndLookAtHeight,
                    normalizedTime));
            }
        }

        private void SortPathPointData()
        {
            for (int i = 1; i < _pathPointTimes.Count; i++)
            {
                int index = i;
                while (index > 0
                    && _pathPointTimes[index] < _pathPointTimes[index - 1])
                {
                    (_pathPointTimes[index - 1], _pathPointTimes[index]) =
                        (_pathPointTimes[index], _pathPointTimes[index - 1]);
                    (PathPointList[index - 1], PathPointList[index]) =
                        (PathPointList[index], PathPointList[index - 1]);
                    (_pathLookAtHeights[index - 1],
                        _pathLookAtHeights[index]) =
                        (_pathLookAtHeights[index],
                            _pathLookAtHeights[index - 1]);
                    index--;
                }
            }
        }

        public CameraShakeRequest CreateShakeRequest()
        {
            return new CameraShakeRequest(
                _duration, _positionAmplitude, _rotationAmplitude, _frequency,
                _envelope);
        }

        public CameraShotRequest CreateShotRequest(Transform anchor)
        {
            return new CameraShotRequest(
                anchor, _shotPosition, Quaternion.Euler(_shotEulerAngles),
                _shotFieldOfView, _shotEndPosition,
                Quaternion.Euler(_shotEndEulerAngles), _shotEndFieldOfView,
                _shotBlendIn, _shotMoveDuration, _shotHold, _shotBlendOut,
                !_shotPreserveReturnHeading, _shotBlendCurve, _shotMoveCurve);
        }

        public CameraPathRequest CreatePathRequest(Transform anchor)
        {
            EnsurePathPointMetadata();
            return new CameraPathRequest(
                anchor, PathPointList.ToArray(), _pathPointTimes.ToArray(),
                _pathLookAtHeights.ToArray(), _pathStartFieldOfView,
                _pathEndFieldOfView, _pathBlendIn, _pathMoveDuration,
                _pathHold, _pathBlendOut, !_pathPreserveReturnHeading,
                _pathBlendCurve, _pathMoveCurve);
        }
    }

    [Serializable]
    public abstract class SoundNotifyModule
    {
    }

    [Serializable]
    public sealed class SoundFadeModule : SoundNotifyModule
    {
        [SerializeField, Min(0f)] private float _fadeInDuration = 0.1f;
        [SerializeField, Min(0f)] private float _fadeOutDuration = 0.15f;

        public float FadeInDuration
        {
            get => _fadeInDuration;
            set => _fadeInDuration = Mathf.Max(0f, value);
        }

        public float FadeOutDuration
        {
            get => _fadeOutDuration;
            set => _fadeOutDuration = Mathf.Max(0f, value);
        }

        public SoundFadeModule()
        {
        }

        public SoundFadeModule(float fadeInDuration, float fadeOutDuration)
        {
            FadeInDuration = fadeInDuration;
            FadeOutDuration = fadeOutDuration;
        }
    }

    [Serializable]
    public sealed class SoundDurationModule : SoundNotifyModule
    {
        [SerializeField, Min(0f)] private float _duration = 1f;

        public float Duration
        {
            get => _duration;
            set => _duration = Mathf.Max(0f, value);
        }

        public SoundDurationModule()
        {
        }

        public SoundDurationModule(float duration)
        {
            Duration = duration;
        }
    }

    [Serializable]
    public sealed class SoundNotifyPayload : NotifyPayload
    {
        [SerializeField] private CompositeSound _sound;
        [SerializeField] private bool _loop;
        [SerializeField] private string _nextSection = "";
        [SerializeReference] private List<SoundNotifyModule> _modules =
            new List<SoundNotifyModule>();

        public override NotifyType Type => NotifyType.Sound;
        public CompositeSound Sound
        {
            get => _sound;
            set => _sound = value;
        }
        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }
        public string NextSection
        {
            get => _nextSection;
            set => _nextSection = value ?? "";
        }
        public List<SoundNotifyModule> Modules
        {
            get
            {
                if (_modules == null)
                    _modules = new List<SoundNotifyModule>();
                return _modules;
            }
        }

        public T FindModule<T>() where T : SoundNotifyModule
        {
            List<SoundNotifyModule> modules = Modules;
            for (int i = 0; i < modules.Count; i++)
                if (modules[i] is T module)
                    return module;
            return null;
        }
    }

    [Serializable]
    public sealed class CustomNotifyPayload : NotifyPayload
    {
        [SerializeField] private ConfigEventType _eventType;

        public override NotifyType Type => NotifyType.Custom;
        public ConfigEventType EventType
        {
            get => _eventType;
            set => _eventType = value;
        }

        public CustomNotifyPayload()
        {
        }

        public CustomNotifyPayload(ConfigEventType eventType)
        {
            _eventType = eventType;
        }
    }
}
