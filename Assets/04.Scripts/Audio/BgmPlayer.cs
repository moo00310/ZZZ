using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace ZZZ.Audio
{
    [DisallowMultipleComponent]
    public sealed class BgmPlayer : MonoBehaviour
    {
        [Header("Scene BGM Request")]
        [SerializeField] private AudioClip _sceneTrack;
        [SerializeField] private bool _playOnStart = true;
        [SerializeField] private bool _stopIfTrackMissing;
        [SerializeField] private bool _loop = true;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField, Min(0f)] private float _fadeDuration = 1f;
        [SerializeField] private AudioMixerGroup _output;

        private AudioSource _sourceA;
        private AudioSource _sourceB;
        private AudioSource _activeSource;
        private Coroutine _fadeRoutine;

        public static BgmPlayer Instance { get; private set; }

        public AudioClip CurrentTrack
        {
            get
            {
                AudioSource source = GetLoudestPlayingSource();
                return source != null ? source.clip : null;
            }
        }

        public bool IsPlaying => GetLoudestPlayingSource() != null;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Instance.ApplySceneRequest(this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureSources();
        }

        private void Start()
        {
            if (Instance != this || !_playOnStart) return;
            ApplyConfiguredTrack();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void PlaySceneTrack()
        {
            if (Instance != this) return;
            ApplyConfiguredTrack();
        }

        public void StopSceneTrack()
        {
            if (Instance != this) return;
            StopInternal(_fadeDuration);
        }

        public static bool Play(AudioClip track, float fadeDuration = -1f)
        {
            if (Instance == null || track == null) return false;
            float duration = fadeDuration >= 0f
                ? fadeDuration
                : Instance._fadeDuration;
            Instance.PlayInternal(
                track, Instance._volume, Instance._loop,
                Instance._output, duration);
            return true;
        }

        public static void Stop(float fadeDuration = -1f)
        {
            if (Instance == null) return;
            float duration = fadeDuration >= 0f
                ? fadeDuration
                : Instance._fadeDuration;
            Instance.StopInternal(duration);
        }

        private void ApplySceneRequest(BgmPlayer request)
        {
            _sceneTrack = request._sceneTrack;
            _playOnStart = request._playOnStart;
            _stopIfTrackMissing = request._stopIfTrackMissing;
            _loop = request._loop;
            _volume = request._volume;
            _fadeDuration = request._fadeDuration;
            _output = request._output;

            if (_playOnStart) ApplyConfiguredTrack();
        }

        private void ApplyConfiguredTrack()
        {
            if (_sceneTrack != null)
            {
                PlayInternal(
                    _sceneTrack, _volume, _loop,
                    _output, _fadeDuration);
                return;
            }

            if (_stopIfTrackMissing)
                StopInternal(_fadeDuration);
        }

        private void PlayInternal(
            AudioClip track, float volume, bool loop,
            AudioMixerGroup output, float fadeDuration)
        {
            if (track == null) return;
            EnsureSources();
            CancelFade();

            float targetVolume = Mathf.Clamp01(volume);
            float duration = Mathf.Max(0f, fadeDuration);
            AudioSource sameTrack = FindPlayingTrack(track);
            if (sameTrack != null)
            {
                AudioSource other = OtherSource(sameTrack);
                if (other != null) ResetSource(other);
                ConfigureSource(sameTrack, loop, output);
                _activeSource = sameTrack;
                if (duration <= 0f)
                {
                    sameTrack.volume = targetVolume;
                    return;
                }

                _fadeRoutine = StartCoroutine(
                    FadeVolume(sameTrack, targetVolume, duration));
                return;
            }

            AudioSource from = GetLoudestPlayingSource();
            AudioSource to = from != null
                ? OtherSource(from)
                : InactiveSource();
            ResetSource(to);
            ConfigureSource(to, loop, output);
            to.clip = track;
            to.volume = duration > 0f ? 0f : targetVolume;
            to.Play();
            _activeSource = to;

            if (duration <= 0f)
            {
                if (from != null) ResetSource(from);
                return;
            }

            _fadeRoutine = StartCoroutine(
                CrossFade(from, to, targetVolume, duration));
        }

        private void StopInternal(float fadeDuration)
        {
            EnsureSources();
            CancelFade();

            float duration = Mathf.Max(0f, fadeDuration);
            if (duration <= 0f)
            {
                ResetSource(_sourceA);
                ResetSource(_sourceB);
                _activeSource = null;
                return;
            }

            _fadeRoutine = StartCoroutine(FadeOutAll(duration));
        }

        private IEnumerator CrossFade(
            AudioSource from, AudioSource to,
            float targetVolume, float duration)
        {
            float fromStart = from != null ? from.volume : 0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                if (from != null)
                    from.volume = Mathf.Lerp(fromStart, 0f, progress);
                to.volume = Mathf.Lerp(0f, targetVolume, progress);
                yield return null;
            }

            if (from != null) ResetSource(from);
            to.volume = targetVolume;
            _fadeRoutine = null;
        }

        private IEnumerator FadeVolume(
            AudioSource source, float targetVolume, float duration)
        {
            float startVolume = source.volume;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                source.volume = Mathf.Lerp(
                    startVolume, targetVolume, progress);
                yield return null;
            }

            source.volume = targetVolume;
            _fadeRoutine = null;
        }

        private IEnumerator FadeOutAll(float duration)
        {
            float volumeA = _sourceA.volume;
            float volumeB = _sourceB.volume;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                _sourceA.volume = Mathf.Lerp(volumeA, 0f, progress);
                _sourceB.volume = Mathf.Lerp(volumeB, 0f, progress);
                yield return null;
            }

            ResetSource(_sourceA);
            ResetSource(_sourceB);
            _activeSource = null;
            _fadeRoutine = null;
        }

        private void EnsureSources()
        {
            if (_sourceA != null && _sourceB != null) return;

            AudioSource[] sources = GetComponents<AudioSource>();
            _sourceA = sources.Length > 0
                ? sources[0]
                : gameObject.AddComponent<AudioSource>();
            _sourceB = sources.Length > 1
                ? sources[1]
                : gameObject.AddComponent<AudioSource>();
            InitializeSource(_sourceA);
            InitializeSource(_sourceB);
        }

        private static void InitializeSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
        }

        private static void ConfigureSource(
            AudioSource source, bool loop, AudioMixerGroup output)
        {
            source.loop = loop;
            source.outputAudioMixerGroup = output;
        }

        private AudioSource FindPlayingTrack(AudioClip track)
        {
            if (_sourceA.isPlaying && _sourceA.clip == track)
                return _sourceA;
            if (_sourceB.isPlaying && _sourceB.clip == track)
                return _sourceB;
            return null;
        }

        private AudioSource GetLoudestPlayingSource()
        {
            AudioSource sourceA = _sourceA != null
                && _sourceA.isPlaying ? _sourceA : null;
            AudioSource sourceB = _sourceB != null
                && _sourceB.isPlaying ? _sourceB : null;
            if (sourceA == null) return sourceB;
            if (sourceB == null) return sourceA;
            return sourceA.volume >= sourceB.volume
                ? sourceA
                : sourceB;
        }

        private AudioSource InactiveSource()
        {
            if (_activeSource == _sourceA) return _sourceB;
            return _sourceA;
        }

        private AudioSource OtherSource(AudioSource source)
        {
            return source == _sourceA ? _sourceB : _sourceA;
        }

        private static void ResetSource(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            source.clip = null;
            source.volume = 0f;
        }

        private void CancelFade()
        {
            if (_fadeRoutine == null) return;
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }
    }
}
