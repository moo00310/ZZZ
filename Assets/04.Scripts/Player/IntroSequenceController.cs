using System.Collections;
using UnityEngine;
using ZZZ.Agent;
using ZZZ.Monster;

namespace ZZZ.Player
{
    [DisallowMultipleComponent]
    public sealed class IntroSequenceController : MonoBehaviour
    {
        [Header("Startup")]
        [SerializeField] private bool _playOnStart = true;

        [Header("References")]
        [SerializeField] private SquadController _squadController;
        [SerializeField] private PlayerInputRouter _inputRouter;
        [SerializeField] private TPSCameraController _cameraController;
        [SerializeField] private MonsterRegistry _monsterRegistry;
        [SerializeField] private CanvasGroup _transitionOverlay;

        [Header("Transition")]
        [SerializeField, Min(0f)] private float _blackHoldDuration = 0.3f;
        [SerializeField, Min(0f)] private float _fadeDuration = 0.3f;
        [SerializeField, Min(0.1f)] private float _introTimeout = 15f;
        [SerializeField, Min(0f)] private float _introBlendDuration = 0.1f;

        private Coroutine _sequenceRoutine;
        private AgentRoot _introAgent;
        private bool _introLocked;

        private void Awake()
        {
            ResolveReferences();

            if (_playOnStart)
            {
                ShowOverlayImmediately();
                SetIntroLock(true);
            }
            else
            {
                HideOverlayImmediately();
            }
        }

        private void Start()
        {
            if (_playOnStart) PlayIntro();
        }

        private void OnDisable()
        {
            if (!_introLocked) return;

            if (_introAgent != null)
                _introAgent.InputTarget.ExitIntro(0f);

            SetIntroLock(false);
            HideOverlayImmediately();
            _sequenceRoutine = null;
        }

        public void PlayIntro()
        {
            if (_sequenceRoutine != null) return;

            ShowOverlayImmediately();
            SetIntroLock(true);
            _sequenceRoutine = StartCoroutine(RunIntroSequence());
        }

        public void SkipIntro()
        {
            if (_sequenceRoutine != null)
            {
                StopCoroutine(_sequenceRoutine);
                _sequenceRoutine = null;
            }

            if (_introAgent != null)
                _introAgent.InputTarget.ExitIntro(0f);

            CompleteIntro();
        }

        private IEnumerator RunIntroSequence()
        {
            yield return null;

            float initializationElapsed = 0f;
            while (_squadController != null
                && _squadController.ActiveAgent == null
                && initializationElapsed < _introTimeout)
            {
                initializationElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_squadController == null
                || _squadController.ActiveAgent == null)
            {
                Debug.LogError(
                    "IntroSequenceController could not find an active agent.",
                    this);
                CompleteIntro();
                yield break;
            }

            _introAgent = _squadController.ActiveAgent;
            AgentActionController actionController = _introAgent.InputTarget;

            if (_blackHoldDuration > 0f)
                yield return new WaitForSecondsRealtime(_blackHoldDuration);

            if (!actionController.TryPlayIntro(_introBlendDuration))
            {
                Debug.LogWarning(
                    "IntroSequenceController could not play the active agent's intro config.",
                    actionController);
                CompleteIntro();
                yield break;
            }

            yield return FadeOverlay(0f);

            float elapsed = 0f;
            while (actionController != null
                && actionController.IsPlayingIntro
                && elapsed < _introTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (actionController != null && actionController.IsPlayingIntro)
            {
                Debug.LogWarning(
                    "IntroSequenceController timed out waiting for the intro config to finish.",
                    actionController);
                actionController.ExitIntro(_introBlendDuration);
            }

            CompleteIntro();
        }

        private void CompleteIntro()
        {
            SetIntroLock(false);
            HideOverlayImmediately();
            _introAgent = null;
            _sequenceRoutine = null;
        }

        private void SetIntroLock(bool locked)
        {
            _introLocked = locked;

            if (_monsterRegistry != null)
                _monsterRegistry.SetAIEnabled(!locked);

            if (_cameraController != null)
                _cameraController.SetLookInputEnabled(!locked);

            if (_inputRouter == null) return;

            if (!locked && _squadController != null
                && _squadController.ActiveAgent != null)
            {
                _inputRouter.SetTarget(
                    _squadController.ActiveAgent.InputTarget);
            }

            _inputRouter.SetInputEnabled(!locked);
        }

        private IEnumerator FadeOverlay(float targetAlpha)
        {
            if (_transitionOverlay == null) yield break;

            float startAlpha = _transitionOverlay.alpha;
            if (_fadeDuration <= 0f)
            {
                _transitionOverlay.alpha = targetAlpha;
                _transitionOverlay.blocksRaycasts = targetAlpha > 0f;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / _fadeDuration);
                _transitionOverlay.alpha = Mathf.Lerp(
                    startAlpha, targetAlpha, t);
                yield return null;
            }

            _transitionOverlay.alpha = targetAlpha;
            _transitionOverlay.blocksRaycasts = targetAlpha > 0f;
        }

        private void ResolveReferences()
        {
            if (_squadController == null)
                _squadController = FindFirstObjectByType<SquadController>();

            if (_inputRouter == null && _squadController != null)
                _inputRouter = _squadController.GetComponent<PlayerInputRouter>();

            if (_cameraController == null && Camera.main != null)
                _cameraController =
                    Camera.main.GetComponent<TPSCameraController>();

            if (_monsterRegistry == null)
                _monsterRegistry = FindFirstObjectByType<MonsterRegistry>();
        }

        private void ShowOverlayImmediately()
        {
            if (_transitionOverlay == null) return;

            _transitionOverlay.alpha = 1f;
            _transitionOverlay.interactable = false;
            _transitionOverlay.blocksRaycasts = true;
        }

        private void HideOverlayImmediately()
        {
            if (_transitionOverlay == null) return;

            _transitionOverlay.alpha = 0f;
            _transitionOverlay.interactable = false;
            _transitionOverlay.blocksRaycasts = false;
        }
    }
}
