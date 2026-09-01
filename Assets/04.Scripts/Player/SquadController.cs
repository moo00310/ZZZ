using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using ZZZ.Agent;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputRouter))]
    public sealed class SquadController : MonoBehaviour
    {
        [Header("Squad")]
        [FormerlySerializedAs("_characterPrefabs")]
        [SerializeField] private List<AgentRoot> _agentPrefabs = new List<AgentRoot>();
        [SerializeField] private int _initialIndex;
        [FormerlySerializedAs("_characterParent")]
        [SerializeField] private Transform _agentParent;

        [Header("Runtime References")]
        [SerializeField] private TPSCameraController _cameraController;

        private readonly List<AgentRoot> _agents = new List<AgentRoot>();
        private PlayerInputRouter _inputRouter;
        private AgentRoot _activeAgent;
        private int _activeIndex = -1;

        public event Action<AgentRoot> OnActiveAgentChanged;

        public AgentRoot ActiveAgent => _activeAgent;
        public int ActiveIndex => _activeIndex;

        private void Awake()
        {
            _inputRouter = GetComponent<PlayerInputRouter>();

            if (_cameraController == null && Camera.main != null)
                _cameraController = Camera.main.GetComponent<TPSCameraController>();

            CreateAgents();
        }

        private void OnEnable()
        {
            _inputRouter.OnPreviousRequested += SwitchPrevious;
            _inputRouter.OnNextRequested += SwitchNext;
        }

        private void Start()
        {
            if (_agents.Count == 0)
            {
                Debug.LogError("SquadController has no valid agent prefabs.", this);
                return;
            }

            int index = Mathf.Clamp(_initialIndex, 0, _agents.Count - 1);
            SwitchTo(index);
        }

        private void OnDisable()
        {
            _inputRouter.OnPreviousRequested -= SwitchPrevious;
            _inputRouter.OnNextRequested -= SwitchNext;
        }

        public void SwitchPrevious()
        {
            if (_agents.Count < 2) return;
            SwitchTo((_activeIndex - 1 + _agents.Count) % _agents.Count);
        }

        public void SwitchNext()
        {
            if (_agents.Count < 2) return;
            SwitchTo((_activeIndex + 1) % _agents.Count);
        }

        public bool SwitchTo(int index)
        {
            if (index < 0 || index >= _agents.Count) return false;
            if (_activeAgent != null && index == _activeIndex) return true;

            Vector3 sharedPosition = _activeAgent != null
                ? _activeAgent.transform.position
                : transform.position;

            _inputRouter.ClearTarget();
            if (_activeAgent != null) _activeAgent.Deactivate();

            _activeIndex = index;
            _activeAgent = _agents[index];
            _activeAgent.Activate(sharedPosition);
            _inputRouter.SetTarget(_activeAgent.InputTarget);

            if (_cameraController != null)
                _cameraController.SetTarget(_activeAgent.CameraPoint, true);

            OnActiveAgentChanged?.Invoke(_activeAgent);
            return true;
        }

        private void CreateAgents()
        {
            for (int i = 0; i < _agentPrefabs.Count; i++)
            {
                AgentRoot prefab = _agentPrefabs[i];
                if (prefab == null) continue;

                AgentRoot agent = Instantiate(prefab, _agentParent);
                agent.Deactivate();
                _agents.Add(agent);
            }
        }
    }
}
