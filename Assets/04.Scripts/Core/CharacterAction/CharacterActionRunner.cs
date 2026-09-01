using System.Collections.Generic;
using UnityEngine;
using ZZZ.Audio;
using ZZZ.Combat;
using ZZZ.Effects;

namespace ZZZ
{
    // AnimationConfig(Clips + Links)를 파싱해 구동하는 단일 러너.
    // 링크가 다른 config를 가리키면 현재 config를 갈아끼운다.
    // 모든 동작(걷기/콤보/회피/피격 등)을 이 한 클래스 + config로 표현한다 — 전이는 전부 config가 관리.
    public class CharacterActionRunner
    {
        private readonly CharacterActionContext   Ctx;
        private readonly ICharacterActionSignals  Signals;
        private readonly AnimationConfig _homeConfig;   // 진입/복귀 기본 config
        private readonly SectionContext  _sc;           // 섹션 모듈에 넘기는 핸들 묶음
        private bool _showHitGizmos;
        private float _hitGizmoDuration;

        private AnimationConfig _config;   // 현재 구동 중 config
        private int             _active;   // 현재 클립 인덱스
        private bool[]          _notifyFired;
        // 구간(Interval) 이펙트의 활성 핸들 — 단발이거나 미진행이면 null. _notifyFired와 인덱스 정렬, 섹션 스코프.
        private EffectHandle[]  _notifyActive;
        private HitHandle[]     _hitActive;
        private bool[]          _hitSyncPending;
        private EffectTransitionMode[] _notifyTransitionModes;
        private string[]        _notifyNextSections;
        private AudioHandle[]   _soundActive;
        private string[]        _soundNextSections;
        private readonly List<EffectHandle> _carriedEffects = new List<EffectHandle>();
        private readonly List<AudioHandle> _carriedSounds = new List<AudioHandle>();
        private readonly List<PendingNextEffect> _pendingNextEffects =
            new List<PendingNextEffect>();
        private readonly EffectBindingScope _effectBindings = new EffectBindingScope();
        private float           _clipTime; // 현재 섹션 진입 후 경과 시간(초) — 전환마다 0으로 리셋

        private sealed class PendingNextEffect
        {
            public CompositeEffect Effect;
            public HitData Hit;
            public string NextSection;
            public float NormalizedTime;
        }

        // OnEndIfMatched 링크의 윈도우 래치 상태 — 섹션 진입마다 비운다(섹션 스코프).
        private readonly HashSet<ClipLink> _latched = new HashSet<ClipLink>();

        // 링크 조건(LinkCondition) 평가용 컨텍스트 — 플레이어 입력/방향 질의를 공급한다.
        private readonly ILinkConditionContext _condCtx;

        // 비어 있는(null) Condition은 무조건 전이(Always)로 취급 — 공유 인스턴스 재사용(무할당, 무상태).
        private static readonly AlwaysCondition s_always = new AlwaysCondition();

        // 링크 조건 접근. Condition이 null이면 Always로 폴백한다(가드 없는 전이).
        private static LinkCondition Cond(ClipLink link) => link.Condition ?? s_always;

        // 캐릭터(플레이어/몬스터)별 컨텍스트·신호·조건소스를 주입받는다 — 구상 타입 비의존.
        public CharacterActionRunner(CharacterActionContext ctx, ICharacterActionSignals signals,
            ILinkConditionContext condCtx, AnimationConfig homeConfig,
            bool showHitGizmos = false, float hitGizmoDuration = 0.1f)
        {
            Ctx         = ctx;
            Signals     = signals;
            _homeConfig = homeConfig;
            _condCtx    = condCtx;
            _sc         = new SectionContext { Ctx = ctx, Machine = signals };
            _showHitGizmos = showHitGizmos;
            _hitGizmoDuration = Mathf.Max(0f, hitGizmoDuration);
        }

