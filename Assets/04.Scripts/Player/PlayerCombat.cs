using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using ZZZ.Combat;

namespace ZZZ.Player
{
    [RequireComponent(typeof(Animator))]
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Attack")]
        [SerializeField] private float _attackDamage = 10f;
        [SerializeField] private float _attackRange  = 1.5f;
        [SerializeField] private LayerMask _hitLayer;

        [Header("Combo")]
        [SerializeField] private int   _maxCombo       = 3;
        [SerializeField] private float _comboResetTime = 1.2f;

        private Animator  _animator;
        private int       _comboIndex;
        private bool      _attackQueued;
        private Coroutine _comboResetRoutine;

        private static readonly int AnimHashAttack1 = Animator.StringToHash("Attack1");
        private static readonly int AnimHashAttack2 = Animator.StringToHash("Attack2");
        private static readonly int AnimHashAttack3 = Animator.StringToHash("Attack3");

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        // Called by PlayerInput component (SendMessages)
        private void OnAttack(InputValue value)
        {
            if (!value.isPressed) return;

            if (_comboIndex >= _maxCombo)
            {
                _attackQueued = true;
                return;
            }

            ExecuteAttack();
        }

        private void ExecuteAttack()
        {
            if (_comboResetRoutine != null)
                StopCoroutine(_comboResetRoutine);

            _comboIndex++;
            TriggerAttackAnim(_comboIndex);
            DealDamage();

            _comboResetRoutine = StartCoroutine(ComboResetAfterDelay());
        }

        private void TriggerAttackAnim(int index)
        {
            int hash = index switch
            {
                1 => AnimHashAttack1,
                2 => AnimHashAttack2,
                _ => AnimHashAttack3,
            };
            _animator.SetTrigger(hash);
        }

        private void DealDamage()
        {
            Vector3 origin = transform.position + Vector3.up * 1f;
            Collider[] hits = Physics.OverlapSphere(origin, _attackRange, _hitLayer);
            foreach (Collider hit in hits)
            {
                if (hit.TryGetComponent<IHittable>(out var target))
                    target.TakeDamage(_attackDamage, hit.ClosestPoint(origin));
            }
        }

        // Called from Animation Event at the end of each attack clip
        public void OnAttackAnimEnd()
        {
            if (_attackQueued && _comboIndex < _maxCombo)
            {
                _attackQueued = false;
                ExecuteAttack();
            }
        }

        private IEnumerator ComboResetAfterDelay()
        {
            yield return new WaitForSeconds(_comboResetTime);
            _comboIndex   = 0;
            _attackQueued = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up, _attackRange);
        }
    }
}
