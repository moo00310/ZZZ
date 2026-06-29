using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Combat
{
    // 공격 이펙트 프리팹에 붙여, 스폰 순간 자기 범위 안의 HitTarget에 데미지를 준다 (AoE).
    // 타격 범위 = 이펙트 비주얼 범위: SphereCollider를 VFX 크기에 맞춰 붙이면 그 반경을 그대로 쓰고,
    // 없으면 _radius 폴백. 데미지는 이펙트 프리팹별로 설정한다.
    //
    // 흐름: 공격 클립의 Effect Notify가 이 프리팹을 Instantiate(ConfigState.DispatchNotify) →
    // Start에서 1회 타격. 별도 OnAttackHit 노티파이/플레이어측 MeleeHitter 없이 이펙트가 타격을 담당.
    public class EffectHitVolume : MonoBehaviour
    {
        [SerializeField] private float     _damage     = 10f;
        [SerializeField] private float     _radius     = 1.5f;   // SphereCollider 없을 때 사용
        [SerializeField] private LayerMask _mask       = ~0;     // 타격 대상 레이어 (공격자 제외하도록 설정)
        [SerializeField] private bool      _hitOnSpawn = true;   // 스폰 즉시 1회 타격

        private static readonly Collider[] s_hits = new Collider[16];
        private readonly List<HitTarget> _struck = new List<HitTarget>();

        private void Start()
        {
            if (_hitOnSpawn) Detonate();
        }

        // 현재 위치 기준 구 범위로 1회 타격. 타이밍을 직접 제어하고 싶으면 외부에서 호출해도 된다.
        public void Detonate()
        {
            GetVolume(out Vector3 center, out float radius);
            int count = Physics.OverlapSphereNonAlloc(
                center, radius, s_hits, _mask, QueryTriggerInteraction.Collide);

            _struck.Clear();
            for (int i = 0; i < count; i++)
            {
                var target = s_hits[i].GetComponentInParent<HitTarget>();
                if (target == null || target.CurrentHp <= 0f) continue;
                if (_struck.Contains(target)) continue;          // 한 적이 콜라이더 여럿이어도 1회만

                _struck.Add(target);
                // hitPoint = 이펙트(=공격자 위치에 스폰) → 피격 측이 앞/뒤 판정에 사용
                target.TakeDamage(_damage, transform.position);
            }
        }

        // SphereCollider가 있으면 그 월드 중심/반경, 없으면 transform 위치 + _radius.
        private void GetVolume(out Vector3 center, out float radius)
        {
            var sphere = GetComponent<SphereCollider>();
            if (sphere != null)
            {
                center = sphere.transform.TransformPoint(sphere.center);
                Vector3 s = sphere.transform.lossyScale;
                radius = sphere.radius * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            }
            else
            {
                center = transform.position;
                radius = _radius;
            }
        }

        private void OnDrawGizmosSelected()
        {
            GetVolume(out Vector3 center, out float radius);
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.7f);
            Gizmos.DrawWireSphere(center, radius);
        }
    }
}
