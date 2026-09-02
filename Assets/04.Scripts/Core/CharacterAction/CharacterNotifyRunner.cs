using System.Collections.Generic;
using UnityEngine;
using ZZZ.Audio;
using ZZZ.Combat;
using ZZZ.Effects;

namespace ZZZ
{
    // Section Notify의 발동 시점과 실행 중 Handle의 생명주기를 관리한다.
    // 실제 Effect, Sound, Hit 실행은 각 Service에 위임한다.
    internal sealed class CharacterNotifyRunner
    {
        private readonly CharacterActionContext _context;
        private readonly List<EffectHandle> _carriedEffects =
            new List<EffectHandle>();
        private readonly List<AudioHandle> _carriedSounds =
            new List<AudioHandle>();
        private readonly List<PendingNextEffect> _pendingNextEffects =
            new List<PendingNextEffect>();
        private readonly EffectBindingScope _effectBindings =
            new EffectBindingScope();

        private bool _showHitGizmos;
        private float _hitGizmoDuration;
        private bool[] _notifyFired;
        private EffectHandle[] _notifyActive;
        private HitHandle[] _hitActive;
        private bool[] _hitSyncPending;
        private EffectTransitionMode[] _notifyTransitionModes;
        private string[] _notifyNextSections;
        private AudioHandle[] _soundActive;
        private string[] _soundNextSections;

        private sealed class PendingNextEffect
        {
            public CompositeEffect Effect;
            public HitData Hit;
            public string NextSection;
            public float NormalizedTime;
        }

        public CharacterNotifyRunner(CharacterActionContext context,
            bool showHitGizmos, float hitGizmoDuration)
        {
            _context = context;
            SetHitDebug(showHitGizmos, hitGizmoDuration);
        }

        public void SetHitDebug(bool showHitGizmos, float hitGizmoDuration)
        {
            _showHitGizmos = showHitGizmos;
            _hitGizmoDuration = Mathf.Max(0f, hitGizmoDuration);
        }

        public void PrepareForSection(string destinationSection,
            bool sameSectionReentry)
        {
            StopTrackedHits();
            if (!sameSectionReentry)
            {
                StopTrackedEffects(true, destinationSection);
                StopTrackedSounds(true, destinationSection);
            }
            PlayPendingNextEffects(destinationSection, sameSectionReentry);
        }

        public void EnterSection(TrackClip clip, float startOffset,
            bool sameSectionReentry)
        {
            bool preserveNotifyState =
                CanPreserveNotifyState(clip, sameSectionReentry);
            if (!preserveNotifyState)
                InitializeNotifyPlaybackState(clip.Notifies.Count);

            _hitActive = new HitHandle[clip.Notifies.Count];
            _hitSyncPending = new bool[clip.Notifies.Count];

            if (preserveNotifyState) ResetReplayableNotifyState(clip);

            CacheNotifyTransitionState(clip);
            MarkNotifiesBeforeOffsetAsFired(clip, startOffset);
        }

        public void ClearSectionStateForMissingClip()
        {
            _notifyFired = null;
            _notifyActive = null;
            _soundActive = null;
            _hitActive = null;
            _notifyTransitionModes = null;
            _notifyNextSections = null;
            _soundNextSections = null;
        }

        public void Tick(TrackClip clip, float previousNormalizedTime,
            float normalizedTime, float deltaTime)
        {
            if (_notifyFired == null) return;

            ResetLoopingSoundNotifiesIfNeeded(
                clip, previousNormalizedTime, normalizedTime);

            float currentTime = clip.IsLooping
                ? Mathf.Repeat(normalizedTime, 1f)
                : normalizedTime;
            for (int i = 0;
                i < clip.Notifies.Count && i < _notifyFired.Length;
                i++)
            {
                TrackNotify notify = clip.Notifies[i];

                if (ShouldStartNotify(notify, i, currentTime))
                {
                    _notifyFired[i] = true;
                    if (notify.Payload is HitNotifyPayload hitPayload)
                    {
                        StartHitNotify(clip, notify, hitPayload, i);
                        continue;
                    }
                    if (notify.Payload is SoundNotifyPayload soundPayload)
                    {
                        StartSoundNotify(soundPayload, i);
                        continue;
                    }

                    StartDispatchedNotify(clip, notify, i);
                }

                UpdatePendingSynchronizedHit(clip, notify, i);
                UpdateActiveHit(notify, i, currentTime, deltaTime);
                StopCompletedInterval(notify, i, currentTime);
            }
        }

