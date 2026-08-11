using UnityEngine;
using ZZZ.Effects;

namespace ZZZ.Combat
{
    public sealed class EffectHitVolume : MonoBehaviour, IEffectPlaybackListener
    {
        private HitHandle _handle;
        private HitData _definition;
        private float _elapsed;

        public void OnEffectPlay(EffectPlayContext context)
        {
            StopHit();
            if (context.Hit == null || context.CharacterRoot == null) return;

            _definition = context.Hit;
            _elapsed = 0f;
            _handle = HitService.Begin(
                _definition,
                new HitExecutionContext(context.CharacterRoot, transform));
        }

        public void OnEffectStop()
        {
            StopHit();
        }

        private void Update()
        {
            if (_handle == null || _definition == null) return;

            _elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(_elapsed / _definition.Duration);
            _handle.Tick(Time.deltaTime, progress);
        }

        private void OnDisable()
        {
            StopHit();
        }

        private void StopHit()
        {
            _handle?.Stop();
            _handle = null;
            _definition = null;
        }
    }
}
