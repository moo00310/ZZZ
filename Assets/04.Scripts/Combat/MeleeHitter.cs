using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Combat
{
    // 플레이어 공격의 적중 처리 — 공격 클립의 Custom Notify(EventName="OnAttackHit")가
    // 타격 프레임에 ConfigState.DispatchNotify → 플레이어 루트로 SendMessage("OnAttackHit").
    // 그 순간 전방 부채꼴(EnemySensor) 안의 모든 HitTarget에 데미지를 준다 (AoE).
    //
    // 다단 히트가 필요한 공격은 Notify를 여러 개(타격 프레임마다) 찍으면 된다.
    // 데미지를 공격별로 바꾸려면 TrackNotify에 데미지 필드를 추가하거나
    // EventName을 분기(OnAttackHitHeavy 등)해 확장한다 — 우선은 컴포넌트 고정 데미지.
    [RequireComponent(typeof(EnemySensor))]
    public class MeleeHitter : MonoBehaviour
    {
        [SerializeField] private float _damage = 10f;

        private EnemySensor _sensor;
        private readonly List<HitTarget> _buffer = new List<HitTarget>();

        private void Awake() => _sensor = GetComponent<EnemySensor>();

        // Notify EventName과 정확히 일치해야 한다(SendMessage 대상).
        // hitPoint = 공격자(플레이어) 위치 → 피격 측(MonsterStateMachine.OnDamaged)이
        // 공격자가 앞/뒤 어디인지로 Hit_Front/Hit_Back을 분기한다.
        public void OnAttackHit()
        {
            _sensor.FindTargets(_buffer);
            Vector3 hitPoint = transform.position;
            for (int i = 0; i < _buffer.Count; i++)
                _buffer[i].TakeDamage(_damage, hitPoint);
        }
    }
}