        public void Enter()
        {
            SwitchConfig(_homeConfig, null, 0f);
            Signals.ConsumeInput();
        }

        public void SetHitDebug(bool showHitGizmos, float hitGizmoDuration)
        {
            _showHitGizmos = showHitGizmos;
            _hitGizmoDuration = Mathf.Max(0f, hitGizmoDuration);
        }

        // 외부 이벤트(피격 등)로 현재 config를 즉시 갈아끼운다.
        // 입력 조건이 아닌 이벤트로 진입해야 하는 config(Hit 등)용.
        // 해당 config가 OnEnd Link로 home(걷기)에 복귀하면 자연스럽게 돌아온다.
        public void InterruptWith(AnimationConfig config, string section = null, float blend = 0.1f)
        {
            if (config == null) return;
            SwitchConfig(config, section, blend);
        }

        // config를 갈아끼우고 지정 섹션(비면 EntrySection)으로 진입
        private void SwitchConfig(AnimationConfig config, string section, float blend, float startOffset = 0f)
        {
            _config = config;
            if (_config == null || _config.Clips.Count == 0)
            {
                StopTrackedEffects(false);
                StopTrackedSounds(false);
                StopTrackedHits();
                _pendingNextEffects.Clear();
                _effectBindings.Clear();
                _active = -1;
                return;
            }

            int idx = !string.IsNullOrEmpty(section)
                ? _config.IndexOfSection(section)
                : _config.IndexOfSection(_config.EntrySection);
            _active = idx >= 0 ? idx : 0;
            PlayActive(blend, startOffset);
        }

        public void Update(float localTimeScale = 1f)
        {
            if (_config == null) return;
            if (_active < 0 || _active >= _config.Clips.Count) return;

            var tc = _config.Clips[_active];

            // 섹션 진입 후 경과 시간으로 nt를 직접 계산한다 — 섹션 타임라인의 0점을 코드가 소유하기 위해서다.
            // Animator 상태 시간은 같은 섹션 재진입(A→B→A) 시 0이 아닌 이전 지점에서 이어지고, EntryOffset
            // (중간 진입)도 통제하기 어렵다. _clipTime은 진입 시 0(또는 offset)으로 리셋되므로 항상 섹션 기준이다.
            float deltaTime = Time.deltaTime * Mathf.Max(0f, localTimeScale);
            float previousNtRaw = SectionNormalizedTime(tc);
            _clipTime += deltaTime;
            float ntRaw = SectionNormalizedTime(tc);

            FireNotifies(tc, previousNtRaw, ntRaw, deltaTime);
            Ctx.Mover.AllowRotation = true;
            Ctx.Mover.WarpWindowActive = false;
            Ctx.Mover.FaceWindowActive = false;
            Ctx.Mover.RootRotationWindowActive = false;
            _sc.PreviousNormalizedTime = previousNtRaw;
            TickModules(tc, ntRaw);

            // 클립 고유 링크 먼저, 그 다음 config 공통 링크(Global) 평가
            if (TryLinks(tc.Links, tc, ntRaw)) return;
            if (_config.GlobalLinks != null && TryLinks(_config.GlobalLinks, tc, ntRaw)) return;
        }

