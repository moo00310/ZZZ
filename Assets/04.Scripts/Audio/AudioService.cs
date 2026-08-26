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
            sound.Play(in context, false, null, 0f, 0f, 0f);
        }

        public static void PlayAfterAnimation(
            CompositeSound sound, SoundPlayContext context)
        {
            if (sound == null || !context.IsValid) return;

            GetRunner().EnqueueLateUpdate(() =>
            {
                if (!context.IsValid) return;
                sound.Play(in context, false, null, 0f, 0f, 0f);
            });
        }

        public static AudioHandle PlayAfterAnimation(
            CompositeSound sound, SoundPlayContext context, bool loop,
            float fadeInDuration = 0f, float fadeOutDuration = 0f,
            float duration = 0f)
        {
            bool needsHandle = loop
                || fadeInDuration > 0f
                || fadeOutDuration > 0f
                || duration > 0f;
            if (!needsHandle)
            {
                PlayAfterAnimation(sound, context);
                return null;
            }
            if (sound == null || !context.IsValid) return null;

            var handle = new AudioHandle();
            GetRunner().EnqueueLateUpdate(() =>
            {
                if (handle.IsStopped) return;
                if (!context.IsValid)
                {
                    handle.Stop();
                    return;
                }
                sound.Play(
                    in context, loop, handle,
                    fadeInDuration, fadeOutDuration, duration);
            });
            return handle;
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

        internal static void PlayAt(
            in AudioPlaybackRequest request, in SoundPlayContext context,
            Vector3 positionOffset, bool loop, AudioHandle handle,
            float fadeInDuration, float fadeOutDuration, float duration)
        {
            if (request.Clip == null) return;
            GetRunner().PlayAt(
                in request, in context, positionOffset, loop, handle,
                fadeInDuration, fadeOutDuration, duration);
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
        private const int MAX_VOICES = 64;
        private const float CLIP_LOAD_TIMEOUT = 5f;
        private const float VOICE_LIMIT_WARNING_INTERVAL = 1f;

        private readonly List<Voice> _voices = new List<Voice>();
        private readonly Queue<Action> _lateUpdateActions = new Queue<Action>();
        private readonly List<PendingPlayback> _pendingPlaybacks =
            new List<PendingPlayback>();
        private float _lastVoiceLimitWarningAt = float.NegativeInfinity;

        private sealed class Voice
        {
            public AudioSource Source;
            public float StartedAt;
            public int BindingVersion;
            public bool Loop;
            public bool FollowAnchor;
            public Transform Anchor;
            public Vector3 PositionOffset;
            public AudioHandle Handle;
            public float TargetVolume;
            public float FadeInDuration;
            public float FadeInElapsed;
            public float FadeOutDuration;
            public float FadeOutElapsed;
            public float FadeOutStartVolume;
            public bool FadingOut;
            public float Duration;
            public float Elapsed;
        }

        private sealed class PendingPlayback
        {
            public AudioPlaybackRequest Request;
            public bool HasContext;
            public SoundPlayContext Context;
            public Vector3 PositionOffset;
            public Vector3 FixedPosition;
            public bool Loop;
            public AudioHandle Handle;
            public float FadeInDuration;
            public float FadeOutDuration;
            public float Duration;
            public float QueuedAt;
        }

        internal void PlayAt(
            in AudioPlaybackRequest request, Vector3 position)
        {
            var pending = new PendingPlayback
            {
                Request = request,
                FixedPosition = position,
            };
            if (!PrepareClipOrQueue(pending)) return;
            PlayReady(in request, position);
        }

        internal void PlayAt(
            in AudioPlaybackRequest request, in SoundPlayContext context,
            Vector3 positionOffset, bool loop, AudioHandle handle,
            float fadeInDuration, float fadeOutDuration, float duration)
        {
            if (handle != null && handle.IsStopped) return;

            var pending = new PendingPlayback
            {
                Request = request,
                HasContext = true,
                Context = context,
                PositionOffset = positionOffset,
                Loop = loop,
                Handle = handle,
                FadeInDuration = Mathf.Max(0f, fadeInDuration),
                FadeOutDuration = Mathf.Max(0f, fadeOutDuration),
                Duration = Mathf.Max(0f, duration),
            };
            if (!PrepareClipOrQueue(pending)) return;
            PlayReady(pending);
        }

        private void PlayReady(
            in AudioPlaybackRequest request, Vector3 position)
        {
            int voiceIndex = GetVoiceIndex();
            Voice voice = _voices[voiceIndex];
            PrepareVoice(voiceIndex, voice);
            AudioSource source = voice.Source;
            source.transform.position = position;
            ApplyRequest(voice, in request, false, 0f, 0f, 0f);
        }

        private void PlayReady(PendingPlayback pending)
        {
            if (pending.Handle != null && pending.Handle.IsStopped) return;
            if (!pending.Context.IsValid)
            {
                pending.Handle?.Stop();
                return;
            }

            int voiceIndex = GetVoiceIndex();
            Voice voice = _voices[voiceIndex];
            PrepareVoice(voiceIndex, voice);
            voice.Source.transform.position =
                pending.Context.ResolvePosition(pending.PositionOffset);
            voice.FollowAnchor =
                pending.Handle != null && pending.Context.FollowsAnchor;
            voice.Anchor = voice.FollowAnchor
                ? pending.Context.Anchor
                : null;
            voice.PositionOffset = pending.PositionOffset;
            voice.Handle = pending.Handle;
            ApplyRequest(
                voice, in pending.Request, pending.Loop,
                pending.FadeInDuration, pending.FadeOutDuration,
                pending.Duration);
            if (pending.Handle != null)
                pending.Handle.Bind(
                    this, voiceIndex, voice.BindingVersion);
        }

        private static void ApplyRequest(
            Voice voice, in AudioPlaybackRequest request, bool loop,
            float fadeInDuration, float fadeOutDuration, float duration)
        {
            AudioSource source = voice.Source;
            source.clip = request.Clip;
            source.outputAudioMixerGroup = request.Output;
            voice.TargetVolume = request.Volume;
            voice.FadeInDuration = Mathf.Max(0f, fadeInDuration);
            voice.FadeInElapsed = 0f;
            voice.FadeOutDuration = Mathf.Max(0f, fadeOutDuration);
            voice.FadeOutElapsed = 0f;
            voice.FadeOutStartVolume = 0f;
            voice.FadingOut = false;
            voice.Duration = Mathf.Max(0f, duration);
            if (voice.Duration > 0f)
                voice.FadeOutDuration = Mathf.Min(
                    voice.FadeOutDuration, voice.Duration);
            voice.Elapsed = 0f;
            source.volume = voice.FadeInDuration > 0f
                ? 0f
                : voice.TargetVolume;
            source.pitch = request.Pitch;
            source.spatialBlend = request.SpatialBlend;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = request.MinimumDistance;
            source.maxDistance = request.MaximumDistance;
            source.dopplerLevel = 0f;
            source.loop = loop;
            source.priority = loop ? 64 : 128;
            voice.StartedAt = Time.realtimeSinceStartup;
            voice.Loop = loop;
            source.Play();
        }

        private bool PrepareClipOrQueue(PendingPlayback pending)
        {
            AudioClip clip = pending.Request.Clip;
            if (clip == null)
            {
                pending.Handle?.Stop();
                return false;
            }
            if (clip.loadState == AudioDataLoadState.Loaded) return true;
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                FailPendingPlayback(pending, "audio data load failed");
                return false;
            }

            if (clip.loadState == AudioDataLoadState.Unloaded
                && !clip.LoadAudioData())
            {
                FailPendingPlayback(pending, "audio data load could not start");
                return false;
            }
            if (clip.loadState == AudioDataLoadState.Loaded) return true;

            pending.QueuedAt = Time.realtimeSinceStartup;
            _pendingPlaybacks.Add(pending);
            return false;
        }

        internal void EnqueueLateUpdate(Action action)
        {
            if (action != null) _lateUpdateActions.Enqueue(action);
        }

        private void LateUpdate()
        {
            while (_lateUpdateActions.Count > 0)
                _lateUpdateActions.Dequeue().Invoke();

            ProcessPendingPlaybacks();
            float deltaTime = Time.unscaledDeltaTime;
            for (int i = 0; i < _voices.Count; i++)
            {
                Voice voice = _voices[i];
                if (voice.FadingOut)
                {
                    if (!voice.Source.isPlaying)
                    {
                        StopVoiceImmediate(i, voice);
                        continue;
                    }

                    voice.FadeOutElapsed += deltaTime;
                    float progress = Mathf.Clamp01(
                        voice.FadeOutElapsed / voice.FadeOutDuration);
                    voice.Source.volume = Mathf.Lerp(
                        voice.FadeOutStartVolume, 0f, progress);
                    if (progress >= 1f)
                        StopVoiceImmediate(i, voice);
                    continue;
                }

                if (voice.FadeInDuration > 0f
                    && voice.Source.isPlaying)
                {
                    voice.FadeInElapsed += deltaTime;
                    float progress = Mathf.Clamp01(
                        voice.FadeInElapsed / voice.FadeInDuration);
                    voice.Source.volume =
                        voice.TargetVolume * progress;
                    if (progress >= 1f) voice.FadeInDuration = 0f;
                }

                if (!voice.Source.isPlaying)
                {
                    StopVoiceImmediate(i, voice);
                    continue;
                }
                voice.Elapsed += deltaTime;
                float fadeOutStart = Mathf.Max(
                    0f, voice.Duration - voice.FadeOutDuration);
                if (voice.Duration > 0f
                    && voice.Elapsed >= fadeOutStart)
                {
                    StopVoice(i, voice.BindingVersion);
                    continue;
                }
                if (!voice.FollowAnchor) continue;
                if (voice.Anchor == null)
                {
                    StopVoice(i, voice.BindingVersion);
                    continue;
                }
                voice.Source.transform.position =
                    voice.Anchor.TransformPoint(voice.PositionOffset);
            }
        }

        internal void StopVoice(int voiceIndex, int bindingVersion)
        {
            if (voiceIndex < 0 || voiceIndex >= _voices.Count) return;
            Voice voice = _voices[voiceIndex];
            if (voice.BindingVersion != bindingVersion) return;

            AudioHandle handle = voice.Handle;
            voice.Handle = null;
            voice.FollowAnchor = false;
            voice.Anchor = null;
            handle?.Detach(this, voiceIndex, bindingVersion);
            if (voice.FadeOutDuration > 0f
                && voice.Source.isPlaying && !voice.FadingOut)
            {
                voice.FadingOut = true;
                voice.FadeOutElapsed = 0f;
                voice.FadeOutStartVolume = voice.Source.volume;
                return;
            }

            StopVoiceImmediate(voiceIndex, voice);
        }

        private void ProcessPendingPlaybacks()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = _pendingPlaybacks.Count - 1; i >= 0; i--)
            {
                PendingPlayback pending = _pendingPlaybacks[i];
                if (pending.Handle != null && pending.Handle.IsStopped)
                {
                    _pendingPlaybacks.RemoveAt(i);
                    continue;
                }
                if (pending.HasContext && !pending.Context.IsValid)
                {
                    pending.Handle?.Stop();
                    _pendingPlaybacks.RemoveAt(i);
                    continue;
                }

                AudioDataLoadState loadState =
                    pending.Request.Clip.loadState;
                if (loadState == AudioDataLoadState.Loaded)
                {
                    _pendingPlaybacks.RemoveAt(i);
                    if (pending.HasContext) PlayReady(pending);
                    else PlayReady(
                        in pending.Request, pending.FixedPosition);
                    continue;
                }

                if (loadState == AudioDataLoadState.Failed
                    || now - pending.QueuedAt >= CLIP_LOAD_TIMEOUT)
                {
                    string reason = loadState == AudioDataLoadState.Failed
                        ? "audio data load failed"
                        : $"audio data did not load within {CLIP_LOAD_TIMEOUT:F0}s";
                    FailPendingPlayback(pending, reason);
                    _pendingPlaybacks.RemoveAt(i);
                }
            }
        }

        private static void FailPendingPlayback(
            PendingPlayback pending, string reason)
        {
            string clipName = pending.Request.Clip != null
                ? pending.Request.Clip.name
                : "(missing clip)";
            Debug.LogWarning(
                $"AudioService: '{clipName}' {reason}. Playback was skipped.");
            pending.Handle?.Stop();
        }

        private void StopVoiceImmediate(int voiceIndex, Voice voice)
        {
            int bindingVersion = voice.BindingVersion;
            AudioHandle handle = voice.Handle;
            voice.Source.Stop();
            voice.Source.loop = false;
            voice.Source.clip = null;
            voice.Source.volume = 1f;
            voice.Source.priority = 128;
            voice.Loop = false;
            voice.FollowAnchor = false;
            voice.Anchor = null;
            voice.Handle = null;
            voice.TargetVolume = 0f;
            voice.FadeInDuration = 0f;
            voice.FadeInElapsed = 0f;
            voice.FadeOutDuration = 0f;
            voice.FadeOutElapsed = 0f;
            voice.FadeOutStartVolume = 0f;
            voice.FadingOut = false;
            voice.Duration = 0f;
            voice.Elapsed = 0f;
            handle?.Detach(this, voiceIndex, bindingVersion);
        }

        private int GetVoiceIndex()
        {
            for (int i = 0; i < _voices.Count; i++)
                if (!_voices[i].Source.isPlaying)
                    return i;

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
                return _voices.Count - 1;
            }

            int oldestIndex = -1;
            for (int i = 0; i < _voices.Count; i++)
            {
                if (_voices[i].Loop) continue;
                if (oldestIndex < 0
                    || _voices[i].StartedAt < _voices[oldestIndex].StartedAt)
                    oldestIndex = i;
            }
            if (oldestIndex >= 0)
            {
                WarnVoiceLimit(_voices[oldestIndex]);
                return oldestIndex;
            }

            oldestIndex = 0;
            for (int i = 1; i < _voices.Count; i++)
                if (_voices[i].StartedAt < _voices[oldestIndex].StartedAt)
                    oldestIndex = i;
            WarnVoiceLimit(_voices[oldestIndex]);
            return oldestIndex;
        }

        private void PrepareVoice(int voiceIndex, Voice voice)
        {
            StopVoiceImmediate(voiceIndex, voice);
            unchecked { voice.BindingVersion++; }
        }

        private void WarnVoiceLimit(Voice interruptedVoice)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastVoiceLimitWarningAt
                < VOICE_LIMIT_WARNING_INTERVAL) return;

            _lastVoiceLimitWarningAt = now;
            string clipName = interruptedVoice.Source.clip != null
                ? interruptedVoice.Source.clip.name
                : "(unknown)";
            Debug.LogWarning(
                $"AudioService: voice limit {MAX_VOICES} reached. "
                + $"Interrupting '{clipName}'.");
        }
    }
}
