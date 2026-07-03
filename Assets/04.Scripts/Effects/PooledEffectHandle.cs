using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Effects
{
    // 스폰된 이펙트 인스턴스에 붙어 자기 자신의 재생 제어(속도/방출 지속)와 반납을 담당한다 —
    // EffectService.Play가 매 재생마다 Bind(entry). 인스턴스가 캐시(파티클 목록/원본 속도)를 들고
    // 재사용되므로, Entry마다 달라지는 값은 전부 여기서 매 재생 적용한다.
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

        // 재생 속도 배율용 캐시 — 프리팹에 구운 simulationSpeed(원본)를 보존하고 배율만 곱한다.
        // 풀 인스턴스는 Entry 간 공유되므로, 이전 재생의 배율이 남지 않게 매 Bind에서 갱신한다.
        private ParticleSystem[] _allSystems;
        private float[]          _baseSimSpeeds;
        private float            _appliedSpeed = 1f;

        public void Bind(EffectPool pool, CompositeEffectEntry entry)
        {
            _pool = pool;
            _mode = entry.Despawn;

            CancelInvoke();   // 이전 재생의 ReleaseSelf/StopEmitting 예약 취소

            ApplyPlaybackSpeed(entry.PlaybackSpeed > 0f ? entry.PlaybackSpeed : 1f);
            if (entry.Duration > 0f)
                Invoke(nameof(StopEmitting), entry.Duration);

            if (_mode == DespawnMode.Fixed)
            {
                Invoke(nameof(ReleaseSelf), entry.Lifetime);
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

        // Entry.PlaybackSpeed를 모든 파티클의 simulationSpeed에 "원본 × 배율"로 적용.
        private void ApplyPlaybackSpeed(float speed)
        {
            if (Mathf.Approximately(speed, _appliedSpeed)) return;

            if (_allSystems == null)
            {
                _allSystems    = GetComponentsInChildren<ParticleSystem>(true);
                _baseSimSpeeds = new float[_allSystems.Length];
                for (int i = 0; i < _allSystems.Length; i++)
                    _baseSimSpeeds[i] = _allSystems[i].main.simulationSpeed;
            }
            for (int i = 0; i < _allSystems.Length; i++)
            {
                var main = _allSystems[i].main;
                main.simulationSpeed = _baseSimSpeeds[i] * speed;
            }
            _appliedSpeed = speed;
        }

        // Entry.Duration 경과 — 방출만 멈춘다. 살아있는 파티클은 자연 소멸하고,
        // ParticleStopped 모드면 전부 소멸한 뒤 Stop 콜백으로 이어서 자동 반납된다.
        private void StopEmitting()
        {
            if (_topLevelSystems == null) _topLevelSystems = CollectTopLevelSystems();
            foreach (var ps in _topLevelSystems)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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
        // Fixed 모드는 릴레이가 붙어 있어도(Duration용 수집 경유) Lifetime 타이머만 따른다.
        public void NotifyChildStopped()
        {
            if (_mode != DespawnMode.ParticleStopped) return;
            if (--_pendingStops <= 0) ReleaseSelf();
        }

        private void ReleaseSelf()
        {
            CancelInvoke();
            _pool.Release(gameObject);
        }
    }
}