        // links를 순서대로 평가해 첫 발동 링크를 타고 전이한다. 전이했으면 true.
        private bool TryLinks(List<ClipLink> links, TrackClip tc, float ntRaw)
        {
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
            foreach (var link in links)
            {
                // OnEndIfMatched: 조건을 '발동 시점'이 아니라 '윈도우 구간'에서 보고 래치한다.
                // 그래서 top의 Matches 게이트를 거치지 않고 따로 처리(끝에선 입력이 이미 사라짐).
                if (link.Timing == LinkTiming.OnEndIfMatched)
                {
                    if (TryLatchLink(link, tc, p)) return true;
                    continue;
                }

                // OnRelease: 이 링크 조건의 '릴리스 신호'(홀드 차지 → 뗌)로 발동. press 버퍼를 보는
                // Matches 게이트 대신 Condition.ReleaseTriggered를 직접 본다.
                if (link.Timing == LinkTiming.OnRelease)
                {
                    if (TryReleaseLink(link, p)) return true;
                    continue;
                }

                // 조건이 안 맞으면 어떤 타이밍이든 발동 안 함
                if (!Cond(link).Matches(_condCtx)) continue;

                bool fire = false;
                switch (link.Timing)
                {
                    case LinkTiming.WhenMatched:
                        fire = p >= link.WindowStart && p <= link.WindowEnd;
                        break;

                    case LinkTiming.OnEnd:
                        // 루프 클립도 사이클 끝(wrap된 p)에서 탈출 조건을 검사한다.
                        // 조건(Direction 등)이 안 맞으면 위 Matches에서 이미 걸러짐.
                        fire = p >= EndThreshold(tc);
                        break;
                }

                if (fire)
                {
                    Cond(link).Consume(_condCtx);   // 입력을 요구한 조건만 버퍼 소비(InputCondition)
                    TakeLink(link);
                    return true;
                }
            }
            return false;
        }

        // OnEndIfMatched 처리 — 윈도우[Start,End] 안에서 조건이 충족되면 래치(입력은 즉시 소비해
        // 같은 입력이 다른 링크를 오발동시키지 않게 함). 섹션 끝(EndThreshold)에서 래치돼 있으면 전이.
        // 래치는 섹션 진입마다 리셋(_latched). 반환 true = 전이함.
        private bool TryLatchLink(ClipLink link, TrackClip tc, float p)
        {
            if (!_latched.Contains(link)
                && p >= link.WindowStart && p <= link.WindowEnd
                && Cond(link).Matches(_condCtx))
            {
                _latched.Add(link);
                Cond(link).Consume(_condCtx);
            }

            if (p >= EndThreshold(tc) && _latched.Contains(link))
            {
                TakeLink(link);
                return true;
            }
            return false;
        }

        // OnRelease 처리 — [WindowStart,End] 안에서 조건의 릴리스 신호가 충족되면 전이.
        // InputCondition은 "Attack 키가 떼졌고 방향 충족"을 릴리스로 본다(홀드 차지 → 발사).
        // 윈도우 시작(WindowStart)이 최소 차지 — 그 전에 떼도 무시되어 windup이 끊기지 않는다.
        private bool TryReleaseLink(ClipLink link, float p)
        {
            if (p < link.WindowStart || p > link.WindowEnd) return false;
            if (!Cond(link).ReleaseTriggered(_condCtx)) return false;

            TakeLink(link);
            return true;
        }

        // OnEnd 발동 기준 normalizedTime.
        // config.DoneThreshold가 (0,1) 사이면 수동값, 아니면 클립 마지막 프레임(1 - 1/frames)
        private float EndThreshold(TrackClip tc)
        {
            float dt = _config != null ? _config.DoneThreshold : 0f;
            if (dt > 0f && dt < 1f) return dt;
            return LastFrameNt(tc);
        }

        // 클립의 끝에서 한 프레임 전 지점 (프레임 정확도)
        private static float LastFrameNt(TrackClip tc)
        {
            if (tc.Clip != null && tc.Clip.frameRate > 0f)
            {
                float frames = tc.Clip.length * tc.Clip.frameRate;
                if (frames > 1f) return Mathf.Clamp01(1f - 1f / frames);
            }
            return 0.999f;
        }

