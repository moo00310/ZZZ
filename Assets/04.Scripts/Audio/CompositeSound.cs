using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Audio
{
    [CreateAssetMenu(
        menuName = "ZZZ/Audio/Composite Sound",
        fileName = "Snd_")]
    public sealed class CompositeSound : ScriptableObject
    {
        [SerializeField]
        private List<SoundLayer> _layers = new List<SoundLayer>();

        public IReadOnlyList<SoundLayer> Layers => _layers;
    }
}