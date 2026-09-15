using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZZZ.ResourceManagement;

namespace ZZZ.Tests
{
    public sealed class SharedAssetStoreTests
    {
        private sealed class LoadOperation : IAssetLoadOperation<string>
        {
            public readonly TaskCompletionSource<string> Source = new TaskCompletionSource<string>();
            public int ReleaseCount;
            public Task<string> Completion => Source.Task;
            public void Dispose() => ReleaseCount++;
        }

        [Test]
        public void CompletedLoadIsRetainedUntilLastLeaseIsDisposed()
        {
            var operation = new LoadOperation();
            operation.Source.SetResult("prefab");
            int loads = 0;
            var store = new SharedAssetStore<string>(key => { loads++; return operation; });
            AssetLease<string> first = store.AcquireAsync("shared").Result;
            AssetLease<string> second = store.AcquireAsync("shared").Result;
            first.Dispose();
            first.Dispose();
            Assert.AreEqual(1, loads);
            Assert.AreEqual(0, operation.ReleaseCount);
            Assert.IsTrue(store.TryGetLoaded("shared", out string asset));
            Assert.AreEqual("prefab", asset);
            second.Dispose();
            Assert.AreEqual(1, operation.ReleaseCount);
            Assert.AreEqual(0, store.EntryCount);
        }

        [UnityTest]
        public IEnumerator ConcurrentPendingRequestsShareOneOperation()
        {
            var operation = new LoadOperation();
            int loads = 0;
            var store = new SharedAssetStore<string>(key => { loads++; return operation; });
            Task<AssetLease<string>> first = store.AcquireAsync("shared");
            Task<AssetLease<string>> second = store.AcquireAsync("shared");
            Assert.AreEqual(1, loads);
            operation.Source.SetResult("prefab");
            while (!first.IsCompleted || !second.IsCompleted) yield return null;
            first.Result.Dispose();
            Assert.AreEqual(0, operation.ReleaseCount);
            second.Result.Dispose();
            Assert.AreEqual(1, operation.ReleaseCount);
        }

        [UnityTest]
        public IEnumerator CancellingOneWaiterDoesNotCancelAnother()
        {
            var operation = new LoadOperation();
            var store = new SharedAssetStore<string>(key => operation);
            using (var cancellation = new CancellationTokenSource())
            {
                Task<AssetLease<string>> cancelled = store.AcquireAsync("shared", cancellation.Token);
                Task<AssetLease<string>> retained = store.AcquireAsync("shared");
                cancellation.Cancel();
                while (!cancelled.IsCompleted) yield return null;
                Assert.IsTrue(cancelled.IsCanceled);
                Assert.AreEqual(0, operation.ReleaseCount);
                operation.Source.SetResult("prefab");
                while (!retained.IsCompleted) yield return null;
                Assert.AreEqual("prefab", retained.Result.Asset);
                retained.Result.Dispose();
                Assert.AreEqual(1, operation.ReleaseCount);
            }
        }

        [UnityTest]
        public IEnumerator AllCancelledWaitersReleaseTheLateResult()
        {
            var operation = new LoadOperation();
            var store = new SharedAssetStore<string>(key => operation);
            using (var cancellation = new CancellationTokenSource())
            {
                Task<AssetLease<string>> request = store.AcquireAsync("late", cancellation.Token);
                cancellation.Cancel();
                while (!request.IsCompleted) yield return null;
                Assert.IsTrue(request.IsCanceled);
                Assert.IsFalse(store.TryGetLoaded("late", out _));
                Assert.AreEqual(0, operation.ReleaseCount);
                operation.Source.SetResult("prefab");
                while (store.EntryCount != 0) yield return null;
                Assert.AreEqual(1, operation.ReleaseCount);
            }
        }

        [UnityTest]
        public IEnumerator FailedRequestReleasesItsHandleAndCanBeRetried()
        {
            var failed = new LoadOperation();
            var success = new LoadOperation();
            success.Source.SetResult("recovered");
            int loads = 0;
            var store = new SharedAssetStore<string>(key => ++loads == 1 ? failed : success);
            Task<AssetLease<string>> request = store.AcquireAsync("retry");
            failed.Source.SetException(new InvalidOperationException("bad address"));
            while (!request.IsCompleted) yield return null;
            Assert.IsTrue(request.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(request.Exception.InnerException);
            Assert.AreEqual(1, failed.ReleaseCount);
            AssetLease<string> recovered = store.AcquireAsync("retry").Result;
            Assert.AreEqual("recovered", recovered.Asset);
            Assert.AreEqual(2, loads);
            recovered.Dispose();
            Assert.AreEqual(1, success.ReleaseCount);
        }

        [Test]
        public void AlreadyCancelledRequestDoesNotStartLoading()
        {
            int loads = 0;
            var store = new SharedAssetStore<string>(key => { loads++; return new LoadOperation(); });
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Task<AssetLease<string>> request = store.AcquireAsync("unused", cancellation.Token);
                Assert.IsTrue(request.IsCanceled);
                Assert.AreEqual(0, loads);
                Assert.AreEqual(0, store.EntryCount);
            }
        }
    }
}
