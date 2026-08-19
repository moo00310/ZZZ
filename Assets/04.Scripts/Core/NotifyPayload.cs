using System;
using UnityEngine;
using ZZZ.Effects;

namespace ZZZ
{
    public enum HitNotifyAction
    {
        Damage,
        ParryWarning
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
    public abstract class EventNotifyPayload : NotifyPayload
    {
        [SerializeField] private string _eventName = "";

        public string EventName
        {
            get => _eventName;
            set => _eventName = value ?? "";
        }

        protected EventNotifyPayload()
        {
        }

        protected EventNotifyPayload(string eventName)
        {
            _eventName = eventName ?? "";
        }
    }

    [Serializable]
    public sealed class CameraNotifyPayload : EventNotifyPayload
    {
        public override NotifyType Type => NotifyType.Camera;

        public CameraNotifyPayload()
        {
        }

        public CameraNotifyPayload(string eventName) : base(eventName)
        {
        }
    }

    [Serializable]
    public sealed class SoundNotifyPayload : EventNotifyPayload
    {
        public override NotifyType Type => NotifyType.Sound;

        public SoundNotifyPayload()
        {
        }

        public SoundNotifyPayload(string eventName) : base(eventName)
        {
        }
    }

    [Serializable]
    public sealed class CustomNotifyPayload : EventNotifyPayload
    {
        public override NotifyType Type => NotifyType.Custom;

        public CustomNotifyPayload()
        {
        }

        public CustomNotifyPayload(string eventName) : base(eventName)
        {
        }
    }
}
