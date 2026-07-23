using UnityEngine;

namespace ZZZ.Effects
{
    [RequireComponent(typeof(TrailRenderer))]
    public class WeaponTrailController : MonoBehaviour
    {
        [SerializeField] private TrailRenderer _trail;

        private void Awake()
        {
            if (_trail == null)
                _trail = GetComponent<TrailRenderer>();
        }

        private void OnEnable()
        {
            _trail.Clear();
            _trail.emitting = true;
        }

        public void StopEmission()
        {
            _trail.emitting = false;
        }

        private void OnDisable()
        {
            _trail.emitting = false;
            _trail.Clear();
        }
    }
}