using System;
using UnityEngine;

namespace ZZZ.Effects
{
    [Serializable]
    public sealed class ParticlePlaybackEffectModule : EffectModule
    {
        [SerializeField, Min(0f)] private float _duration;
        [SerializeField, Min(0.01f)] private float _playbackSpeed = 1f;
        [SerializeField, Min(0f)] private float _startLifetime;

        public float Duration
        {
            get => _duration;
            set => _duration = Mathf.Max(0f, value);
        }

        public float PlaybackSpeed
        {
            get => _playbackSpeed > 0f ? _playbackSpeed : 1f;
            set => _playbackSpeed = Mathf.Max(0.01f, value);
        }

        public float StartLifetime
        {
            get => _startLifetime;
            set => _startLifetime = Mathf.Max(0f, value);
        }

        internal override int Order => 10;
        internal override EffectModuleRuntime CreateRuntime() =>
            EmptyEffectModuleRuntime.Instance;
    }
}
