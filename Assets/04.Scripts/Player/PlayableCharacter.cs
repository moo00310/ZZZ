using UnityEngine;
using ZZZ.Player.StateMachine;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerStateMachine))]
    public sealed class PlayableCharacter : MonoBehaviour
    {
        [SerializeField] private PlayerStateMachine _stateMachine;
        [SerializeField] private Transform _cameraPoint;

        public PlayerStateMachine InputTarget => _stateMachine;
        public Transform CameraPoint => _cameraPoint;

        private void Awake()
        {
            if (_stateMachine == null) _stateMachine = GetComponent<PlayerStateMachine>();
        }

        public void Activate(Vector3 worldPosition)
        {
            transform.position = worldPosition;
            gameObject.SetActive(true);
            _stateMachine.ActivateCharacter();
        }

        public void Deactivate()
        {
            _stateMachine.DeactivateCharacter();
            gameObject.SetActive(false);
        }
    }
}
