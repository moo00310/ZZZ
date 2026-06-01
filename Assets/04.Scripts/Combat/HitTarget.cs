using System;
using System.Collections;
using UnityEngine;
using ZZZ.Combat;

namespace ZZZ.Combat
{
    public class HitTarget : MonoBehaviour, IHittable
    {
        [SerializeField] private float _maxHp = 100f;

        public float CurrentHp { get; private set; }
        public float MaxHp     => _maxHp;

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

        public void TakeDamage(float damage, Vector3 hitPoint)
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
