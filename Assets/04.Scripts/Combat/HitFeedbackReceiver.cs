using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ZZZ.Effects;
using ZZZ.ResourceManagement;

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

        private void OnDestroy()
        {
            if (_profile != null)
                EffectOwnership.Unregister(this, _profile.Effects);
        }

        public bool TryGet(
            HitResult result, AttackStrength strength,
            out HitFeedbackSelection feedback)
        {
            if (_profile != null && EffectOwnership.IsReady(this))
                return _profile.TryGet(result, strength, out feedback);

            feedback = default;
            return false;
        }

        public Task<bool> PrepareResourcesAsync(CancellationToken cancellationToken)
        {
            EffectOwnership.Register(this, _profile != null ? _profile.Effects : null);
            return ResourceTask.WaitAsync(EffectOwnership.WaitUntilReady(this), cancellationToken);
        }
    }
}