        public void StopPlayback()
        {
            StopTrackedEffects(false);
            StopTrackedSounds(false);
            StopTrackedHits();
            _pendingNextEffects.Clear();
            _effectBindings.Clear();
        }

        public void Exit()
        {
            StopPlayback();
            _notifyFired = null;
            _hitActive = null;
            _hitSyncPending = null;
            _notifyTransitionModes = null;
            _notifyNextSections = null;
            _soundActive = null;
            _soundNextSections = null;
        }

        private bool CanPreserveNotifyState(
            TrackClip clip, bool sameSectionReentry)
        {
            return sameSectionReentry
                && _notifyFired != null
                && _notifyFired.Length == clip.Notifies.Count
                && _notifyActive != null
                && _notifyActive.Length == clip.Notifies.Count
                && _soundActive != null
                && _soundActive.Length == clip.Notifies.Count;
        }

        private void InitializeNotifyPlaybackState(int notifyCount)
        {
            _notifyFired = new bool[notifyCount];
            _notifyActive = new EffectHandle[notifyCount];
            _soundActive = new AudioHandle[notifyCount];
        }

        private void ResetReplayableNotifyState(TrackClip clip)
        {
            for (int i = 0; i < clip.Notifies.Count; i++)
            {
                TrackNotify notify = clip.Notifies[i];
                bool preserveNotify =
                    notify.Payload is EffectNotifyPayload effectPayload
                    && (effectPayload.TransitionMode == EffectTransitionMode.Next
                        || effectPayload.TransitionMode == EffectTransitionMode.Stop
                        && _notifyActive[i] != null)
                    || (notify.Payload is SoundNotifyPayload soundPayload
                        && soundPayload.Loop
                        && _soundActive[i] != null
                        && !_soundActive[i].IsStopped);
                if (preserveNotify) continue;

                _notifyFired[i] = false;
                _notifyActive[i] = null;
                _soundActive[i] = null;
            }
        }

        private void CacheNotifyTransitionState(TrackClip clip)
        {
            int notifyCount = clip.Notifies.Count;
            _notifyTransitionModes = new EffectTransitionMode[notifyCount];
            _notifyNextSections = new string[notifyCount];
            _soundNextSections = new string[notifyCount];
            for (int i = 0; i < notifyCount; i++)
            {
                _notifyTransitionModes[i] = clip.Notifies[i].TransitionMode;
                _notifyNextSections[i] = clip.Notifies[i].NextSection;
                _soundNextSections[i] =
                    clip.Notifies[i].Payload is SoundNotifyPayload soundPayload
                        ? soundPayload.NextSection
                        : "";
            }
        }

        private void MarkNotifiesBeforeOffsetAsFired(
            TrackClip clip, float startOffset)
        {
            // 중간 진입 시 이전 Notify를 다시 실행하지 않는다.
            if (startOffset <= 0f) return;

            for (int i = 0; i < clip.Notifies.Count; i++)
                if (clip.Notifies[i].NormalizedTime < startOffset)
                    _notifyFired[i] = true;
        }

        private void ResetLoopingSoundNotifiesIfNeeded(
            TrackClip clip, float previousNormalizedTime, float normalizedTime)
        {
            if (clip.IsLooping
                && Mathf.FloorToInt(normalizedTime)
                > Mathf.FloorToInt(previousNormalizedTime))
                ResetLoopingSoundNotifies(clip);
        }

        private bool ShouldStartNotify(
            TrackNotify notify, int index, float normalizedTime)
            => !_notifyFired[index] && normalizedTime >= notify.NormalizedTime;

