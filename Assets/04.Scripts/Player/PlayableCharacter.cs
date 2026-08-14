using UnityEngine;
using UnityEngine.Serialization;
using ZZZ.Player.StateMachine;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerActionController))]
    public sealed class PlayableCharacter : MonoBehaviour
    {
        [FormerlySerializedAs("_stateMachine")]
        [SerializeField] private PlayerActionController _actionController;
        [SerializeField] private Transform _cameraPoint;

        public PlayerActionController InputTarget => _actionController;
        public Transform CameraPoint => _cameraPoint;

        private void Awake()
        {
            if (_actionController == null)
                _actionController = GetComponent<PlayerActionController>();
        }

        public void Activate(Vector3 worldPosition)
        {
            transform.position = worldPosition;
            gameObject.SetActive(true);
            _actionController.ActivateCharacter();
        }

        public void Deactivate()
        {
            _actionController.DeactivateCharacter();
            gameObject.SetActive(false);
        }
    }
}
