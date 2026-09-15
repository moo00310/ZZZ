using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ZZZ.Effects;
using ZZZ.ResourceManagement;
using Object = UnityEngine.Object;

namespace ZZZ
{
    public static class EffectOwnership
    {
        private sealed class Scope
        {
            private readonly Object _owner;
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private readonly List<AssetLease<GameObject>> _leases = new List<AssetLease<GameObject>>();
            private readonly HashSet<GameObject> _prefabs = new HashSet<GameObject>();
            private Task _release;

            public Task<bool> Ready { get; private set; }

            public Scope(Object owner, List<CompositeEffectEntry> entries)
            {
                _owner = owner;
                _cancellation.CancelAfter(TimeSpan.FromSeconds(30));
                Ready = PrepareAsync(entries);
            }

            private async Task<bool> PrepareAsync(List<CompositeEffectEntry> entries)
            {
                try
                {
                    var keys = new HashSet<string>();
                    foreach (CompositeEffectEntry entry in entries)
                    {
                        GameObject prefab;
                        if (entry.PrefabReference != null && entry.PrefabReference.RuntimeKeyIsValid())
                        {
                            string key = entry.PrefabReference.AssetGUID;
                            if (!keys.Add(key)) continue;
                            AssetLease<GameObject> lease = await AddressableResources.AcquirePrefabAsync(
                                key, _cancellation.Token);
                            _leases.Add(lease);
                            prefab = lease.Asset;
                        }
                        else prefab = entry.LegacyPrefab;

                        _cancellation.Token.ThrowIfCancellationRequested();
                        if (_owner == null) throw new OperationCanceledException();
                        if (prefab != null && _prefabs.Add(prefab))
                            EffectService.RegisterOwner(prefab, _owner);
                    }
                    _cancellation.CancelAfter(Timeout.Infinite);
                    return true;
                }
                catch (Exception exception)
                {
                    if (_owner != null && !_cancellation.IsCancellationRequested)
                        Debug.LogError($"VFX preparation failed for {_owner.name}: {exception.Message}", _owner);
                    await ReleaseResourcesAsync();
                    return false;
                }
            }

            public async Task CloseAsync()
            {
                _cancellation.Cancel();
                await Ready;
                await ReleaseResourcesAsync();
                _cancellation.Dispose();
            }

            private Task ReleaseResourcesAsync()
            {
                if (_release == null) _release = ReleaseCoreAsync();
                return _release;
            }

            private async Task ReleaseCoreAsync()
            {
                var releases = new List<Task>();
                foreach (GameObject prefab in _prefabs)
                    releases.Add(EffectService.UnregisterOwnerAsync(prefab, _owner));
                await Task.WhenAll(releases);
                _prefabs.Clear();
                foreach (AssetLease<GameObject> lease in _leases) lease.Dispose();
                _leases.Clear();
            }
        }

        private static Dictionary<Object, Scope> _scopes = new Dictionary<Object, Scope>();
        private static Dictionary<Object, Task> _closing = new Dictionary<Object, Task>();

        public static bool IsReady(Object owner)
        {
            return !ReferenceEquals(owner, null) && _scopes.TryGetValue(owner, out Scope scope)
                && scope.Ready.Status == TaskStatus.RanToCompletion && scope.Ready.Result;
        }

        public static Task<bool> WaitUntilReady(Object owner)
        {
            return !ReferenceEquals(owner, null) && _scopes.TryGetValue(owner, out Scope scope)
                ? scope.Ready : Task.FromResult(false);
        }

        public static void Register(Object owner, IEnumerable<AnimationConfig> configs)
        {
            var entries = new List<CompositeEffectEntry>();
            var visited = new HashSet<AnimationConfig>();
            if (configs != null)
                foreach (AnimationConfig config in configs) CollectConfig(config, visited, entries);
            RegisterEntries(owner, entries);
        }

        public static void Register(Object owner, params AnimationConfig[] configs)
            => Register(owner, (IEnumerable<AnimationConfig>)configs);

        public static void Register(Object owner, CompositeEffect composite)
            => Register(owner, new[] { composite });

        public static void Register(Object owner, IEnumerable<CompositeEffect> composites)
        {
            var entries = new List<CompositeEffectEntry>();
            if (composites != null)
                foreach (CompositeEffect composite in composites) CollectComposite(composite, entries);
            RegisterEntries(owner, entries);
        }

        public static void Unregister(Object owner, IEnumerable<AnimationConfig> configs) => Unregister(owner);
        public static void Unregister(Object owner, params AnimationConfig[] configs) => Unregister(owner);
        public static void Unregister(Object owner, CompositeEffect composite) => Unregister(owner);
        public static void Unregister(Object owner, IEnumerable<CompositeEffect> composites) => Unregister(owner);
        public static void Unregister(Object owner) => _ = UnregisterAsync(owner);

        public static Task UnregisterAsync(Object owner)
        {
            if (ReferenceEquals(owner, null)) return Task.CompletedTask;
            if (_closing.TryGetValue(owner, out Task closing)) return closing;
            if (!_scopes.TryGetValue(owner, out Scope scope)) return Task.CompletedTask;
            _scopes.Remove(owner);
            Task release = scope.CloseAsync();
            _closing.Add(owner, release);
            _ = ForgetClosedScopeAsync(owner, release);
            return release;
        }

        private static async Task ForgetClosedScopeAsync(Object owner, Task release)
        {
            try { await release; }
            finally { _closing.Remove(owner); }
        }

        private static void RegisterEntries(Object owner, List<CompositeEffectEntry> entries)
        {
            if (owner == null || _scopes.ContainsKey(owner)) return;
            _scopes.Add(owner, new Scope(owner, entries));
        }

        private static void CollectConfig(
            AnimationConfig config, HashSet<AnimationConfig> visited, List<CompositeEffectEntry> entries)
        {
            if (config == null || !visited.Add(config)) return;
            foreach (ClipLink link in config.GlobalLinks)
                if (link != null) CollectConfig(link.TargetConfig, visited, entries);
            foreach (TrackClip clip in config.Clips)
            {
                if (clip == null) continue;
                foreach (ClipLink link in clip.Links)
                    if (link != null) CollectConfig(link.TargetConfig, visited, entries);
                foreach (TrackNotify notify in clip.Notifies)
                    if (notify?.Payload is EffectNotifyPayload payload) CollectComposite(payload.Effect, entries);
            }
        }

        private static void CollectComposite(CompositeEffect composite, List<CompositeEffectEntry> entries)
        {
            if (composite == null || composite.Entries == null) return;
            foreach (CompositeEffectEntry entry in composite.Entries)
                if (entry != null) entries.Add(entry);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _scopes = new Dictionary<Object, Scope>();
            _closing = new Dictionary<Object, Task>();
        }
    }
}
