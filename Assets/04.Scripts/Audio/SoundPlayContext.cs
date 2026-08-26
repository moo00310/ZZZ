using UnityEngine;

namespace ZZZ.Audio
{
    public readonly struct SoundPlayContext
    {
        private readonly Transform _anchor;
        private readonly bool _hasWorldPose;
        private readonly Vector3 _worldPosition;
        private readonly Quaternion _worldRotation;

        private SoundPlayContext(
            Transform anchor, bool hasWorldPose,
            Vector3 worldPosition, Quaternion worldRotation)
        {
            _anchor = anchor;
            _hasWorldPose = hasWorldPose;
            _worldPosition = worldPosition;
            _worldRotation = hasWorldPose
                ? worldRotation
                : Quaternion.identity;
        }

        public bool IsValid => _anchor != null;

        internal Transform Anchor => _anchor;
        internal bool FollowsAnchor => !_hasWorldPose;

        public static SoundPlayContext ForTransform(Transform anchor) =>
            new SoundPlayContext(
                anchor, false, default, Quaternion.identity);

        public static SoundPlayContext AtWorldPose(
            Transform ownerRoot, Vector3 position, Quaternion rotation) =>
            new SoundPlayContext(ownerRoot, true, position, rotation);

        public Vector3 ResolvePosition(Vector3 localOffset)
        {
            if (_hasWorldPose)
                return _worldPosition + _worldRotation * localOffset;

            return _anchor != null
                ? _anchor.TransformPoint(localOffset)
                : localOffset;
        }
    }
}
