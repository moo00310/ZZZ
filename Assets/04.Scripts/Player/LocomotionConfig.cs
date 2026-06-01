using UnityEngine;

namespace ZZZ.Player
{
    [CreateAssetMenu(menuName = "ZZZ/Locomotion Config", fileName = "LocomotionConfig")]
    public class LocomotionConfig : ScriptableObject
    {
        [Header("Movement Speed")]
        public float MoveSpeed     = 4f;
        public float SprintSpeed   = 7f;
        public float RotationSpeed = 15f;

        [Header("Animator")]
        public float AnimatorSpeed = 1f;

        [Header("Root Motion")]
        public bool UseRootMotion_WalkStart = true;
        public bool UseRootMotion_WalkEnd   = true;
        public bool UseRootMotion_RunEnd    = true;

        [Header("Preview Clips")]
        public AnimationClip WalkStartClip;
        public AnimationClip WalkEndClip;
        public AnimationClip RunEndClip;
    }
}
