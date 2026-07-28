using System;
using UnityEngine;

namespace ZZZ.Effects
{
    [Serializable]
    public sealed class ArcMotionEffectModule : EffectModule
    {
        [SerializeField] private Vector3 _centerOffset = Vector3.zero;
        [SerializeField] private float _radius = 2f;
        [Range(0f, 360f)]
        [SerializeField] private float _startAngle = 0f;
        [Range(0f, 360f)]
        [SerializeField] private float _arcAngle = 180f;
        [SerializeField] private bool _clockwise = true;
        [Min(0.001f)]
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private AnimationCurve _curve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        internal override int Order => 100;
        internal override EffectModuleRuntime CreateRuntime() => new Runtime(this);

        public override void EvaluatePreview(
            Transform effect, Transform characterRoot, float localTime)
        {
            if (effect == null || characterRoot == null) return;
            float progress = Mathf.Clamp01(localTime / Mathf.Max(_duration, 0.001f));
            Apply(effect, characterRoot, Mathf.Clamp01(_curve.Evaluate(progress)), out _);
        }

        private void Apply(
            Transform effect, Transform characterRoot, float progress, out Vector3 worldOutward)
        {
            float direction = _clockwise ? 1f : -1f;
            float angle = _startAngle + _arcAngle * progress * direction;
            Vector3 localOutward = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
            worldOutward = characterRoot.TransformDirection(localOutward);
            effect.position = characterRoot.TransformPoint(
                _centerOffset + localOutward * Mathf.Max(0f, _radius));
        }

        private sealed class Runtime : EffectModuleRuntime
        {
            private readonly ArcMotionEffectModule _config;
            private float _elapsed;

            public Runtime(ArcMotionEffectModule config)
            {
                _config = config;
            }

            internal override int Order => _config.Order;

            internal override void Start(EffectModuleContext context)
            {
                _elapsed = 0f;
                context.MotionCompleted = false;
                context.Effect.SetParent(context.CharacterRoot, true);
                Apply(context, 0f);
            }

            internal override void Tick(EffectModuleContext context, float deltaTime)
            {
                if (context.MotionCompleted) return;

                _elapsed += deltaTime;
                float progress = Mathf.Clamp01(
                    _elapsed / Mathf.Max(_config._duration, 0.001f));
                Apply(context, Mathf.Clamp01(_config._curve.Evaluate(progress)));
                if (progress >= 1f) context.MotionCompleted = true;
            }

            private void Apply(EffectModuleContext context, float progress)
            {
                _config.Apply(
                    context.Effect, context.CharacterRoot, progress, out Vector3 outward);
                context.HasOutward = true;
                context.OutwardWorld = outward;
            }
        }
    }
}
