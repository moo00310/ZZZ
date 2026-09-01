using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace ZZZ.Agent
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentActionController))]
    [MovedFrom(true, "ZZZ.Player", "Assembly-CSharp", "PlayableCharacter")]
    public sealed class AgentRoot : MonoBehaviour
    {
        [FormerlySerializedAs("_stateMachine")]
        [SerializeField] private AgentActionController _actionController;
        [SerializeField] private Transform _cameraPoint;

        public AgentActionController InputTarget => _actionController;
        public Transform CameraPoint => _cameraPoint;

        private void Awake()
        {
            if (_actionController == null)
                _actionController = GetComponent<AgentActionController>();
        }

        public void Activate(Vector3 worldPosition)
        {
            transform.position = worldPosition;
            gameObject.SetActive(true);
            _actionController.StartActions();
        }

        public void Deactivate()
        {
            _actionController.StopActions();
            gameObject.SetActive(false);
        }
    }
}