        private void TakeLink(ClipLink link)
        {
            // 다른 config로 전이 (EntryOffset → 대상 섹션 중간 프레임부터)
            if (link.TargetConfig != null && link.TargetConfig != _config)
            {
                SwitchConfig(link.TargetConfig, link.TargetSection, link.BlendDuration, link.EntryOffset);
                return;
            }

            // 같은 config 내 섹션 전이 (대상 없으면 home으로 복귀)
            int ti = _config.IndexOfSection(link.TargetSection);
            if (ti < 0)
            {
                SwitchConfig(_homeConfig, null, link.BlendDuration, link.EntryOffset);
                return;
            }
            bool sameSectionReentry = ti == _active;
            _active = ti;
            PlayActive(link.BlendDuration, link.EntryOffset, sameSectionReentry);
        }

        // startOffset(normalizedTime, 0~1) = 대상 클립을 그 지점부터 재생(중간 프레임 진입). 0 = 처음부터.
        private void PlayActive(float blend, float startOffset = 0f,
            bool sameSectionReentry = false)
        {
            string destinationSection = _config.Clips[_active].SectionName;
            StopTrackedHits();
            if (!sameSectionReentry)
            {
                StopTrackedEffects(true, destinationSection);
                StopTrackedSounds(true, destinationSection);
            }
            PlayPendingNextEffects(destinationSection, sameSectionReentry);
            _clipTime = 0f;   // 새 섹션 진입 → 타임라인 리셋
            _latched.Clear(); // 새 섹션 → OnEndIfMatched 윈도우 래치 리셋
            Signals.Invulnerable = false;    // 섹션 진입 시 무적 해제 — i-frame 모듈이 윈도우 동안만 다시 켠다
            Signals.ParryActive  = false;    // 섹션 진입 시 패링 해제 — parry 모듈이 윈도우 동안만 다시 켠다

            var tc = _config.Clips[_active];
            if (tc.Clip == null)
            {
                _notifyFired = null;
                _notifyActive = null;
                _soundActive = null;
                _hitActive = null;
                _notifyTransitionModes = null;
                _notifyNextSections = null;
                _soundNextSections = null;
                return;
            }

            // 중간 프레임 진입 — 로직 타임라인(_clipTime)과 애니 시작점을 같은 normalizedTime으로 맞춘다.
            // SectionNormalizedTime = _clipTime * Speed / length 이므로 _clipTime = off * length / Speed.
            float offsetSec = 0f;
            if (startOffset > 0f && tc.Clip.length > 0f)
            {
                float off = Mathf.Clamp01(startOffset);
                _clipTime = off * tc.Clip.length / Mathf.Max(0.01f, tc.Speed);
                offsetSec = off * tc.Clip.length;   // CrossFade는 클립 초(speed 무관) 단위
            }

            Ctx.Animator.Play(tc.Clip.name, blend, offsetSec);
            // 비주얼 재생 속도를 로직 타임라인(SectionNormalizedTime의 Speed)과 일치시킨다.
            // 안 맞추면 애니는 1배속으로 끝나 freeze되고 로직만 Speed배로 흘러 OnEnd가 늦게/일찍 발동(전환 딜레이).
            Ctx.Animator.ApplyAnimatorSpeed(tc.Speed);

            // 이동 방식 적용
            Ctx.Mover.UseCodeMovement = tc.MoveMode != MoveMode.RootMotion;
            Ctx.Mover.AllowRotation = true;
            Ctx.Mover.BackMotionScale = 1f;
            Ctx.Mover.ExtractRootRotation = false;
            Ctx.Mover.RootRotationSourceAxis = RootMotionRotationAxis.Auto;
            Ctx.Mover.RootRotationScale = 1f;
            Ctx.Mover.RootRotationTargetAngle = 0f;
            Ctx.Mover.KillRootRotation = false;
            Ctx.Mover.RootRotationWindowActive = false;
            Ctx.Mover.ClearWarpTarget();
            Ctx.Mover.AddStartBoost(0f, 0f);
            // 섹션 기능은 모듈만 소유한다. 위 기본값 초기화 후 OnEnter 순서대로 필요한 기능을 켠다.
            _sc.FacedInputThisEnter = false;
            _sc.EntryForward = Ctx.Transform != null
                ? Ctx.Transform.forward
                : Vector3.forward;
            _sc.EntryMoveDirection = Ctx.Mover.MoveDirection;
            _sc.PreviousNormalizedTime = SectionNormalizedTime(tc);
            for (int i = 0; i < tc.Modules.Count; i++)
                tc.Modules[i]?.OnEnter(tc, _sc);

            bool preserveNotifyState = sameSectionReentry
                && _notifyFired != null
                && _notifyFired.Length == tc.Notifies.Count
                && _notifyActive != null
                && _notifyActive.Length == tc.Notifies.Count
                && _soundActive != null
                && _soundActive.Length == tc.Notifies.Count;
            if (!preserveNotifyState)
            {
                _notifyFired = new bool[tc.Notifies.Count];
                _notifyActive = new EffectHandle[tc.Notifies.Count];
                _soundActive = new AudioHandle[tc.Notifies.Count];
            }
            _hitActive = new HitHandle[tc.Notifies.Count];
            _hitSyncPending = new bool[tc.Notifies.Count];
            for (int i = 0; i < tc.Notifies.Count; i++)
            {
                if (!preserveNotifyState) continue;

                TrackNotify notify = tc.Notifies[i];
                bool preserveNotify =
                    notify.Payload is EffectNotifyPayload effectPayload
                    && (effectPayload.TransitionMode == EffectTransitionMode.Next
                        || effectPayload.TransitionMode == EffectTransitionMode.Stop
                        && _notifyActive[i] != null)
                    || (notify.Payload is SoundNotifyPayload soundPayload
                        && soundPayload.Loop
                        && _soundActive[i] != null
                        && !_soundActive[i].IsStopped);
                if (!preserveNotify)
                {
                    _notifyFired[i] = false;
                    _notifyActive[i] = null;
                    _soundActive[i] = null;
                }
            }

            _notifyTransitionModes = new EffectTransitionMode[tc.Notifies.Count];
            _notifyNextSections = new string[tc.Notifies.Count];
            _soundNextSections = new string[tc.Notifies.Count];
            for (int i = 0; i < tc.Notifies.Count; i++)
            {
                _notifyTransitionModes[i] = tc.Notifies[i].TransitionMode;
                _notifyNextSections[i] = tc.Notifies[i].NextSection;
                _soundNextSections[i] =
                    tc.Notifies[i].Payload is SoundNotifyPayload soundPayload
                        ? soundPayload.NextSection
                        : "";
            }
            // 중간 진입 시 그 지점 이전의 Notify는 이미 지난 것으로 처리 — 진입하자마자 무더기 발동 방지.
            if (startOffset > 0f)
                for (int i = 0; i < tc.Notifies.Count; i++)
                    if (tc.Notifies[i].NormalizedTime < startOffset) _notifyFired[i] = true;
        }

