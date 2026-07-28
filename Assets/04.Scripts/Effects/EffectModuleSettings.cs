namespace ZZZ.Effects
{
    public static class EffectModuleSettings
    {
        public static ParticlePlaybackEffectModule Playback(CompositeEffectEntry entry) =>
            Find<ParticlePlaybackEffectModule>(entry);

        public static ParticleAppearanceEffectModule Appearance(CompositeEffectEntry entry) =>
            Find<ParticleAppearanceEffectModule>(entry);

        public static MaterialOverrideEffectModule Material(CompositeEffectEntry entry) =>
            Find<MaterialOverrideEffectModule>(entry);

        public static float Duration(CompositeEffectEntry entry)
        {
            ParticlePlaybackEffectModule module = Playback(entry);
            return module != null ? module.Duration : entry.Duration;
        }

        public static float PlaybackSpeed(CompositeEffectEntry entry)
        {
            ParticlePlaybackEffectModule module = Playback(entry);
            if (module != null) return module.PlaybackSpeed;
            return entry.PlaybackSpeed > 0f ? entry.PlaybackSpeed : 1f;
        }

        public static float StartLifetime(CompositeEffectEntry entry)
        {
            ParticlePlaybackEffectModule module = Playback(entry);
            return module != null ? module.StartLifetime : entry.StartLifetime;
        }

        public static UnityEngine.Material MaterialOverride(CompositeEffectEntry entry)
        {
            MaterialOverrideEffectModule module = Material(entry);
            return module != null ? module.Material : entry.MaterialOverride;
        }

        private static T Find<T>(CompositeEffectEntry entry) where T : EffectModule
        {
            if (entry == null || entry.Modules == null) return null;
            for (int i = 0; i < entry.Modules.Count; i++)
                if (entry.Modules[i] is T module) return module;
            return null;
        }
    }
}
