using System;
using System.Collections;
using UnityEngine;

namespace ZZZ.Combat
{
    public class HitTarget : MonoBehaviour, IHittable
    {
        [SerializeField] private float _maxHp = 100f;
        [SerializeField] private CombatTeam _team = CombatTeam.Enemy;

        public float CurrentHp { get; private set; }
        public float MaxHp     => _maxHp;
        public CombatTeam Team => _team;
        public Transform HitTransform => transform;

        public event Action<float, Vector3> OnDamaged;
        public event Action                 OnDeath;

        private Coroutine _hitFlashRoutine;
        private Renderer  _renderer;
        private Color     _originalColor;

        private void Awake()
        {
            CurrentHp = _maxHp;
            _renderer = GetComponentInChildren<Renderer>();
            if (_renderer != null)
                _originalColor = _renderer.material.color;
        }

        public HitResult ReceiveHit(in HitContext context)
        {
            if (context.Definition == null || CurrentHp <= 0f) return HitResult.Ignored;

            Vector3 sourcePosition = context.Source != null
                ? context.Source.position
                : context.HitPoint;
            ApplyDamage(context.Definition.Damage, sourcePosition);
            return HitResult.Accepted;
        }

        public void TakeDamage(float damage, Vector3 hitPoint)
        {
            ApplyDamage(damage, hitPoint);
        }

        private void ApplyDamage(float damage, Vector3 hitPoint)
        {
            if (CurrentHp <= 0f) return;

            CurrentHp = Mathf.Max(0f, CurrentHp - damage);
            OnDamaged?.Invoke(damage, hitPoint);

            PlayHitFlash();

            if (CurrentHp <= 0f)
                Die();
        }

        private void Die()
        {
            OnDeath?.Invoke();
            // HP 리셋 (허수아비는 죽지 않고 초기화)
            StartCoroutine(RespawnAfterDelay(2f));
        }

        private IEnumerator RespawnAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            CurrentHp = _maxHp;
        }

        private void PlayHitFlash()
        {
            if (_renderer == null) return;
            if (_hitFlashRoutine != null)
                StopCoroutine(_hitFlashRoutine);
            _hitFlashRoutine = StartCoroutine(HitFlash());
        }

        private IEnumerator HitFlash()
        {
            _renderer.material.color = Color.red;
            yield return new WaitForSeconds(0.1f);
            _renderer.material.color = _originalColor;
        }
    }
}
