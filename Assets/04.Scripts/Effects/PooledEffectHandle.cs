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
        private WeaponTrailController[] _trailControllers;

        // 셰이더 노브 오버라이드(MPB)용 캐시 — 프리팹의 선언(EffectParameterSet)과 대상 렌더러.
        // 인스턴스 구조는 재사용 내내 안 바뀌므로 최초 1회만 수집한다.
        private EffectParameterSet     _paramSet;
        private Renderer[]             _renderers;
        private MaterialPropertyBlock  _mpb;
        private bool                   _paramCacheInit;

        // 파티클 모듈 노브(수명/Size커브/색)용 캐시 — 단일 PS와 그 프리팹 기본값 스냅샷.
        // baseline은 최초 1회(모듈을 아직 안 덮은 상태)에 캡처해야 프리팹 기본값이 남는다.
        private ParticleSystem   _overrideParticle;
        private ParticleBaseline _particleBaseline;
        private bool             _particleCacheInit;

        // 머티리얼 스왑 노브용 캐시 — 단일 렌더러와 프리팹 기본 머티리얼(스왑 전 최초 1회 캡처).
        private Renderer _overrideRenderer;
        private Material _baseMaterial;
        private bool     _materialCacheInit;

        public void Bind(EffectPool pool, CompositeEffectEntry entry)
        {
            _pool = pool;
            _mode = entry.Despawn;

            CancelInvoke();   // 이전 재생의 ReleaseSelf/StopEmitting 예약 취소

            ApplyPlaybackSpeed(EffectModuleSettings.PlaybackSpeed(entry));
            ApplyMaterialOverride(entry);
            ApplyParamOverrides(entry);
            ApplyParticleOverrides(entry);
            float duration = EffectModuleSettings.Duration(entry);
            if (duration > 0f)
                Invoke(nameof(StopEmitting), duration);

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

        // 프리팹이 선언한 셰이더 노브를 Entry 오버라이드로 MPB에 얹는다(에디터 프리뷰와 공용 로직).
        // 인스턴스 구조(선언/렌더러)는 재사용 내내 안 바뀌므로 최초 1회만 수집해 캐시한다.
        private void ApplyParamOverrides(CompositeEffectEntry entry)
        {
            if (!_paramCacheInit)
            {
                _paramSet       = GetComponent<EffectParameterSet>();
                _renderers      = GetComponentsInChildren<Renderer>(true);
                _mpb            = new MaterialPropertyBlock();
                _paramCacheInit = true;
            }
            if (_paramSet == null) return;
            EffectParamApplier.Apply(entry, _paramSet, _renderers, _mpb);
        }

        // 파티클 모듈 노브를 단일 PS에 적용(셰이더 노브와 대칭). 프리팹 기본값(baseline)은 최초 1회 캡처 —
        // 그 시점엔 아직 오버라이드를 안 덮었으므로 프리팹 원본이 남는다. 이후 매 Bind에서
        // 오버라이드 있으면 그 값, 없으면 baseline으로 되돌려 풀 재사용 누수를 막는다.
        private void ApplyParticleOverrides(CompositeEffectEntry entry)
        {
            if (!_particleCacheInit)
            {
                _overrideParticle  = GetComponentInChildren<ParticleSystem>(true);
                _particleBaseline  = ParticleBaseline.Capture(_overrideParticle);
                _particleCacheInit = true;
            }
            ParticleParamApplier.Apply(entry, _overrideParticle, _particleBaseline);
        }

        // 룩 통째 교체 — 단일 렌더러의 sharedMaterial을 조합의 MaterialOverride로(null이면 프리팹 기본).
        // baseline(기본 머티리얼)은 스왑 전 최초 1회 캡처해 풀 재사용 시 복원한다.
        private void ApplyMaterialOverride(CompositeEffectEntry entry)
        {
            if (!_materialCacheInit)
            {
                _overrideRenderer  = GetComponentInChildren<Renderer>(true);
                _baseMaterial      = _overrideRenderer != null ? _overrideRenderer.sharedMaterial : null;
                _materialCacheInit = true;
            }
            EffectMaterialApplier.Apply(
                EffectModuleSettings.MaterialOverride(entry), _overrideRenderer, _baseMaterial);
        }

        // 구간 이펙트가 끝났을 때(또는 섹션 이탈·캔슬) 외부(EffectHandle)에서 부르는 정지 진입점.
        // 방출만 멈추고 살아있는 파티클은 자연 소멸시킨다 — ParticleStopped 모드면 소멸 후 자동 반납된다.
        // Fixed 모드는 자연 정지 콜백이 없으므로 Lifetime 타이머를 기다리지 않고 방출 정지 후 즉시 반납한다.
        public void StopWindowed()
        {
            if (_mode == DespawnMode.Fixed)
            {
                StopEmitting();
                ReleaseSelf();
                return;
            }
            StopEmitting();
        }

        // Entry.Duration 경과 — 방출만 멈춘다. 살아있는 파티클은 자연 소멸하고,
        // ParticleStopped 모드면 전부 소멸한 뒤 Stop 콜백으로 이어서 자동 반납된다.
        private void StopEmitting()
        {
            if (_topLevelSystems == null) _topLevelSystems = CollectTopLevelSystems();
            foreach (var ps in _topLevelSystems)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            if (_trailControllers == null)
                _trailControllers = GetComponentsInChildren<WeaponTrailController>(true);

            foreach (WeaponTrailController trailController in _trailControllers)
                if (trailController != null) trailController.StopEmission();
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
