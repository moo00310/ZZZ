using System;
using UnityEngine;

namespace ZZZ.Effects
{
    [Serializable]
    public abstract class EffectModule
    {
        internal abstract int Order { get; }
        public int EvaluationOrder => Order;
        internal abstract EffectModuleRuntime CreateRuntime();

        public virtual void EvaluatePreview(
            Transform effect, Transform characterRoot, float localTime)
        {
        }
    }

    internal abstract class EffectModuleRuntime
    {
        internal abstract int Order { get; }

        internal virtual void Start(EffectModuleContext context) { }
        internal virtual void Tick(EffectModuleContext context, float deltaTime) { }
        internal virtual void LateTick(EffectModuleContext context) { }
        internal virtual void RequestStop(EffectModuleContext context) { }
        internal virtual void Stop(EffectModuleContext context) { }
    }

    internal sealed class EmptyEffectModuleRuntime : EffectModuleRuntime
    {
        internal static readonly EmptyEffectModuleRuntime Instance =
            new EmptyEffectModuleRuntime();

        internal override int Order => 0;
    }

    internal sealed class EffectModuleContext
    {
        public Transform Effect;
        public Transform CharacterRoot;
        public ParticleSystem[] ParticleSystems;
        public bool MotionCompleted;
        public bool HasOutward;
        public Vector3 OutwardWorld;
    }
}