        private void StartHitNotify(
            TrackClip clip, TrackNotify notify,
            HitNotifyPayload payload, int index)
        {
            bool parryWarning = payload.Action == HitNotifyAction.ParryWarning;
            if (!parryWarning && payload.SyncWithEffect)
            {
                _hitSyncPending[index] = !TryAttachSynchronizedHit(
                    clip, notify, payload.Hit);
                return;
            }

            var hitContext = new HitExecutionContext(
                _context.Transform, null, _effectBindings,
                _showHitGizmos, _hitGizmoDuration);
            if (notify.IsInterval || payload.Hit.Origin == HitOrigin.Effect)
                _hitActive[index] = parryWarning
                    ? HitService.BeginParryWarning(
                        payload.Hit, hitContext, payload.WarningDuration)
                    : HitService.Begin(payload.Hit, hitContext);
            else if (parryWarning)
                HitService.ExecuteParryWarning(
                    payload.Hit, hitContext, payload.WarningDuration);
            else
                HitService.Execute(payload.Hit, hitContext);
        }

        private void StartSoundNotify(SoundNotifyPayload payload, int index)
        {
            if (payload.Sound == null) return;

            SoundFadeModule fadeModule = payload.FindModule<SoundFadeModule>();
            SoundDurationModule durationModule =
                payload.FindModule<SoundDurationModule>();
            _soundActive[index] = AudioService.PlayAfterAnimation(
                payload.Sound,
                SoundPlayContext.ForTransform(_context.Transform),
                payload.Loop,
                fadeModule != null ? fadeModule.FadeInDuration : 0f,
                fadeModule != null ? fadeModule.FadeOutDuration : 0f,
                durationModule != null ? durationModule.Duration : 0f);
        }

        private void StartDispatchedNotify(
            TrackClip clip, TrackNotify notify, int index)
        {
            EffectHandle handle = notify.Payload is EffectNotifyPayload
                && notify.TransitionMode == EffectTransitionMode.Next
                ? QueueNextEffect(clip, notify)
                : DispatchNotify(notify);
            if (notify.Payload is EffectNotifyPayload && handle != null)
                _notifyActive[index] = handle;
        }

        private void UpdatePendingSynchronizedHit(
            TrackClip clip, TrackNotify notify, int index)
        {
            if (_hitSyncPending == null || !_hitSyncPending[index]
                || !(notify.Payload is HitNotifyPayload payload)) return;

            _hitSyncPending[index] = !TryAttachSynchronizedHit(
                clip, notify, payload.Hit);
        }

        private void UpdateActiveHit(
            TrackNotify notify, int index,
            float normalizedTime, float deltaTime)
        {
            if (_hitActive == null || _hitActive[index] == null) return;

            float duration = notify.EndNormalizedTime - notify.NormalizedTime;
            float progress = duration > 0f
                ? Mathf.InverseLerp(
                    notify.NormalizedTime,
                    notify.EndNormalizedTime,
                    normalizedTime)
                : 1f;
            _hitActive[index].Tick(deltaTime, progress);
            if (notify.IsInterval || !_hitActive[index].HasSampled) return;

            _hitActive[index].Stop();
            _hitActive[index] = null;
        }

        private void StopCompletedInterval(
            TrackNotify notify, int index, float normalizedTime)
        {
            if (!notify.IsInterval
                || normalizedTime < notify.EndNormalizedTime) return;

            if (_notifyActive[index] != null)
            {
                _notifyActive[index].Stop();
                _notifyActive[index] = null;
            }
            if (_hitActive == null || _hitActive[index] == null) return;

            _hitActive[index].Stop();
            _hitActive[index] = null;
        }

        private void ResetLoopingSoundNotifies(TrackClip clip)
        {
            int count = Mathf.Min(clip.Notifies.Count, _notifyFired.Length);
            for (int i = 0; i < count; i++)
            {
                if (!(clip.Notifies[i].Payload
                    is SoundNotifyPayload soundPayload)) continue;

                AudioHandle handle = _soundActive != null
                    && i < _soundActive.Length
                    ? _soundActive[i]
                    : null;
                bool keepPlayingLoop = soundPayload.Loop
                    && handle != null
                    && !handle.IsStopped;
                if (!keepPlayingLoop) _notifyFired[i] = false;
            }
        }

