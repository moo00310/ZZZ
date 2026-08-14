using System.Collections.Generic;
using UnityEngine;
using ZZZ.Combat;

namespace ZZZ.Effects
{
    public readonly struct EffectPlayContext
    {
        public Transform Spawner       { get; }
        public Transform CharacterRoot { get; }
        public HitData Hit             { get; }
        public EffectBindingScope Bindings { get; }
        public bool DebugDraw { get; }
        public float DebugDuration { get; }

        public EffectPlayContext(
            Transform spawner, Transform characterRoot, HitData hit = null,
            EffectBindingScope bindings = null, bool debugDraw = false,
            float debugDuration = 0.1f)
        {
            Spawner       = spawner;
            CharacterRoot = characterRoot;
            Hit           = hit;
            Bindings      = bindings;
            DebugDraw     = debugDraw;
            DebugDuration = Mathf.Max(0f, debugDuration);
        }

        public static EffectPlayContext ForCharacter(
            Transform characterRoot, HitData hit = null,
            EffectBindingScope bindings = null, bool debugDraw = false,
            float debugDuration = 0.1f) =>
            new EffectPlayContext(
                characterRoot, characterRoot, hit, bindings,
                debugDraw, debugDuration);
    }

    public sealed class EffectBindingScope : IHitOriginResolver
    {
        private sealed class Binding
        {
            public int Id;
            public Transform Origin;
            public PooledEffectHandle Handle;
        }

        private readonly Dictionary<string, List<Binding>> _bindings =
            new Dictionary<string, List<Binding>>(System.StringComparer.Ordinal);
        private int _nextId;

        internal int Register(
            string key, Transform origin, PooledEffectHandle handle)
        {
            key = Normalize(key);
            if (string.IsNullOrEmpty(key) || origin == null) return 0;
            if (!_bindings.TryGetValue(key, out List<Binding> list))
            {
                list = new List<Binding>();
                _bindings.Add(key, list);
            }

            int id = ++_nextId;
            if (id == 0) id = ++_nextId;
            list.Add(new Binding { Id = id, Origin = origin, Handle = handle });
            return id;
        }

        internal bool TryAttachHit(
            string key, HitData hit, Transform source,
            bool debugDraw, float debugDuration)
        {
            key = Normalize(key);
            if (hit == null || source == null || string.IsNullOrEmpty(key)
                || !_bindings.TryGetValue(key, out List<Binding> list)) return false;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                Binding binding = list[i];
                if (binding.Origin != null
                    && binding.Origin.gameObject.activeInHierarchy
                    && binding.Handle != null)
                    return binding.Handle.AttachSynchronizedHit(
                        hit, source, debugDraw, debugDuration);

                list.RemoveAt(i);
            }
            if (list.Count == 0) _bindings.Remove(key);
            return false;
        }

