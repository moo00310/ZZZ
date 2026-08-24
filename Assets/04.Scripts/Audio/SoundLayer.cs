using System;
using UnityEngine;
using UnityEngine.Audio;

namespace ZZZ.Audio
{
    [Serializable]
    public sealed class SoundLayer
    {
        [SerializeField, Min(0f)] private float _startDelay;
        [SerializeField] private AudioClip[] _clips = Array.Empty<AudioClip>();
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private Vector2 _pitchRange = Vector2.one;
        [Tooltip("0은 2D, 1은 월드 위치와 거리에 따라 들리는 3D 사운드입니다.")]
        [SerializeField, Range(0f, 1f)] private float _spatialBlend = 1f;
        [Tooltip("3D 사운드가 최대 음량으로 들리는 거리입니다.")]
        [SerializeField, Min(0.01f)] private float _minimumDistance = 1f;
        [Tooltip("3D 사운드 감쇠가 끝나는 최대 청취 거리입니다.")]
        [SerializeField, Min(0.01f)] private float _maximumDistance = 25f;
        [Tooltip("이 레이어를 전달할 Audio Mixer Group입니다.")]
        [SerializeField] private AudioMixerGroup _output;
        [Tooltip("재생 기준점에서 사운드 발생 위치를 이동하는 로컬 오프셋입니다.")]
        [SerializeField] private Vector3 _positionOffset;

        public SoundLayer() { }

        public float StartDelay => Mathf.Max(0f, _startDelay);

        internal void Play(in SoundPlayContext context)
        {
            AudioClip clip = SelectClip();
            if (clip == null) return;

            float minimumPitch = Mathf.Clamp(
                Mathf.Min(_pitchRange.x, _pitchRange.y), 0.01f, 3f);
            float maximumPitch = Mathf.Clamp(
                Mathf.Max(_pitchRange.x, _pitchRange.y), minimumPitch, 3f);
            var request = new AudioPlaybackRequest(
                clip,
                Mathf.Clamp01(_volume),
                UnityEngine.Random.Range(minimumPitch, maximumPitch),
                Mathf.Clamp01(_spatialBlend),
                Mathf.Max(0.01f, _minimumDistance),
                Mathf.Max(_minimumDistance, _maximumDistance),
                _output);
            AudioService.PlayAt(
                in request, context.ResolvePosition(_positionOffset));
        }

        private AudioClip SelectClip()
        {
            if (_clips == null || _clips.Length == 0) return null;

            int startIndex = UnityEngine.Random.Range(0, _clips.Length);
            for (int i = 0; i < _clips.Length; i++)
            {
                AudioClip clip = _clips[(startIndex + i) % _clips.Length];
                if (clip != null) return clip;
            }
            return null;
        }
    }

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