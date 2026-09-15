using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Serialization;
using ZZZ.Agent;
using ZZZ.Combat;
using ZZZ.ResourceManagement;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputRouter))]
    public sealed class SquadController : MonoBehaviour
    {
        private sealed class LoadedAgent
        {
            public AgentRoot Agent;
            public AssetLease<GameObject> Lease;
            public List<MonoBehaviour> ResourceOwners = new List<MonoBehaviour>();
        }

        [Header("Squad")]
        [FormerlySerializedAs("_characterPrefabs")]
        [SerializeField, HideInInspector] private List<AgentRoot> _agentPrefabs = new List<AgentRoot>();
        [SerializeField] private List<AssetReferenceGameObject> _agentReferences = new List<AssetReferenceGameObject>();
        [SerializeField] private int _initialIndex;
        [FormerlySerializedAs("_characterParent")]
        [SerializeField] private Transform _agentParent;
        [SerializeField, Min(1f)] private float _loadTimeout = 30f;

        [Header("Runtime References")]
        [SerializeField] private TPSCameraController _cameraController;

        private readonly List<LoadedAgent> _agents = new List<LoadedAgent>();
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private PlayerInputRouter _inputRouter;
        private AgentRoot _activeAgent;
        private int _activeIndex = -1;
        private Task<bool> _initialization;
        private Task _unloading;
        private GameObject _stagingRoot;

        public event Action<AgentRoot> OnActiveAgentChanged;

        public AgentRoot ActiveAgent => _activeAgent;
        public int ActiveIndex => _activeIndex;
        public bool IsLoading { get; private set; }
        public bool IsReady { get; private set; }
        public bool IsShuttingDown => _lifetimeCancellation.IsCancellationRequested;
        public string LoadError { get; private set; }

        private void Awake()
        {
            _inputRouter = GetComponent<PlayerInputRouter>();
            if (_cameraController == null && Camera.main != null)
                _cameraController = Camera.main.GetComponent<TPSCameraController>();
        }

        private void OnEnable()
        {
            _inputRouter.OnPreviousRequested += SwitchPrevious;
            _inputRouter.OnNextRequested += SwitchNext;
        }

        private async void Start() => await InitializeAsync();

        private void OnDisable()
        {
            _inputRouter.OnPreviousRequested -= SwitchPrevious;
            _inputRouter.OnNextRequested -= SwitchNext;
        }

        private void OnDestroy() => _ = ShutdownAsync();

        public Task<bool> InitializeAsync()
        {
            if (IsReady) return Task.FromResult(true);
            if (_initialization != null && !_initialization.IsCompleted) return _initialization;
            if (_lifetimeCancellation.IsCancellationRequested) return Task.FromResult(false);
            _unloading = null;
            _initialization = LoadAgentsAsync();
            return _initialization;
        }

        private async Task<bool> LoadAgentsAsync()
        {
            IsLoading = true;
            LoadError = null;
            _inputRouter.ClearTarget();
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token))
            {
                cancellation.CancelAfter(TimeSpan.FromSeconds(_loadTimeout));
                try
                {
                    _stagingRoot = new GameObject("Preparing Squad");
                    _stagingRoot.SetActive(false);
                    int count = _agentReferences.Count > 0 ? _agentReferences.Count : _agentPrefabs.Count;
                    if (count == 0) throw new InvalidOperationException("Squad contains no character references.");

                    for (int i = 0; i < count; i++)
                    {
                        var loaded = new LoadedAgent();
                        _agents.Add(loaded);
                        GameObject prefab;
                        if (_agentReferences.Count > 0)
                        {
                            AssetReferenceGameObject reference = _agentReferences[i];
                            if (reference == null || !reference.RuntimeKeyIsValid())
                                throw new InvalidOperationException($"Invalid character reference at squad slot {i}.");
                            loaded.Lease = await AddressableResources.AcquirePrefabAsync(
                                reference.AssetGUID, cancellation.Token);
                            prefab = loaded.Lease.Asset;
                        }
                        else
                        {
                            if (_agentPrefabs[i] == null)
                                throw new InvalidOperationException($"Missing legacy character at squad slot {i}.");
                            prefab = _agentPrefabs[i].gameObject;
                        }
                        cancellation.Token.ThrowIfCancellationRequested();
                        GameObject instance = Instantiate(prefab, _stagingRoot.transform);
                        loaded.Agent = instance.GetComponent<AgentRoot>();
                        if (loaded.Agent == null)
                        {
                            Destroy(instance);
                            await ResourceLifetime.WaitForDestructionAsync(new[] { instance });
                            throw new InvalidOperationException($"Character prefab has no AgentRoot: {prefab.name}");
                        }

                        AgentActionController controller = instance.GetComponent<AgentActionController>();
                        loaded.ResourceOwners.Add(controller);
                        if (!await controller.PrepareResourcesAsync(cancellation.Token))
                            throw new InvalidOperationException($"VFX preparation failed for {prefab.name}.");
                        foreach (HitFeedbackReceiver receiver in instance.GetComponentsInChildren<HitFeedbackReceiver>(true))
                        {
                            loaded.ResourceOwners.Add(receiver);
                            if (!await receiver.PrepareResourcesAsync(cancellation.Token))
                                throw new InvalidOperationException($"Hit feedback preparation failed for {prefab.name}.");
                        }
                        instance.SetActive(false);
                        instance.transform.SetParent(_agentParent, false);
                    }
                    cancellation.Token.ThrowIfCancellationRequested();
                    Destroy(_stagingRoot);
                    _stagingRoot = null;
                    IsReady = true;
                    SwitchTo(Mathf.Clamp(_initialIndex, 0, _agents.Count - 1));
                    return true;
                }
                catch (Exception exception)
                {
                    LoadError = exception is OperationCanceledException
                        ? "Squad loading cancelled or timed out." : exception.Message;
                    if (this != null && !_lifetimeCancellation.IsCancellationRequested)
                        Debug.LogError(LoadError, this);
                    await UnloadAgentsAsync();
                    return false;
                }
                finally { IsLoading = false; }
            }
        }

        public async Task ShutdownAsync()
        {
            if (!_lifetimeCancellation.IsCancellationRequested) _lifetimeCancellation.Cancel();
            if (_initialization != null) await _initialization;
            await UnloadAgentsAsync();
        }

        private Task UnloadAgentsAsync()
        {
            if (_unloading == null) _unloading = UnloadCoreAsync();
            return _unloading;
        }

        private async Task UnloadCoreAsync()
        {
            IsReady = false;
            _activeAgent = null;
            _activeIndex = -1;
            if (_inputRouter != null) _inputRouter.ClearTarget();
            var releases = new List<Task>();
            foreach (LoadedAgent loaded in _agents)
            {
                if (loaded.Agent != null) loaded.Agent.Deactivate();
                // Keep owner wrappers even if a scene unload already destroyed never-activated agents.
                foreach (MonoBehaviour owner in loaded.ResourceOwners)
                    releases.Add(EffectOwnership.UnregisterAsync(owner));
            }
            await Task.WhenAll(releases);
            var destroyed = new List<GameObject>();
            foreach (LoadedAgent loaded in _agents)
            {
                if (loaded.Agent == null) continue;
                destroyed.Add(loaded.Agent.gameObject);
                Destroy(loaded.Agent.gameObject);
            }
            if (_stagingRoot != null)
            {
                destroyed.Add(_stagingRoot);
                Destroy(_stagingRoot);
                _stagingRoot = null;
            }
            await ResourceLifetime.WaitForDestructionAsync(destroyed);
            foreach (LoadedAgent loaded in _agents) loaded.Lease?.Dispose();
            _agents.Clear();
        }

        public void SwitchPrevious()
        {
            if (!IsReady || _agents.Count < 2) return;
            SwitchTo((_activeIndex - 1 + _agents.Count) % _agents.Count);
        }

        public void SwitchNext()
        {
            if (!IsReady || _agents.Count < 2) return;
            SwitchTo((_activeIndex + 1) % _agents.Count);
        }

        public bool SwitchTo(int index)
        {
            if (!IsReady || index < 0 || index >= _agents.Count) return false;
            if (_activeAgent != null && index == _activeIndex) return true;
            Vector3 sharedPosition = _activeAgent != null ? _activeAgent.transform.position : transform.position;
            _inputRouter.ClearTarget();
            if (_activeAgent != null) _activeAgent.Deactivate();
            _activeIndex = index;
            _activeAgent = _agents[index].Agent;
            _activeAgent.Activate(sharedPosition);
            _inputRouter.SetTarget(_activeAgent.InputTarget);
            if (_cameraController != null) _cameraController.SetTarget(_activeAgent.CameraPoint, true);
            OnActiveAgentChanged?.Invoke(_activeAgent);
            return true;
        }
    }
}
