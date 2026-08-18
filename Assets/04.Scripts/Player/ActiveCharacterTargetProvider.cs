using System;
using UnityEngine;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SquadController))]
    public sealed class ActiveCharacterTargetProvider : TargetProvider
    {
        [SerializeField] private SquadController _squad;

        public override Transform CurrentTarget => _squad != null
            && _squad.ActiveCharacter != null
                ? _squad.ActiveCharacter.transform
                : null;

        public override event Action<Transform> TargetChanged;

        private void Awake()
        {
            if (_squad == null)
                _squad = GetComponent<SquadController>();
        }

        private void OnEnable()
        {
            _squad.OnActiveCharacterChanged += OnActiveCharacterChanged;
        }

        private void OnDisable()
        {
            if (_squad != null)
                _squad.OnActiveCharacterChanged -= OnActiveCharacterChanged;
            TargetChanged?.Invoke(null);
        }

        private void OnActiveCharacterChanged(PlayableCharacter character)
        {
            TargetChanged?.Invoke(character != null ? character.transform : null);
        }
    }
}
