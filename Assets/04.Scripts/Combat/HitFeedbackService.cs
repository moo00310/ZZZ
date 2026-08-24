using UnityEngine;
using ZZZ.Audio;
using ZZZ.Effects;

namespace ZZZ.Combat
{
    public static class HitFeedbackService
    {
        public static void Play(
            in HitContext context, Transform targetRoot, HitResult result)
        {
            HitData hit = context.Definition;
            if (hit == null || targetRoot == null
                || result == HitResult.Ignored)
                return;

            HitFeedbackReceiver receiver =
                targetRoot.GetComponentInParent<HitFeedbackReceiver>();
            if (receiver == null
                || !receiver.TryGet(
                    result, hit.Strength, out HitFeedbackSelection feedback))
                return;

            Vector3 fallbackDirection = targetRoot.forward;
            if (fallbackDirection.sqrMagnitude <= 0.0001f
                && context.Source != null)
                fallbackDirection = context.Source.forward;
            Vector3 direction = context.Direction.sqrMagnitude > 0.0001f
                ? context.Direction.normalized
                : fallbackDirection;
            Quaternion rotation = Quaternion.LookRotation(-direction);

            if (feedback.Effect != null)
                EffectService.PlayAt(
                    feedback.Effect, context.HitPoint, rotation, targetRoot);
            if (feedback.Sound != null)
                AudioService.PlayAt(
                    feedback.Sound, context.HitPoint, rotation, targetRoot);
        }
    }
}