        // 섹션 진입 후 경과 시간을 normalizedTime으로 변환 (Speed 반영, 루프는 계속 증가)
        private float SectionNormalizedTime(TrackClip tc)
        {
            if (tc.Clip == null || tc.Clip.length <= 0f) return 1f;
            return _clipTime * Mathf.Max(0.01f, tc.Speed) / tc.Clip.length;
        }

        // 섹션 모듈 매 프레임 구동 (i-frame 등). 있는 모듈만 실행.
        private void TickModules(TrackClip tc, float ntRaw)
        {
            var mods = tc.Modules;
            for (int i = 0; i < mods.Count; i++)
                mods[i]?.Tick(tc, ntRaw, _sc);
        }

        private void FireNotifies(
            TrackClip tc, float previousNtRaw, float ntRaw, float deltaTime)
        {
            if (_notifyFired == null) return;
            if (tc.IsLooping
                && Mathf.FloorToInt(ntRaw) > Mathf.FloorToInt(previousNtRaw))
                ResetLoopingSoundNotifies(tc);

            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
            for (int i = 0; i < tc.Notifies.Count && i < _notifyFired.Length; i++)
            {
                var notify = tc.Notifies[i];

                // 시작 — 아직 발동 안 했고 시작 시점을 지났으면 스폰. 구간 이펙트면 정지용 핸들을 보관한다.
                if (!_notifyFired[i] && p >= notify.NormalizedTime)
                {
                    _notifyFired[i] = true;
                    if (notify.Payload is HitNotifyPayload hitPayload)
                    {
                        bool parryWarning = hitPayload.Action
                            == HitNotifyAction.ParryWarning;
                        if (!parryWarning && hitPayload.SyncWithEffect)
                        {
                            _hitSyncPending[i] = !TryAttachSynchronizedHit(
                                tc, notify, hitPayload.Hit);
                            continue;
                        }
                        var hitContext = new HitExecutionContext(
                            Ctx.Transform, null, _effectBindings,
                            _showHitGizmos, _hitGizmoDuration);
                        if (notify.IsInterval
                            || hitPayload.Hit.Origin == HitOrigin.Effect)
                            _hitActive[i] = parryWarning
                                ? HitService.BeginParryWarning(
                                    hitPayload.Hit, hitContext,
                                    hitPayload.WarningDuration)
                                : HitService.Begin(hitPayload.Hit, hitContext);
                        else if (parryWarning)
                            HitService.ExecuteParryWarning(
                                hitPayload.Hit, hitContext,
                                hitPayload.WarningDuration);
                        else
                            HitService.Execute(hitPayload.Hit, hitContext);
                        continue;
                    }
                    if (notify.Payload is SoundNotifyPayload soundPayload)
                    {
                        if (soundPayload.Sound != null)
                        {
                            SoundFadeModule fadeModule =
                                soundPayload.FindModule<SoundFadeModule>();
                            SoundDurationModule durationModule =
                                soundPayload.FindModule<SoundDurationModule>();
                            _soundActive[i] = AudioService.PlayAfterAnimation(
                                soundPayload.Sound,
                                SoundPlayContext.ForTransform(Ctx.Transform),
                                soundPayload.Loop,
                                fadeModule != null
                                    ? fadeModule.FadeInDuration
                                    : 0f,
                                fadeModule != null
                                    ? fadeModule.FadeOutDuration
                                    : 0f,
                                durationModule != null
                                    ? durationModule.Duration
                                    : 0f);
                        }
                        continue;
                    }
                    EffectHandle handle = notify.Payload is EffectNotifyPayload
                        && notify.TransitionMode == EffectTransitionMode.Next
                        ? QueueNextEffect(tc, notify)
                        : DispatchNotify(notify);
                    if (notify.Payload is EffectNotifyPayload && handle != null)
                        _notifyActive[i] = handle;
                }

                if (_hitSyncPending != null && _hitSyncPending[i]
                    && notify.Payload is HitNotifyPayload pendingHit)
                    _hitSyncPending[i] = !TryAttachSynchronizedHit(
                        tc, notify, pendingHit.Hit);

                if (_hitActive != null && _hitActive[i] != null)
                {
                    float duration = notify.EndNormalizedTime - notify.NormalizedTime;
                    float progress = duration > 0f
                        ? Mathf.InverseLerp(
                            notify.NormalizedTime, notify.EndNormalizedTime, p)
                        : 1f;
                    _hitActive[i].Tick(deltaTime, progress);
                    if (!notify.IsInterval && _hitActive[i].HasSampled)
                    {
                        _hitActive[i].Stop();
                        _hitActive[i] = null;
                    }
                }

                // 종료 — 구간 이펙트가 진행 중이고 끝 시점을 지났으면 방출 정지(잔여 파티클은 자연 소멸).
                if (notify.IsInterval && _notifyActive[i] != null
                    && p >= notify.EndNormalizedTime)
                {
                    _notifyActive[i].Stop();
                    _notifyActive[i] = null;
                }
                if (notify.IsInterval && _hitActive != null && _hitActive[i] != null
                    && p >= notify.EndNormalizedTime)
                {
                    _hitActive[i].Stop();
                    _hitActive[i] = null;
                }
            }
        }

