using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZZZ.ResourceManagement
{
    public interface IAssetLoadOperation<T> : IDisposable
    {
        Task<T> Completion { get; }
    }

    public sealed class AssetLease<T> : IDisposable
    {
        private Action _release;

        public T Asset { get; }

        internal AssetLease(T asset, Action release)
        {
            Asset = asset;
            _release = release;
        }

        public void Dispose()
        {
            Action release = Interlocked.Exchange(ref _release, null);
            release?.Invoke();
        }
    }

    // Calls and lease disposal belong to the Unity main thread.
    public sealed class SharedAssetStore<T>
    {
        private sealed class Entry
        {
            public string Key;
            public IAssetLoadOperation<T> Operation;
            public int References;
        }

        private readonly Func<string, IAssetLoadOperation<T>> _load;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        public int EntryCount => _entries.Count;

        public SharedAssetStore(Func<string, IAssetLoadOperation<T>> load)
        {
            _load = load ?? throw new ArgumentNullException(nameof(load));
        }

        public async Task<AssetLease<T>> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("An asset key is required.", nameof(key));
            cancellationToken.ThrowIfCancellationRequested();

            if (!_entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry { Key = key, Operation = _load(key), References = 1 };
                _entries.Add(key, entry);
                _ = ObserveCompletionAsync(entry);
            }
            else entry.References++;

            try
            {
                T asset = await ResourceTask.WaitAsync(entry.Operation.Completion, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new AssetLease<T>(asset, () => Release(entry));
            }
            catch
            {
                Release(entry);
                throw;
            }
        }

        public bool TryGetLoaded(string key, out T asset)
        {
            asset = default;
            if (string.IsNullOrEmpty(key) || !_entries.TryGetValue(key, out Entry entry)
                || entry.References == 0 || entry.Operation.Completion.Status != TaskStatus.RanToCompletion)
                return false;

            asset = entry.Operation.Completion.Result;
            return true;
        }

        private async Task ObserveCompletionAsync(Entry entry)
        {
            try { await entry.Operation.Completion; }
            catch { }
            finally
            {
                if (entry.References == 0) Remove(entry);
            }
        }

        private void Release(Entry entry)
        {
            entry.References--;
            // A caller can cancel its wait, but the shared Addressables operation still owns its handle.
            if (entry.References == 0 && entry.Operation.Completion.IsCompleted) Remove(entry);
        }

        private void Remove(Entry entry)
        {
            if (!_entries.TryGetValue(entry.Key, out Entry current) || current != entry) return;
            _entries.Remove(entry.Key);
            entry.Operation.Dispose();
        }
    }

    public static class ResourceTask
    {
        public static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) return await task;
            cancellationToken.ThrowIfCancellationRequested();
            var cancelled = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task)
                    throw new OperationCanceledException(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return await task;
        }
    }
}
