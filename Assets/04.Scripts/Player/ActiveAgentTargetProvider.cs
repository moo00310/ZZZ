using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using ZZZ.Agent;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SquadController))]
    [MovedFrom(true, "ZZZ.Player", "Assembly-CSharp", "ActiveCharacterTargetProvider")]
    public sealed class ActiveAgentTargetProvider : TargetProvider
    {
        [SerializeField] private SquadController _squad;

        public override Transform CurrentTarget => _squad != null
            && _squad.ActiveAgent != null
                ? _squad.ActiveAgent.transform
                : null;

        public override event Action<Transform> TargetChanged;

        private void Awake()
        {
            if (_squad == null)
                _squad = GetComponent<SquadController>();
        }

        private void OnEnable()
        {
            _squad.OnActiveAgentChanged += OnActiveAgentChanged;
        }

        private void OnDisable()
        {
            if (_squad != null)
                _squad.OnActiveAgentChanged -= OnActiveAgentChanged;
            TargetChanged?.Invoke(null);
        }

        private void OnActiveAgentChanged(AgentRoot agent)
        {
            TargetChanged?.Invoke(agent != null ? agent.transform : null);
        }
    }
}
