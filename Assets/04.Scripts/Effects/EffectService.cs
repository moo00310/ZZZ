using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Effects
{
    // 런타임 진입점 — AnimConfig(Notify)가 아는 유일한 이펙트 API.
    // Notify ─(CompositeEffect 참조)─▶ EffectService ─▶ 프리팹별 EffectPool ─▶ 실제 이펙트.
    // 풀링은 프리팹 단위, 실행(Play)은 조합(CompositeEffect) 단위 —
    // 같은 프리팹을 서로 다른 조합에서 다른 시차/배치로 재사용할 수 있다(풀은 공유).
    public static class EffectService
    {
        private static Dictionary<GameObject, EffectPool> s_pools;
        private static Transform            s_poolRoot;
        private static EffectServiceRunner  s_runner;

        // 도메인 리로드 없는 Enter Play Mode 설정에서도 이전 플레이의 정적 상태가 새지 않도록 리셋.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            s_pools    = new Dictionary<GameObject, EffectPool>();
            s_poolRoot = null;
            s_runner   = null;
        }

        // 조합 안의 각 Entry를 자기 StartDelay만큼 지연시켜 재생한다. 지연 중 spawner가 파괴되면 스킵.
        public static void Play(CompositeEffect composite, Transform spawner)
        {
            if (composite == null || spawner == null) return;

            foreach (CompositeEffectEntry entry in composite.Entries)
            {
                if (entry == null || entry.Prefab == null) continue;

                if (entry.StartDelay <= 0f)
                {
                    PlayEntry(entry, spawner);
                }
                else
                {
                    var e = entry;   // 클로저 캡처
                    GetRunner().Delay(entry.StartDelay, () =>
                    {
                        if (spawner == null) return;   // 지연 중 스포너가 파괴된 경우
                        PlayEntry(e, spawner);
                    });
                }
            }
        }

        private static GameObject PlayEntry(CompositeEffectEntry entry, Transform spawner)
        {
            EffectPool pool     = GetOrCreatePool(entry.Prefab, entry.PrewarmCount, entry.MaxSize);
            GameObject instance = pool.Get();

            Transform anchor = FindSocket(spawner, entry.Socket);
            PlaceInstance(instance, entry, anchor);

            var handle = instance.GetComponent<PooledEffectHandle>();
            if (handle == null) handle = instance.AddComponent<PooledEffectHandle>();
            handle.Bind(pool, entry);

            instance.SetActive(true);
            RestartParticles(instance);
            return instance;
        }

        // 풀 개요 모니터(에디터 툴)용 읽기 전용 스냅샷. Play 모드에서만 의미 있음.
        public static IEnumerable<EffectPool> DebugPools =>
            s_pools != null ? s_pools.Values : System.Linq.Enumerable.Empty<EffectPool>();

        private static EffectPool GetOrCreatePool(GameObject prefab, int prewarm, int maxSize)
        {
            if (s_pools == null) ResetState();
            if (!s_pools.TryGetValue(prefab, out EffectPool pool))
            {
                pool = new EffectPool(prefab, prewarm, maxSize, GetPoolRoot());
                s_pools[prefab] = pool;
            }
            return pool;
        }

        private static Transform GetPoolRoot()
        {
            if (s_poolRoot == null)
            {
                var go = new GameObject("EffectPool");
                Object.DontDestroyOnLoad(go);
                s_poolRoot = go.transform;
            }
            return s_poolRoot;
        }

        private static EffectServiceRunner GetRunner()
        {
            if (s_runner == null) s_runner = GetPoolRoot().gameObject.AddComponent<EffectServiceRunner>();
            return s_runner;
        }

        private static Transform FindSocket(Transform spawner, string socket)
        {
            if (string.IsNullOrEmpty(socket)) return spawner;
            Transform found = FindRecursive(spawner, socket);
            return found != null ? found : spawner;
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindRecursive(root.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }

        private static void PlaceInstance(GameObject instance, CompositeEffectEntry entry, Transform anchor)
        {
            Transform t = instance.transform;
            if (entry.FollowSpawner)
            {
                t.SetParent(anchor, false);
                t.localPosition    = entry.PositionOffset;
                t.localEulerAngles = entry.EulerOffset;
            }
            else
            {
                t.SetParent(null, true);
                t.position = anchor.TransformPoint(entry.PositionOffset);
                t.rotation = anchor.rotation * Quaternion.Euler(entry.EulerOffset);
            }
            t.localScale = entry.Scale;
        }

        // 풀 재사용 인스턴스는 비활성화되며 정지된 상태 — 루트 파티클(있으면)만 재생해 자식은
        // 내부 Start Delay(에디트타임에 구운 시차)로 순차 재생되게 한다.
        private static void RestartParticles(GameObject instance)
        {
            var root = instance.GetComponent<ParticleSystem>();
            if (root != null) { root.Play(true); return; }

            var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++) systems[i].Play(false);
        }
    }
}