        private bool TryAttachHitToEffect(HitData hit)
        {
            if (hit == null || hit.Origin != HitOrigin.Effect) return false;
            return _effectBindings.TryAttachHit(
                hit.EffectKey, hit, _context.Transform,
                _showHitGizmos, _hitGizmoDuration);
        }

        private bool TryAttachSynchronizedHit(
            TrackClip clip, TrackNotify hitNotify, HitData hit)
        {
            if (hit == null || hit.Origin != HitOrigin.Effect) return false;
            if (TryAssignHitToPendingNextEffect(
                hitNotify.NormalizedTime, hit)) return true;

            // 같은 시점의 Next Effect는 Section 전환 전까지 Binding이 없다.
            if (HasMatchingNextEffect(clip, hitNotify.NormalizedTime, hit))
                return false;

            return TryAttachHitToEffect(hit);
        }

        private bool TryAssignHitToPendingNextEffect(
            float normalizedTime, HitData hit)
        {
            for (int i = _pendingNextEffects.Count - 1; i >= 0; i--)
            {
                PendingNextEffect pending = _pendingNextEffects[i];
                if (!Mathf.Approximately(pending.NormalizedTime, normalizedTime)
                    || !CanBindHit(pending.Effect, hit)) continue;

                if (pending.Hit == null) pending.Hit = hit;
                return true;
            }
            return false;
        }

        private static bool HasMatchingNextEffect(
            TrackClip clip, float normalizedTime, HitData hit)
        {
            for (int i = 0; i < clip.Notifies.Count; i++)
            {
                TrackNotify notify = clip.Notifies[i];
                if (!Mathf.Approximately(notify.NormalizedTime, normalizedTime)
                    || notify.TransitionMode != EffectTransitionMode.Next
                    || !(notify.Payload is EffectNotifyPayload payload)
                    || !CanBindHit(payload.Effect, hit)) continue;

                return true;
            }
            return false;
        }

