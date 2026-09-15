using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ZZZ.ResourceManagement
{
    public sealed class ResourceLifetime : MonoBehaviour
    {
        private static ResourceLifetime _runner;
        private static bool _quitting;
        private readonly HashSet<TaskCompletionSource<bool>> _pending = new HashSet<TaskCompletionSource<bool>>();

        public static Task WaitForDestructionAsync(IReadOnlyList<GameObject> objects)
        {
            if (_quitting || !Application.isPlaying || objects.Count == 0) return Task.CompletedTask;
            if (_runner == null)
            {
                var root = new GameObject("ResourceLifetime");
                DontDestroyOnLoad(root);
                _runner = root.AddComponent<ResourceLifetime>();
            }
            var completion = new TaskCompletionSource<bool>();
            _runner._pending.Add(completion);
            _runner.StartCoroutine(_runner.WaitForDestruction(objects, completion));
            return completion.Task;
        }

        private IEnumerator WaitForDestruction(
            IReadOnlyList<GameObject> objects, TaskCompletionSource<bool> completion)
        {
            // Object.Destroy is deferred, so handles must survive the frame containing the destroy calls.
            yield return null;
            while (true)
            {
                bool alive = false;
                for (int i = 0; i < objects.Count; i++)
                    if (objects[i] != null) { alive = true; break; }
                if (!alive) break;
                yield return null;
            }
            _pending.Remove(completion);
            completion.TrySetResult(true);
        }

        private static void BeginShutdown()
        {
            _quitting = true;
            if (_runner != null) _runner.CompletePending();
        }

        private void CompletePending()
        {
            // Unity stops these coroutines at shutdown; unblock resource cleanup before they disappear.
            var completions = new List<TaskCompletionSource<bool>>(_pending);
            _pending.Clear();
            foreach (TaskCompletionSource<bool> completion in completions) completion.TrySetResult(true);
        }

        private void OnApplicationQuit() => BeginShutdown();

        private void OnDestroy()
        {
            if (!ReferenceEquals(_runner, this)) return;
            _quitting = true;
            _runner = null;
            CompletePending();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            // Listen before the lazy runner exists, so OnDestroy cannot create it during Play Mode exit.
            Application.quitting -= BeginShutdown;
            Application.quitting += BeginShutdown;
            _runner = null;
            _quitting = false;
        }
    }
}