        internal void Unregister(string key, int id)
        {
            key = Normalize(key);
            if (id == 0 || string.IsNullOrEmpty(key)
                || !_bindings.TryGetValue(key, out List<Binding> list)) return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Id != id) continue;
                list.RemoveAt(i);
                break;
            }
            if (list.Count == 0) _bindings.Remove(key);
        }

        public bool TryResolve(string key, out Transform origin)
        {
            key = Normalize(key);
            if (!string.IsNullOrEmpty(key)
                && _bindings.TryGetValue(key, out List<Binding> list))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Transform candidate = list[i].Origin;
                    if (candidate != null && candidate.gameObject.activeInHierarchy)
                    {
                        origin = candidate;
                        return true;
                    }
                    list.RemoveAt(i);
                }
                _bindings.Remove(key);
            }

            origin = null;
            return false;
        }

        public void Clear() => _bindings.Clear();

        private static string Normalize(string key) => key?.Trim() ?? "";
    }

    public interface IEffectPlaybackListener
    {
        void OnEffectPlay(EffectPlayContext context);
        void OnEffectStop();
    }

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
        // trackForStop=true(구간 이펙트)일 때만 정지용 EffectHandle을 할당해 반환한다 —
        // 단발(point) 재생은 핸들 없이(무할당) 스폰만 하고 null을 반환한다(전투 스폰 GC 회피).
        public static EffectHandle Play(
            CompositeEffect composite, EffectPlayContext context, bool trackForStop = false)
        {
            if (composite == null || context.Spawner == null || context.CharacterRoot == null) return null;

            var handle = trackForStop ? new EffectHandle() : null;
            PlayEntries(composite, context, handle, false);
            return handle;
        }

        // Notify 판정은 Update에서 유지하되 실제 생성은 Animator와 캐릭터 루트 이동이 반영된 뒤 처리한다.
        public static EffectHandle PlayAfterAnimation(
            CompositeEffect composite, EffectPlayContext context, bool trackForStop = false)
        {
            if (composite == null || context.Spawner == null || context.CharacterRoot == null) return null;

            var handle = trackForStop ? new EffectHandle() : null;
            GetRunner().EnqueueLateUpdate(() =>
            {
                if (context.Spawner == null || context.CharacterRoot == null
                    || (handle != null && handle.IsStopped)) return;
                PlayEntries(composite, context, handle, true);
            });
            return handle;
        }

        private static void PlayEntries(
            CompositeEffect composite, EffectPlayContext context, EffectHandle handle,
            bool afterAnimation)
        {
            foreach (CompositeEffectEntry entry in composite.Entries)
            {
                if (entry == null || entry.Prefab == null) continue;

                if (entry.StartDelay <= 0f)
                {
                    // PlayEntry는 항상 호출(스폰). handle?.Add(PlayEntry(...))로 쓰면 handle이 null(단발)일 때
                    // null-조건 연산자가 인자까지 통째로 건너뛰어 스폰 자체가 안 되므로, 반드시 먼저 호출한다.
                    var spawned = PlayEntry(entry, context);
                    handle?.Add(spawned);
                }
                else
                {
                    var e = entry;   // 클로저 캡처
                    var h = handle;
                    GetRunner().Delay(entry.StartDelay, () =>
                    {
                        if (context.Spawner == null || context.CharacterRoot == null) return;
                        if (afterAnimation)
                        {
                            GetRunner().EnqueueLateUpdate(() =>
                            {
                                if (context.Spawner == null || context.CharacterRoot == null
                                    || (h != null && h.IsStopped)) return;
                                var lateSpawned = PlayEntry(e, context);
                                h?.Add(lateSpawned);
                            });
                            return;
                        }

                        var spawned = PlayEntry(e, context);
                        h?.Add(spawned);   // 이미 Stop됐으면 핸들이 즉시 정지 처리
                    });
                }
            }
        }

        private static PooledEffectHandle PlayEntry(
            CompositeEffectEntry entry, EffectPlayContext context)
        {
            EffectPool pool     = GetOrCreatePool(entry.Prefab);
            GameObject instance = pool.Get();

            Transform anchor = FindSocket(context.Spawner, entry.Socket);
            PlaceInstance(instance, entry, anchor, context.CharacterRoot);
            BindCustomSimulationSpace(instance, context.CharacterRoot);
            BindEffectModules(instance, entry, context.CharacterRoot);

            var handle = instance.GetComponent<PooledEffectHandle>();
            if (handle == null) handle = instance.AddComponent<PooledEffectHandle>();
            handle.Bind(pool, entry);

            instance.SetActive(true);
            HitData entryHit = context.Hit != null
                && !string.IsNullOrEmpty(context.Hit.EffectKey)
                && string.Equals(
                    context.Hit.EffectKey, entry.BindingKey?.Trim(),
                    System.StringComparison.Ordinal)
                ? context.Hit
                : null;
            var entryContext = new EffectPlayContext(
                context.Spawner, context.CharacterRoot, entryHit,
                context.Bindings, context.DebugDraw, context.DebugDuration);
            handle.NotifyPlaybackStarted(entryContext, entry.BindingKey);
            RestartParticles(instance);
            return handle;
        }

        private static void BindCustomSimulationSpace(
            GameObject instance, Transform characterRoot)
        {
            var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                if (main.simulationSpace == ParticleSystemSimulationSpace.Custom
                    && main.customSimulationSpace != characterRoot)
                    main.customSimulationSpace = characterRoot;
            }
        }

        private static void BindEffectModules(
            GameObject instance, CompositeEffectEntry entry, Transform characterRoot)
        {
            if (entry.Modules == null || entry.Modules.Count == 0) return;

            var runner = instance.GetComponent<EffectModuleRunner>();
            if (runner == null) runner = instance.AddComponent<EffectModuleRunner>();
            runner.Bind(entry.Modules, characterRoot);
        }

        // 풀 개요 모니터(에디터 툴)용 읽기 전용 스냅샷. Play 모드에서만 의미 있음.
        public static IEnumerable<EffectPool> DebugPools =>
            s_pools != null ? s_pools.Values : System.Linq.Enumerable.Empty<EffectPool>();

        // 온디맨드 풀 획득 — 프리웜 안 된 프리팹은 여기서 무제한(상한 0)으로 생성된다.
        private static EffectPool GetOrCreatePool(GameObject prefab)
        {
            if (s_pools == null) ResetState();
            if (!s_pools.TryGetValue(prefab, out EffectPool pool))
            {
                pool = new EffectPool(prefab, 0, 0, GetPoolRoot());
                s_pools[prefab] = pool;
            }
            return pool;
        }

        // RegisterOwner가 프리팹의 EffectPoolConfig 값으로 호출 — 프리팹 단위 전역 풀을 미리 채운다(첫 스폰 히칭/GC 방지).
        // 이미 있는 풀이면 free 인스턴스를 count까지 보충한다(여러 캐릭터가 같은 프리팹을 프리웜해도 안전).
        // 상한(maxSize)은 풀 최초 생성 시에만 적용(프리팹 단위 공유라 최초값 유지).
        public static void Prewarm(GameObject prefab, int count, int maxSize)
        {
            if (prefab == null) return;
            if (s_pools == null) ResetState();
            if (!s_pools.TryGetValue(prefab, out EffectPool pool))
            {
                pool = new EffectPool(prefab, count, maxSize, GetPoolRoot());
                s_pools[prefab] = pool;
            }
            else pool.Prewarm(count);
        }

        // 소유권 등록 — 이 프리팹을 쓰는 캐릭터가 로드될 때(EffectOwnership이 config에서 유도해 호출).
        // 용량(프리웜 개수·상한)은 프리팹의 EffectPoolConfig에서 읽는다 — 프리팹 단위 속성이라 소유자와 무관.
        // Config가 없으면 온디맨드(프리웜 0·무제한). 풀은 Prewarm이 MaxSize와 함께 (없으면) 생성하고 owner만 추가.
        public static void RegisterOwner(GameObject prefab, Object owner)
        {
            if (prefab == null || owner == null) return;

            int count = 0, maxSize = 0;
            var cfg = prefab.GetComponent<EffectPoolConfig>();
            if (cfg != null) { count = cfg.PrewarmCount; maxSize = cfg.MaxSize; }

            Prewarm(prefab, count, maxSize);
            GetOrCreatePool(prefab).AddOwner(owner);
        }

        // 소유권 해제 — 캐릭터 언로드 시(OnDestroy). 마지막 owner가 빠지면 풀이 teardown되어
        // 대기 인스턴스를 파괴하고, 재생 중이던 것도 끝나는 대로 회수된다(EffectPool.RemoveOwner).
        // 텍스처 등 에셋 메모리까지 실제로 내리려면 씬 전환 등 적절한 시점에 Resources.UnloadUnusedAssets() 호출.
        public static void UnregisterOwner(GameObject prefab, Object owner)
        {
            if (prefab == null || owner == null || s_pools == null) return;
            if (s_pools.TryGetValue(prefab, out EffectPool pool))
                pool.RemoveOwner(owner);
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

        private static void PlaceInstance(GameObject instance, CompositeEffectEntry entry, Transform anchor, Transform spawnerRoot)
        {
            Transform t = instance.transform;
            var socketFollower = instance.GetComponent<EffectSocketFollower>();
            if (socketFollower != null) socketFollower.Unbind();

            // 스폰 월드 포즈.
            // 회전 기준 프레임: 기본은 소켓(본) 회전. IgnoreSocketRotation이면 본에 구워진 회전 대신
            // 스포너 루트(캐릭터 facing)를 기준으로 삼는다 — 월드 절대가 아니라 '캐릭터 기준'으로 조준되어
            // 캐릭터가 돌면 이펙트도 함께 돈다. offset/euler 모두 이 프레임 기준.
            Quaternion frame = (entry.IgnoreSocketRotation && spawnerRoot != null)
                ? spawnerRoot.rotation
                : anchor.rotation;
            Vector3 spawnPos = entry.IgnoreSocketRotation
                ? anchor.position + frame * entry.PositionOffset
                : anchor.TransformPoint(entry.PositionOffset);
            Quaternion spawnRot = frame * Quaternion.Euler(entry.EulerOffset);

            // 소켓(손) 위치·방향에서 스폰하되 부모는 스포너 루트(캐릭터)로 — 손 스윙은 무시, 캐릭터 이동/방향만 따라감.
            // 발사/빔처럼 "손에서 나가지만 손 스윙에 휘둘리면 안 되고, 캐릭터는 따라와야" 하는 이펙트용.
            if (entry.ParentToSpawnerRoot && spawnerRoot != null)
            {
                t.SetParent(null, true);
                t.position = spawnPos;
                t.rotation = spawnRot;
                t.SetParent(spawnerRoot, true);   // worldPositionStays=true → 손 스폰 위치 유지, 이후 루트를 따라감
                t.localScale = entry.Scale;
                return;
            }

            if (entry.FollowSpawner)
            {
                if (HasModule<ArcMotionEffectModule>(entry))
                {
                    // ArcMotion이 활성화되면 시작 시 캐릭터 루트로 재부모화하고 위치를 직접 구동한다.
                    t.SetParent(anchor, false);
                    t.localPosition = entry.PositionOffset;
                    t.localEulerAngles = entry.EulerOffset;
                }
                else
                {
                    // 소켓의 직접 자식이면 Animator 평가 중 Bip001에 남아 있는 원본 루트 이동까지
                    // 파티클 방출 위치에 반영된다. 계층에서 분리하고 보정된 프레임의 소켓 포즈만 복사한다.
                    t.SetParent(null, true);
                    bool followRotation = !HasModule<FaceOutwardEffectModule>(entry);
                    if (socketFollower == null)
                        socketFollower = instance.AddComponent<EffectSocketFollower>();
                    socketFollower.Bind(
                        anchor, entry.PositionOffset, entry.EulerOffset,
                        entry.Scale, followRotation);
                    return;
                }
                // (IgnoreSocketRotation은 소켓 추종 모드에선 무효 — 소켓 회전을 계속 따라감)
            }
            else
            {
                t.SetParent(null, true);
                t.position = spawnPos;
                t.rotation = spawnRot;
            }
            t.localScale = entry.Scale;
        }

        private static bool HasModule<T>(CompositeEffectEntry entry)
            where T : EffectModule
        {
            if (entry.Modules == null) return false;
            for (int i = 0; i < entry.Modules.Count; i++)
                if (entry.Modules[i] is T) return true;
            return false;
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

    // Animator가 소켓 계층에 원본 루트 이동을 임시로 쓰는 동안에는 계층에서 분리된 직전 보정 포즈를
    // 유지하고, PlayerMotor가 LateUpdate에서 Bip001을 보정한 뒤 현재 소켓 포즈를 다시 복사한다.
    [DefaultExecutionOrder(90)]
    internal sealed class EffectSocketFollower : MonoBehaviour
    {
        private Transform _anchor;
        private Vector3 _positionOffset;
        private Quaternion _rotationOffset;
        private Vector3 _scale;
        private bool _followRotation;

        internal void Bind(
            Transform anchor, Vector3 positionOffset, Vector3 eulerOffset,
            Vector3 scale, bool followRotation)
        {
            _anchor = anchor;
            _positionOffset = positionOffset;
            _rotationOffset = Quaternion.Euler(eulerOffset);
            _scale = scale;
            _followRotation = followRotation;
            enabled = true;
            ApplyPose();
        }

        internal void Unbind()
        {
            _anchor = null;
            enabled = false;
        }

        private void Update() => ApplyPose();
        private void LateUpdate() => ApplyPose();

        private void OnDisable()
        {
            _anchor = null;
        }

        private void ApplyPose()
        {
            if (_anchor == null) return;

            transform.position = _anchor.TransformPoint(_positionOffset);
            if (_followRotation)
                transform.rotation = _anchor.rotation * _rotationOffset;
            transform.localScale = _scale;
        }
    }
}