        private static bool CanBindHit(CompositeEffect effect, HitData hit)
        {
            if (effect == null || hit == null
                || string.IsNullOrEmpty(hit.EffectKey)) return false;

            string effectKey = hit.EffectKey;
            for (int i = 0; i < effect.Entries.Count; i++)
            {
                CompositeEffectEntry entry = effect.Entries[i];
                if (entry != null && string.Equals(
                    entry.BindingKey?.Trim(), effectKey,
                    System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private EffectHandle DispatchNotify(TrackNotify notify)
        {
            switch (notify.Payload)
            {
                case EffectNotifyPayload effectPayload:
                    if (effectPayload.Effect != null)
                        return EffectService.PlayAfterAnimation(
                            effectPayload.Effect,
                            EffectPlayContext.ForCharacter(
                                _context.Transform,
                                effectPayload.Hit,
                                _effectBindings,
                                _showHitGizmos,
                                _hitGizmoDuration),
                            true);
                    return null;

                case CameraNotifyPayload cameraPayload:
                    if (cameraPayload.Mode == CameraNotifyMode.Shot)
                        CameraFeedbackService.PlayShot(
                            cameraPayload.CreateShotRequest(_context.Transform));
                    else
                        CameraFeedbackService.PlayShake(
                            cameraPayload.CreateShakeRequest());
                    return null;

                case CustomNotifyPayload customPayload:
                    if (customPayload.EventType == ConfigEventType.HitShake)
                        _context.Animator.PlayHitShake();
                    return null;

                default:
                    return null;
            }
        }

        private EffectHandle QueueNextEffect(
            TrackClip clip, TrackNotify notify)
        {
            if (!(notify.Payload is EffectNotifyPayload payload)
                || payload.Effect == null
                || string.IsNullOrEmpty(payload.NextSection)) return null;

            var pending = new PendingNextEffect
            {
                Effect = payload.Effect,
                Hit = payload.Hit,
                NextSection = payload.NextSection,
                NormalizedTime = notify.NormalizedTime,
            };
            _pendingNextEffects.Add(pending);
            AssignFiredHitToPendingNextEffect(clip, pending);
            return null;
        }

        private void AssignFiredHitToPendingNextEffect(
            TrackClip clip, PendingNextEffect pending)
        {
            for (int i = 0; i < clip.Notifies.Count; i++)
            {
                if (!_notifyFired[i] || !_hitSyncPending[i]
                    || !(clip.Notifies[i].Payload
                        is HitNotifyPayload payload)
                    || !payload.SyncWithEffect
                    || !Mathf.Approximately(
                        clip.Notifies[i].NormalizedTime,
                        pending.NormalizedTime)
                    || !CanBindHit(pending.Effect, payload.Hit)) continue;

                if (pending.Hit == null) pending.Hit = payload.Hit;
                _hitSyncPending[i] = false;
                return;
            }
        }

        private void PlayPendingNextEffects(
            string destinationSection, bool preserveUnmatched = false)
        {
            for (int i = _pendingNextEffects.Count - 1; i >= 0; i--)
            {
                PendingNextEffect pending = _pendingNextEffects[i];
                if (!string.Equals(
                    pending.NextSection,
                    destinationSection,
                    System.StringComparison.Ordinal)) continue;

                EffectHandle handle = EffectService.PlayAfterAnimation(
                    pending.Effect,
                    EffectPlayContext.ForCharacter(
                        _context.Transform,
                        pending.Hit,
                        _effectBindings,
                        _showHitGizmos,
                        _hitGizmoDuration),
                    true);
                if (handle != null) _carriedEffects.Add(handle);
                _pendingNextEffects.RemoveAt(i);
            }
            if (!preserveUnmatched) _pendingNextEffects.Clear();
        }

        private void StopTrackedEffects(
            bool transferNext, string destinationSection = null)
        {
            for (int i = 0; i < _carriedEffects.Count; i++)
                _carriedEffects[i]?.Stop();
            _carriedEffects.Clear();

            if (_notifyActive == null) return;
            for (int i = 0; i < _notifyActive.Length; i++)
            {
                EffectHandle handle = _notifyActive[i];
                if (handle != null)
                {
                    EffectTransitionMode mode =
                        _notifyTransitionModes != null
                        && i < _notifyTransitionModes.Length
                            ? _notifyTransitionModes[i]
                            : EffectTransitionMode.Keep;
                    if (mode == EffectTransitionMode.Stop
                        || mode == EffectTransitionMode.Next)
                    {
                        string nextSection = _notifyNextSections != null
                            && i < _notifyNextSections.Length
                                ? _notifyNextSections[i]
                                : null;
                        bool matchesDestination = transferNext
                            && !string.IsNullOrEmpty(nextSection)
                            && string.Equals(
                                nextSection,
                                destinationSection,
                                System.StringComparison.Ordinal);
                        if (matchesDestination) _carriedEffects.Add(handle);
                        else handle.Stop();
                    }
                }
                _notifyActive[i] = null;
            }
        }

        private void StopTrackedSounds(
            bool transferNext, string destinationSection = null)
        {
            for (int i = 0; i < _carriedSounds.Count; i++)
                _carriedSounds[i]?.Stop();
            _carriedSounds.Clear();

            if (_soundActive == null) return;
            for (int i = 0; i < _soundActive.Length; i++)
            {
                AudioHandle handle = _soundActive[i];
                if (handle != null)
                {
                    string nextSection = _soundNextSections != null
                        && i < _soundNextSections.Length
                            ? _soundNextSections[i]
                            : null;
                    bool matchesDestination = transferNext
                        && !string.IsNullOrEmpty(nextSection)
                        && string.Equals(
                            nextSection,
                            destinationSection,
                            System.StringComparison.Ordinal);
                    if (matchesDestination) _carriedSounds.Add(handle);
                    else handle.Stop();
                }
                _soundActive[i] = null;
            }
        }

        private void StopTrackedHits()
        {
            if (_hitActive == null) return;
            for (int i = 0; i < _hitActive.Length; i++)
            {
                _hitActive[i]?.Stop();
                _hitActive[i] = null;
            }
        }
    }
}
