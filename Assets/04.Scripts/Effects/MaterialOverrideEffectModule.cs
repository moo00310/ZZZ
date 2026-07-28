using System;
using UnityEngine;

namespace ZZZ.Effects
{
    [Serializable]
    public sealed class MaterialOverrideEffectModule : EffectModule
    {
        [SerializeField] private Material _material;

        public Material Material { get => _material; set => _material = value; }

        internal override int Order => 30;
        internal override EffectModuleRuntime CreateRuntime() =>
            EmptyEffectModuleRuntime.Instance;
    }
}
