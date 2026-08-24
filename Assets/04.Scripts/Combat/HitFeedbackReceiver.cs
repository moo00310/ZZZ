using UnityEngine;
using ZZZ.Effects;

namespace ZZZ.Combat
{
    [DisallowMultipleComponent]
    public sealed class HitFeedbackReceiver : MonoBehaviour
    {
        [SerializeField] private HitFeedbackProfile _profile;

        private void OnEnable()
        {
            if (_profile != null)
                EffectOwnership.Register(this, _profile.Effects);
        }

        private void OnDisable()
        {
            if (_profile != null)
                EffectOwnership.Unregister(this, _profile.Effects);
        }

        public bool TryGet(
            HitResult result, AttackStrength strength,
            out HitFeedbackSelection feedback)
        {
            if (_profile != null)
                return _profile.TryGet(result, strength, out feedback);

            feedback = default;
            return false;
        }
    }
}
