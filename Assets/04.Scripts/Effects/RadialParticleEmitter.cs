using UnityEngine;
using ZZZ.Player;

namespace ZZZ.Effects
{
    public class RadialParticleEmitter : MonoBehaviour
    {
        [SerializeField] private Transform _center;
        private void OnEnable()
        {
            PlayerController player = GetComponentInParent<PlayerController>();
            _center = player == null ? null : player.transform;
        }

        [SerializeField] private Vector3 _up = Vector3.up;
        private void LateUpdate()
        {
            if (_center == null)
                return;

            Vector3 outward = transform.position - _center.position;
            outward.y = 0f;

            if (outward.sqrMagnitude < 0.0001f)
                return;

            transform.rotation =
                Quaternion.LookRotation(outward.normalized, Vector3.up);
        }

        private void OnDisable()
        {
            _center = null;
        }
    }
}
