using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ZZZ.ResourceManagement
{
    public static class AddressableResources
    {
        private sealed class PrefabLoadOperation : IAssetLoadOperation<GameObject>
        {
            private readonly AsyncOperationHandle<GameObject> _handle;
            private readonly TaskCompletionSource<GameObject> _completion = new TaskCompletionSource<GameObject>();
            private bool _released;

            public Task<GameObject> Completion => _completion.Task;

            public PrefabLoadOperation(string key)
            {
                using (LoadMarker.Auto())
                    _handle = Addressables.LoadAssetAsync<GameObject>(key);
                _handle.Completed += OnCompleted;
            }

            private void OnCompleted(AsyncOperationHandle<GameObject> handle)
            {
                using (LoadCompleteMarker.Auto())
                {
                    if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                        _completion.TrySetResult(handle.Result);
                    else
                        _completion.TrySetException(handle.OperationException
                            ?? new InvalidOperationException("Addressable prefab load failed."));
                }
            }

            public void Dispose()
            {
                if (_released) return;
                _released = true;
                using (ReleaseMarker.Auto())
                {
                    if (!_handle.IsValid()) return;
                    _handle.Completed -= OnCompleted;
                    Addressables.Release(_handle);
                }
            }
        }

        private static readonly ProfilerMarker LoadMarker = new ProfilerMarker("ZZZ.Resources.LoadRequest");
        private static readonly ProfilerMarker LoadCompleteMarker = new ProfilerMarker("ZZZ.Resources.LoadComplete");
        private static readonly ProfilerMarker ReleaseMarker = new ProfilerMarker("ZZZ.Resources.Release");
        private static SharedAssetStore<GameObject> _prefabs = CreateStore();

        public static int RetainedPrefabCount => _prefabs.EntryCount;

        public static Task<AssetLease<GameObject>> AcquirePrefabAsync(
            string key, CancellationToken cancellationToken = default)
        {
            return _prefabs.AcquireAsync(key, cancellationToken);
        }

        public static bool TryGetPrefab(string key, out GameObject prefab)
        {
            return _prefabs.TryGetLoaded(key, out prefab);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => _prefabs = CreateStore();

        private static SharedAssetStore<GameObject> CreateStore()
        {
            return new SharedAssetStore<GameObject>(key => new PrefabLoadOperation(key));
        }
    }
}
