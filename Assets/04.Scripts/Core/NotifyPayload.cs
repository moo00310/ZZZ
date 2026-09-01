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
        Shot
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

    public interface ICameraFeedbackReceiver
    {
        void PlayCameraShake(CameraShakeRequest request);
        void PlayCameraShot(CameraShotRequest request);
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