        private void ResetLoopingSoundNotifies(TrackClip tc)
        {
            int count = Mathf.Min(tc.Notifies.Count, _notifyFired.Length);
            for (int i = 0; i < count; i++)
            {
                if (!(tc.Notifies[i].Payload is SoundNotifyPayload soundPayload))
                    continue;

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
                hit.EffectKey, hit, Ctx.Transform,
                _showHitGizmos, _hitGizmoDuration);
        }

        private bool TryAttachSynchronizedHit(
            TrackClip clip, TrackNotify hitNotify, HitData hit)
        {
            if (hit == null || hit.Origin != HitOrigin.Effect) return false;
            if (TryAssignHitToPendingNextEffect(hitNotify.NormalizedTime, hit))
                return true;

            // A same-time Next effect has no live binding until the section changes.
            if (HasMatchingNextEffect(clip, hitNotify.NormalizedTime, hit))
                return false;

            return TryAttachHitToEffect(hit);
        }

        private bool TryAssignHitToPendingNextEffect(float normalizedTime, HitData hit)
        {
            for (int i = _pendingNextEffects.Count - 1; i >= 0; i--)
            {
                PendingNextEffect pending = _pendingNextEffects[i];
                if (!Mathf.Approximately(pending.NormalizedTime, normalizedTime)
                    || !CanBindHit(pending.Effect, hit))
                    continue;

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
                    || !CanBindHit(payload.Effect, hit))
                    continue;

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
                    System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // 이펙트 재생이면 정지용 EffectHandle을 돌려준다(구간 이펙트만 사용). 그 외/단발은 null.
        private EffectHandle DispatchNotify(TrackNotify notify)
        {
            switch (notify.Payload)
            {
                case EffectNotifyPayload effectPayload:
                    if (effectPayload.Effect != null)
                        return EffectService.PlayAfterAnimation(
                            effectPayload.Effect,
                            EffectPlayContext.ForCharacter(
                                Ctx.Transform, effectPayload.Hit, _effectBindings,
                                _showHitGizmos, _hitGizmoDuration),
                            true);
                    return null;
                case CameraNotifyPayload cameraPayload:
                    if (cameraPayload.Mode == CameraNotifyMode.Shot)
                        CameraFeedbackService.PlayShot(
                            cameraPayload.CreateShotRequest(Ctx.Transform));
                    else
                        CameraFeedbackService.PlayShake(
                            cameraPayload.CreateShakeRequest());
                    return null;
                case CustomNotifyPayload customPayload:
                    if (customPayload.EventType
                        == ConfigEventType.HitShake)
                        Ctx.Animator.PlayHitShake();
                    return null;
                default:
                    return null;
            }
        }

        private EffectHandle QueueNextEffect(TrackClip clip, TrackNotify notify)
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
                    || !(clip.Notifies[i].Payload is HitNotifyPayload payload)
                    || !payload.SyncWithEffect
                    || !Mathf.Approximately(
                        clip.Notifies[i].NormalizedTime, pending.NormalizedTime)
                    || !CanBindHit(pending.Effect, payload.Hit))
                    continue;

