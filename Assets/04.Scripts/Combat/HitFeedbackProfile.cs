using System;
using System.Collections.Generic;
using UnityEngine;
using ZZZ.Audio;
using ZZZ.Effects;

namespace ZZZ.Combat
{
    public readonly struct HitFeedbackSelection
    {
        public CompositeEffect Effect { get; }
        public CompositeSound Sound { get; }

        public HitFeedbackSelection(
            CompositeEffect effect, CompositeSound sound)
        {
            Effect = effect;
            Sound = sound;
        }

        public bool HasAny => Effect != null || Sound != null;
    }

    [CreateAssetMenu(
        menuName = "ZZZ/Combat/Hit Feedback Profile",
        fileName = "HitFeedbackProfile_")]
    public sealed class HitFeedbackProfile : ScriptableObject
    {
        [Serializable]
        private sealed class Entry
        {
            [SerializeField] private HitResult _result = HitResult.Accepted;
            [SerializeField] private AttackStrength _strength;
            [SerializeField] private CompositeEffect _effect;
            [SerializeField] private CompositeSound _sound;

            public HitResult Result => _result;
            public AttackStrength Strength => _strength;
            public CompositeEffect Effect => _effect;
            public CompositeSound Sound => _sound;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public IEnumerable<CompositeEffect> Effects
        {
            get
            {
                if (_entries == null) yield break;
                for (int i = 0; i < _entries.Length; i++)
                {
                    Entry entry = _entries[i];
                    if (entry != null && entry.Effect != null)
                        yield return entry.Effect;
                }
            }
        }

        public bool TryGet(
            HitResult result, AttackStrength strength,
            out HitFeedbackSelection feedback)
        {
            feedback = default;
            if (_entries == null) return false;

            for (int i = 0; i < _entries.Length; i++)
            {
                Entry entry = _entries[i];
                if (entry == null || entry.Result != result
                    || entry.Strength != strength)
                    continue;

                feedback = new HitFeedbackSelection(
                    entry.Effect, entry.Sound);
                return feedback.HasAny;
            }
            return false;
        }
    }
}
