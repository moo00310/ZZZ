using UnityEngine;

namespace ZZZ.Player
{
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimatorController : MonoBehaviour
    {
        [SerializeField] private float _dampTime = 0.1f;

        private Animator         _animator;
        private PlayerController _controller;

        private static readonly int AnimHashSpeed     = Animator.StringToHash("Speed");
        private static readonly int AnimHashIsSprinting = Animator.StringToHash("IsSprinting");

        private void Awake()
        {
            _animator   = GetComponent<Animator>();
            _controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            _animator.SetFloat(AnimHashSpeed, _controller.CurrentSpeed, _dampTime, Time.deltaTime);
            _animator.SetBool(AnimHashIsSprinting, _controller.IsSprinting);
        }
    }
}
