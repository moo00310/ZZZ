using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Combat
{
    // 전방 부채꼴 범위에서 가장 가까운 적(HitTarget)을 찾는다.
    // 공격 섹션 진입 시 ConfigState가 호출 — 루트모션 워프 타겟 획득용.
    // 플레이어 루트(PlayerController와 같은 GameObject)에 부착.
    public class EnemySensor : MonoBehaviour
    {
        [SerializeField] private float     _radius = 6f;     // 탐지 반경
        [SerializeField] private float     _angle  = 120f;   // 전방 부채꼴 전체 각도
        [SerializeField] private LayerMask _mask   = ~0;     // 탐지 레이어

        private static readonly Collider[] s_hits = new Collider[16];

        // 부채꼴 안에서 가장 가까운 생존 HitTarget의 Transform (없으면 null)
        public Transform FindTarget() => FindTarget(out _);

        // 위와 동일하되 가장 가까운 적까지의 수평 거리도 함께 반환 (적 없으면 distance = float.MaxValue).
        // 거리별 진입 섹션 분기(근/중/원) 등에서 사용 — 탐색을 한 번만 돌리도록 out으로 묶었다.
        public Transform FindTarget(out float distance)
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _radius, s_hits, _mask, QueryTriggerInteraction.Collide);

            Transform best      = null;
            float     bestDist  = float.MaxValue;
            float     halfAngle = _angle * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var target = s_hits[i].GetComponentInParent<HitTarget>();
                if (target == null || target.CurrentHp <= 0f) continue;

                Vector3 to = target.transform.position - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.01f || dist >= bestDist) continue;
                if (Vector3.Angle(transform.forward, to) > halfAngle) continue;

                best     = target.transform;
                bestDist = dist;
            }
            distance = best != null ? bestDist : float.MaxValue;
            return best;
        }

        // 전방 부채꼴 안의 모든 생존 HitTarget을 results에 채운다 (AoE 타격용). 반환=개수.
        // 한 적이 콜라이더를 여러 개 가져도 중복 없이 한 번만 담는다.
        public int FindTargets(List<HitTarget> results)
        {
            results.Clear();
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _radius, s_hits, _mask, QueryTriggerInteraction.Collide);

            float halfAngle = _angle * 0.5f;
            for (int i = 0; i < count; i++)
            {
                var target = s_hits[i].GetComponentInParent<HitTarget>();
                if (target == null || target.CurrentHp <= 0f) continue;
                if (results.Contains(target)) continue;

                Vector3 to = target.transform.position - transform.position;
                to.y = 0f;
                if (to.magnitude < 0.01f) continue;                  // 자기 자신/겹친 위치 제외
                if (Vector3.Angle(transform.forward, to) > halfAngle) continue;

                results.Add(target);
            }
            return results.Count;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Vector3 left  = Quaternion.Euler(0f, -_angle * 0.5f, 0f) * transform.forward;
            Vector3 right = Quaternion.Euler(0f,  _angle * 0.5f, 0f) * transform.forward;
            Gizmos.DrawRay(transform.position, left  * _radius);
            Gizmos.DrawRay(transform.position, right * _radius);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
