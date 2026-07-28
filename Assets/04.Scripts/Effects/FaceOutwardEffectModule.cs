using System;
using UnityEngine;

namespace ZZZ.Effects
{
    [Serializable]
    public sealed class FaceOutwardEffectModule : EffectModule
    {
        [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

        internal override int Order => 200;
        internal override EffectModuleRuntime CreateRuntime() => new Runtime(this);

        public override void EvaluatePreview(
            Transform effect, Transform characterRoot, float localTime)
        {
            Apply(effect, characterRoot, Vector3.zero, false);
        }

        private void Apply(
            Transform effect, Transform characterRoot, Vector3 outwardWorld, bool hasOutward)
        {
            if (effect == null || characterRoot == null) return;
            if (!hasOutward)
            {
                outwardWorld = Vector3.ProjectOnPlane(
                    effect.position - characterRoot.position, characterRoot.up);
                if (outwardWorld.sqrMagnitude < 0.0001f) return;
            }

            effect.rotation = Quaternion.LookRotation(outwardWorld.normalized, characterRoot.up)
                * Quaternion.Euler(_rotationOffset);
        }

        private sealed class Runtime : EffectModuleRuntime
        {
            private readonly FaceOutwardEffectModule _config;

            public Runtime(FaceOutwardEffectModule config)
            {
                _config = config;
            }

            internal override int Order => _config.Order;

            internal override void Start(EffectModuleContext context) => Apply(context);
            internal override void Tick(EffectModuleContext context, float deltaTime) => Apply(context);

            private void Apply(EffectModuleContext context)
            {
                _config.Apply(
                    context.Effect, context.CharacterRoot,
                    context.OutwardWorld, context.HasOutward);
            }
        }
    }
}
