using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputRouter))]
    public sealed class SquadController : MonoBehaviour
    {
        [Header("Squad")]
        [SerializeField] private List<PlayableCharacter> _characterPrefabs = new List<PlayableCharacter>();
        [SerializeField] private int _initialIndex;
        [SerializeField] private Transform _characterParent;

        [Header("Runtime References")]
        [SerializeField] private TPSCameraController _cameraController;

        private readonly List<PlayableCharacter> _characters = new List<PlayableCharacter>();
        private PlayerInputRouter _inputRouter;
        private PlayableCharacter _activeCharacter;
        private int _activeIndex = -1;

        public event Action<PlayableCharacter> OnActiveCharacterChanged;

        public PlayableCharacter ActiveCharacter => _activeCharacter;
        public int ActiveIndex => _activeIndex;

        private void Awake()
        {
            _inputRouter = GetComponent<PlayerInputRouter>();

            if (_cameraController == null && Camera.main != null)
                _cameraController = Camera.main.GetComponent<TPSCameraController>();

            CreateCharacters();
        }

        private void OnEnable()
        {
            _inputRouter.OnPreviousRequested += SwitchPrevious;
            _inputRouter.OnNextRequested += SwitchNext;
        }

        private void Start()
        {
            if (_characters.Count == 0)
            {
                Debug.LogError("SquadController has no valid character prefabs.", this);
                return;
            }

            int index = Mathf.Clamp(_initialIndex, 0, _characters.Count - 1);
            SwitchTo(index);
        }

        private void OnDisable()
        {
            _inputRouter.OnPreviousRequested -= SwitchPrevious;
            _inputRouter.OnNextRequested -= SwitchNext;
        }

        public void SwitchPrevious()
        {
            if (_characters.Count < 2) return;
            SwitchTo((_activeIndex - 1 + _characters.Count) % _characters.Count);
        }

        public void SwitchNext()
        {
            if (_characters.Count < 2) return;
            SwitchTo((_activeIndex + 1) % _characters.Count);
        }

        public bool SwitchTo(int index)
        {
            if (index < 0 || index >= _characters.Count) return false;
            if (_activeCharacter != null && index == _activeIndex) return true;

            Vector3 sharedPosition = _activeCharacter != null
                ? _activeCharacter.transform.position
                : transform.position;

            _inputRouter.ClearTarget();
            if (_activeCharacter != null) _activeCharacter.Deactivate();

            _activeIndex = index;
            _activeCharacter = _characters[index];
            _activeCharacter.Activate(sharedPosition);
            _inputRouter.SetTarget(_activeCharacter.InputTarget);

            if (_cameraController != null)
                _cameraController.SetTarget(_activeCharacter.CameraPoint, true);

            OnActiveCharacterChanged?.Invoke(_activeCharacter);
            return true;
        }

        private void CreateCharacters()
        {
            for (int i = 0; i < _characterPrefabs.Count; i++)
            {
                PlayableCharacter prefab = _characterPrefabs[i];
                if (prefab == null) continue;

                PlayableCharacter character = Instantiate(prefab, _characterParent);
                character.Deactivate();
                _characters.Add(character);
            }
        }
    }
}