                if (pending.Hit == null) pending.Hit = payload.Hit;
                _hitSyncPending[i] = false;
                return;
            }
        }

        private void PlayPendingNextEffects(string destinationSection,
            bool preserveUnmatched = false)
        {
            for (int i = _pendingNextEffects.Count - 1; i >= 0; i--)
            {
                PendingNextEffect pending = _pendingNextEffects[i];
                if (!string.Equals(pending.NextSection, destinationSection,
                    System.StringComparison.Ordinal))
                    continue;

                EffectHandle handle = EffectService.PlayAfterAnimation(
                    pending.Effect,
                    EffectPlayContext.ForCharacter(
                        Ctx.Transform, pending.Hit, _effectBindings,
                        _showHitGizmos, _hitGizmoDuration),
                    true);
                if (handle != null) _carriedEffects.Add(handle);
                _pendingNextEffects.RemoveAt(i);
            }
            if (!preserveUnmatched) _pendingNextEffects.Clear();
        }

        // 추적 중인 이펙트를 실제 섹션 이탈·인터럽트·종료 시 정리한다.
        private void StopTrackedEffects(bool transferNext, string destinationSection = null)
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
                    EffectTransitionMode mode = _notifyTransitionModes != null
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
                            && string.Equals(nextSection, destinationSection,
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
                        && string.Equals(nextSection, destinationSection,
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

        // 현재는 항상 활성이라 호출되지 않지만, 무적 누수 방지를 위한 정리 진입점으로 남겨둔다.
        public void Exit()
        {
            StopTrackedEffects(false);
            StopTrackedSounds(false);
            StopTrackedHits();
            _pendingNextEffects.Clear();
            _effectBindings.Clear();
            _notifyFired = null;
            _hitActive = null;
            _hitSyncPending = null;
            _notifyTransitionModes = null;
            _notifyNextSections = null;
            _soundActive = null;
            _soundNextSections = null;
            Ctx.Mover.ClearWarpTarget();
            Ctx.Mover.KillRootRotation = false;
            Signals.Invulnerable = false;
            Signals.ParryActive  = false;
        }

        // ── 에디터 라이브 모니터용 읽기 전용 노출 ──────────────────
        public AnimationConfig CurrentConfig => _config;
        public int             ActiveIndex   => _active;
        public string ActiveSection =>
            (_config != null && _active >= 0 && _active < _config.Clips.Count)
                ? _config.Clips[_active].SectionName : null;
        public float CurrentNormalizedTime =>
            (_config != null && _active >= 0 && _active < _config.Clips.Count)
                ? SectionNormalizedTime(_config.Clips[_active]) : 0f;
        public MoveDir CurrentMoveDir        => Ctx.Mover.CurrentMoveDir;
        public bool IsCurrentSectionComplete
        {
            get
            {
                if (_config == null || _active < 0 || _active >= _config.Clips.Count)
                    return false;

                TrackClip clip = _config.Clips[_active];
                return !clip.IsLooping && SectionNormalizedTime(clip) >= EndThreshold(clip);
            }
        }

        // 현재 섹션(또는 config 공통 GlobalLinks)에 이 공격 입력을 받는 링크가 있는지.
        // 있으면 그 섹션이 입력을 '직접' 처리한다는 뜻 → 전역 폴백 트리거(강화 등)가 윈도우 전에 입력을
        // 가로채지 않도록 게이트하는 데 쓴다. (Attack==input 또는 Any를 받는 링크가 대상. None은 제외)
        public bool ActiveSectionHandles(ComboInput input)
        {
            if (_config == null || _active < 0 || _active >= _config.Clips.Count) return false;
            return HasInputLink(_config.Clips[_active].Links, input)
                || HasInputLink(_config.GlobalLinks, input);
        }

        public bool ActiveSectionBlocks(ComboInput input)
        {
            if (_config == null || _active < 0 || _active >= _config.Clips.Count) return false;

            TrackClip tc = _config.Clips[_active];
            float nt = SectionNormalizedTime(tc);
            var modules = tc.Modules;
            if (modules == null) return false;

            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] != null && modules[i].BlocksInput(tc, nt, input)) return true;
            }
            return false;
        }

        private static bool HasInputLink(List<ClipLink> links, ComboInput input)
        {
            if (links == null) return false;
            foreach (var l in links)
                if (l != null && Cond(l).AcceptsInput(input)) return true;
            return false;
        }
    }
}
