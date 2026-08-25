using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace ZZZ.Audio
{
    public readonly struct AudioPlaybackRequest
    {
        public AudioClip Clip { get; }
        public float Volume { get; }
        public float Pitch { get; }
        public float SpatialBlend { get; }
        public float MinimumDistance { get; }
        public float MaximumDistance { get; }
        public AudioMixerGroup Output { get; }

        public AudioPlaybackRequest(
            AudioClip clip, float volume, float pitch, float spatialBlend,
            float minimumDistance, float maximumDistance,
            AudioMixerGroup output)
        {
            Clip = clip;
            Volume = Mathf.Clamp01(volume);
            Pitch = Mathf.Clamp(pitch, 0.01f, 3f);
            SpatialBlend = Mathf.Clamp01(spatialBlend);
            MinimumDistance = Mathf.Max(0.01f, minimumDistance);
            MaximumDistance = Mathf.Max(MinimumDistance, maximumDistance);
            Output = output;
        }
    }

    public static class AudioService
    {
        private static AudioServiceRunner s_runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            s_runner = null;
        }

        public static void Play(
            CompositeSound sound, SoundPlayContext context)
        {
            if (sound == null || !context.IsValid) return;
            sound.Play(in context);
        }

        public static void PlayAfterAnimation(
            CompositeSound sound, SoundPlayContext context)
        {
            if (sound == null || !context.IsValid) return;

            GetRunner().EnqueueLateUpdate(() =>
            {
                if (!context.IsValid) return;
                sound.Play(in context);
            });
        }

        public static void PlayAt(
            CompositeSound sound, Vector3 position, Quaternion rotation,
            Transform ownerRoot)
        {
            Play(
                sound,
                SoundPlayContext.AtWorldPose(
                    ownerRoot, position, rotation));
        }

        public static void PlayAt(
            in AudioPlaybackRequest request, Vector3 position)
        {
            if (request.Clip == null) return;
            GetRunner().PlayAt(in request, position);
        }

        private static AudioServiceRunner GetRunner()
        {
            if (s_runner != null) return s_runner;

            var gameObject = new GameObject("AudioService");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            s_runner = gameObject.AddComponent<AudioServiceRunner>();
            return s_runner;
        }
    }

    internal sealed class AudioServiceRunner : MonoBehaviour
    {
        private const int MAX_VOICES = 32;

        private readonly List<Voice> _voices = new List<Voice>();
        private readonly Queue<Action> _lateUpdateActions = new Queue<Action>();

        private sealed class Voice
        {
            public AudioSource Source;
            public float StartedAt;
        }

        internal void PlayAt(
            in AudioPlaybackRequest request, Vector3 position)
        {
            Voice voice = GetVoice();
            AudioSource source = voice.Source;
            source.transform.position = position;
            source.Stop();
            source.clip = request.Clip;
            source.outputAudioMixerGroup = request.Output;
            source.volume = request.Volume;
            source.pitch = request.Pitch;
            source.spatialBlend = request.SpatialBlend;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = request.MinimumDistance;
            source.maxDistance = request.MaximumDistance;
            source.dopplerLevel = 0f;
            source.loop = false;
            voice.StartedAt = Time.realtimeSinceStartup;
            source.Play();
        }

        internal void EnqueueLateUpdate(Action action)
        {
            if (action != null) _lateUpdateActions.Enqueue(action);
        }

        private void LateUpdate()
        {
            while (_lateUpdateActions.Count > 0)
                _lateUpdateActions.Dequeue().Invoke();
        }

        private Voice GetVoice()
        {
            for (int i = 0; i < _voices.Count; i++)
                if (!_voices[i].Source.isPlaying)
                    return _voices[i];

            if (_voices.Count < MAX_VOICES)
            {
                var gameObject = new GameObject($"Voice_{_voices.Count + 1}");
                gameObject.transform.SetParent(transform, false);
                var added = new Voice
                {
                    Source = gameObject.AddComponent<AudioSource>(),
                    StartedAt = 0f,
                };
                _voices.Add(added);
                return added;
            }

            Voice oldest = _voices[0];
            for (int i = 1; i < _voices.Count; i++)
                if (_voices[i].StartedAt < oldest.StartedAt)
                    oldest = _voices[i];
            return oldest;
        }
    }
}
