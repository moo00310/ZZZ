using System;
using UnityEngine;

namespace ZZZ.Effects
{
    [Serializable]
    public sealed class ParticleAppearanceEffectModule : EffectModule
    {
        [SerializeField] private bool _overrideSizeCurve;
        [SerializeField] private float _sizeMultiplier = 1f;
        [SerializeField] private AnimationCurve _sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));
        [SerializeField] private bool _overrideStartColor;
        [SerializeField, ColorUsage(true, true)] private Color _startColor = Color.white;

        public bool OverrideSizeCurve { get => _overrideSizeCurve; set => _overrideSizeCurve = value; }
        public float SizeMultiplier { get => _sizeMultiplier; set => _sizeMultiplier = value; }
        public AnimationCurve SizeCurve { get => _sizeCurve; set => _sizeCurve = value; }
        public bool OverrideStartColor { get => _overrideStartColor; set => _overrideStartColor = value; }
        public Color StartColor { get => _startColor; set => _startColor = value; }

        internal override int Order => 20;
        internal override EffectModuleRuntime CreateRuntime() =>
            EmptyEffectModuleRuntime.Instance;
    }
}
