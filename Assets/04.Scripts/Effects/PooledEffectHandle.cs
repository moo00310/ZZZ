using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Effects
{
    // 스폰된 이펙트 인스턴스에 붙어 자기 자신의 반납을 담당한다 — EffectService.Play가 매 재생마다 Bind.
    //
    // DespawnMode.ParticleStopped: 인스턴스 안의 "최상위" ParticleSystem(다른 ParticleSystem의 자식이
    // 아닌 것) 전부에 ParticleStopRelay를 붙여 각자의 Stop 콜백을 받는다. 전부 멈추면 반납.
    // 프리팹 루트 자체엔 파티클이 없고 여러 자식(예: Dom/Test1/Test2)에 나눠 붙은 구조도 지원한다 —
    // Unity의 OnParticleSystemStopped은 ParticleSystem이 붙은 GameObject에게만 오지 부모로 안 올라오기 때문.
    //
    // DespawnMode.Fixed: Lifetime초 뒤 강제 반납(자동정지 안 되는 이펙트용).
    public class PooledEffectHandle : MonoBehaviour
    {
        private EffectPool _pool;
        private DespawnMode _mode;
        private List<ParticleSystem> _topLevelSystems;   // 인스턴스 구조는 재사용 내내 안 바뀌므로 최초 1회만 계산
        private int _pendingStops;

        public void Bind(EffectPool pool, DespawnMode mode, float lifetime)
        {
            _pool = pool;
            _mode = mode;

            CancelInvoke(nameof(ReleaseSelf));

            if (_mode == DespawnMode.Fixed)
            {
                Invoke(nameof(ReleaseSelf), lifetime);
                return;
            }

            if (_topLevelSystems == null) _topLevelSystems = CollectTopLevelSystems();
            _pendingStops = _topLevelSystems.Count;
            if (_pendingStops == 0)
            {
                Debug.LogWarning($"{name}: ParticleStopped 반납 대상 ParticleSystem이 없습니다. 즉시 반납합니다.", this);
                ReleaseSelf();
            }
        }

        // 다른 ParticleSystem의 자식인 건 제외(중첩 서브이미터는 부모 정지에 딸려간다고 본다) — 남는 게 최상위.
        private List<ParticleSystem> CollectTopLevelSystems()
        {
            var all = GetComponentsInChildren<ParticleSystem>(true);
            var top = new List<ParticleSystem>(all.Length);
            foreach (var ps in all)
                if (!HasParticleSystemAncestor(ps.transform)) top.Add(ps);

            foreach (var ps in top)
            {
                var relay = ps.GetComponent<ParticleStopRelay>();
                if (relay == null) relay = ps.gameObject.AddComponent<ParticleStopRelay>();
                relay.Owner = this;
            }
            return top;
        }

        private bool HasParticleSystemAncestor(Transform t)
        {
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                if (p.GetComponent<ParticleSystem>() != null) return true;
                if (p == transform) break;   // 인스턴스 루트까지만 확인
            }
            return false;
        }

        // ParticleStopRelay가 최상위 시스템이 멈출 때마다 호출 — 전부 멈추면 반납.
        public void NotifyChildStopped()
        {
            if (--_pendingStops <= 0) ReleaseSelf();
        }

        private void ReleaseSelf()
        {
            CancelInvoke(nameof(ReleaseSelf));
            _pool.Release(gameObject);
        }
    }
}
