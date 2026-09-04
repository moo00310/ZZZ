using UnityEngine;
using ZZZ.Audio;
using ZZZ.Combat;

namespace ZZZ.Agent
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentActionController))]
    public sealed class AgentVoiceController : MonoBehaviour
    {
        [Header("Combat Voice")]
        [SerializeField] private CompositeSound _parryWarningVoice;
        [SerializeField, Range(0f, 1f)] private float _parryWarningPlayChance = 0.35f;
        [SerializeField, Min(0f)] private float _parryWarningMinimumInterval = 2f;

        private AgentActionController _actionController;
        private float _lastParryWarningPlayedAt = float.NegativeInfinity;

        private void Awake()
        {
            _actionController = GetComponent<AgentActionController>();
        }

        private void OnEnable()
        {
            if (_actionController == null)
                _actionController = GetComponent<AgentActionController>();

            _actionController.ParryWarningReceived += OnParryWarningReceived;
        }

        private void OnDisable()
        {
            if (_actionController == null) return;

            _actionController.ParryWarningReceived -= OnParryWarningReceived;
        }

        private void OnParryWarningReceived(HitContext context)
        {
            if (_parryWarningVoice == null) return;

            float now = Time.unscaledTime;
            if (now - _lastParryWarningPlayedAt
                < _parryWarningMinimumInterval) return;
            if (_parryWarningPlayChance <= 0f) return;
            if (_parryWarningPlayChance < 1f
                && Random.value > _parryWarningPlayChance) return;

            _lastParryWarningPlayedAt = now;

            AudioService.PlayAfterAnimation(
                _parryWarningVoice,
                SoundPlayContext.ForTransform(transform));
        }
    }
}
