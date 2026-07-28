using System.Collections.Generic;
using UnityEngine;
using ZZZ;
using ZZZ.Effects;

namespace ZZZ.Player.StateMachine.States
{
    // AnimationConfig(Clips + Links)를 파싱해 구동하는 단일 러너.
    // 링크가 다른 config를 가리키면 현재 config를 갈아끼운다.
    // 모든 동작(걷기/콤보/회피/피격 등)을 이 한 클래스 + config로 표현한다 — 전이는 전부 config가 관리.
    public class ConfigState
    {
        private readonly ConfigContext   Ctx;
        private readonly IConfigSignals  Signals;
        private readonly AnimationConfig _homeConfig;   // 진입/복귀 기본 config
        private readonly SectionContext  _sc;           // 섹션 모듈에 넘기는 핸들 묶음

        private AnimationConfig _config;   // 현재 구동 중 config
        private int             _active;   // 현재 클립 인덱스
        private bool[]          _notifyFired;
        // 구간(Interval) 이펙트의 활성 핸들 — 단발이거나 미진행이면 null. _notifyFired와 인덱스 정렬, 섹션 스코프.
        private EffectHandle[]  _notifyActive;
        private float           _clipTime; // 현재 섹션 진입 후 경과 시간(초) — 전환마다 0으로 리셋

        // OnEndIfMatched 링크의 윈도우 래치 상태 — 섹션 진입마다 비운다(섹션 스코프).
        private readonly HashSet<ClipLink> _latched = new HashSet<ClipLink>();

        // 링크 조건(LinkCondition) 평가용 컨텍스트 — 플레이어 입력/방향 질의를 공급한다.
        private readonly ILinkConditionContext _condCtx;

        // 비어 있는(null) Condition은 무조건 전이(Always)로 취급 — 공유 인스턴스 재사용(무할당, 무상태).
        private static readonly AlwaysCondition s_always = new AlwaysCondition();

        // 링크 조건 접근. Condition이 null이면 Always로 폴백한다(가드 없는 전이).
        private static LinkCondition Cond(ClipLink link) => link.Condition ?? s_always;

        // 캐릭터(플레이어/몬스터)별 컨텍스트·신호·조건소스를 주입받는다 — 구상 타입 비의존.
        public ConfigState(ConfigContext ctx, IConfigSignals signals,
            ILinkConditionContext condCtx, AnimationConfig homeConfig)
        {
            Ctx         = ctx;
            Signals     = signals;
            _homeConfig = homeConfig;
            _condCtx    = condCtx;
            _sc         = new SectionContext { Ctx = ctx, Machine = signals };
        }

        public void Enter()
        {
            SwitchConfig(_homeConfig, null, 0f);
            Signals.ConsumeInput();
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
            if (_config == null || _config.Clips.Count == 0) { StopActiveIntervals(); _active = -1; return; }

            int idx = !string.IsNullOrEmpty(section)
                ? _config.IndexOfSection(section)
                : _config.IndexOfSection(_config.EntrySection);
            _active = idx >= 0 ? idx : 0;
            PlayActive(blend, startOffset);
        }

        public void Update()
        {
            if (_config == null) return;
            if (_active < 0 || _active >= _config.Clips.Count) return;

            var tc = _config.Clips[_active];

            // 섹션 진입 후 경과 시간으로 nt를 직접 계산한다 — 섹션 타임라인의 0점을 코드가 소유하기 위해서다.
            // Animator 상태 시간은 같은 섹션 재진입(A→B→A) 시 0이 아닌 이전 지점에서 이어지고, EntryOffset
            // (중간 진입)도 통제하기 어렵다. _clipTime은 진입 시 0(또는 offset)으로 리셋되므로 항상 섹션 기준이다.
            float previousNtRaw = SectionNormalizedTime(tc);
            _clipTime += Time.deltaTime;
            float ntRaw = SectionNormalizedTime(tc);

            FireNotifies(tc, ntRaw);
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
            _active = ti;
            PlayActive(link.BlendDuration, link.EntryOffset);
        }

        // startOffset(normalizedTime, 0~1) = 대상 클립을 그 지점부터 재생(중간 프레임 진입). 0 = 처음부터.
        private void PlayActive(float blend, float startOffset = 0f)
        {
            StopActiveIntervals();   // 떠나는 섹션의 진행 중 구간 이펙트 정지(전이/재진입 시 누수 방지)
            _clipTime = 0f;   // 새 섹션 진입 → 타임라인 리셋
            _latched.Clear(); // 새 섹션 → OnEndIfMatched 윈도우 래치 리셋
            Signals.Invulnerable = false;    // 섹션 진입 시 무적 해제 — i-frame 모듈이 윈도우 동안만 다시 켠다
            Signals.ParryActive  = false;    // 섹션 진입 시 패링 해제 — parry 모듈이 윈도우 동안만 다시 켠다

            var tc = _config.Clips[_active];
            if (tc.Clip == null) { _notifyFired = null; _notifyActive = null; return; }

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
            Ctx.Mover.SmoothLoopSpeed = false;
            Ctx.Mover.BackMotionScale = 1f;
            Ctx.Mover.ExtractRootRotation = false;
            Ctx.Mover.RootRotationWindowActive = false;
            Ctx.Mover.ClearWarpTarget();
            Ctx.Mover.AddStartBoost(0f, 0f);
            if (tc.MoveMode == MoveMode.RootMotion) Ctx.Mover.FlushRootPos();

            // 섹션 기능은 모듈만 소유한다. 위 기본값 초기화 후 OnEnter 순서대로 필요한 기능을 켠다.
            _sc.FacedInputThisEnter = false;
            _sc.PreviousNormalizedTime = SectionNormalizedTime(tc);
            for (int i = 0; i < tc.Modules.Count; i++)
                tc.Modules[i]?.OnEnter(tc, _sc);

            _notifyFired  = new bool[tc.Notifies.Count];
            _notifyActive = new EffectHandle[tc.Notifies.Count];
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

        private void FireNotifies(TrackClip tc, float ntRaw)
        {
            if (_notifyFired == null) return;
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
            for (int i = 0; i < tc.Notifies.Count && i < _notifyFired.Length; i++)
            {
                var notify = tc.Notifies[i];

                // 시작 — 아직 발동 안 했고 시작 시점을 지났으면 스폰. 구간 이펙트면 정지용 핸들을 보관한다.
                if (!_notifyFired[i] && p >= notify.NormalizedTime)
                {
                    _notifyFired[i] = true;
                    EffectHandle handle = DispatchNotify(notify);
                    if (notify.IsInterval) _notifyActive[i] = handle;
                }

                // 종료 — 구간 이펙트가 진행 중이고 끝 시점을 지났으면 방출 정지(잔여 파티클은 자연 소멸).
                if (_notifyActive[i] != null && p >= notify.EndNormalizedTime)
                {
                    _notifyActive[i].Stop();
                    _notifyActive[i] = null;
                }
            }
        }

        // 이펙트 재생이면 정지용 EffectHandle을 돌려준다(구간 이펙트만 사용). 그 외/단발은 null.
        private EffectHandle DispatchNotify(TrackNotify notify)
        {
            switch (notify.Type)
            {
                case NotifyType.Effect:
                    if (notify.Effect != null)
                        return EffectService.PlayAfterAnimation(
                            notify.Effect, Ctx.Transform, notify.IsInterval);
                    return null;
                default:
                    if (!string.IsNullOrEmpty(notify.EventName))
                        Ctx.GameObject.SendMessage(
                            notify.EventName, SendMessageOptions.DontRequireReceiver);
                    return null;
            }
        }

        // 진행 중인 구간 이펙트를 전부 정지(섹션 이탈·인터럽트·종료 시 누수 방지).
        private void StopActiveIntervals()
        {
            if (_notifyActive == null) return;
            for (int i = 0; i < _notifyActive.Length; i++)
            {
                if (_notifyActive[i] != null) _notifyActive[i].Stop();
                _notifyActive[i] = null;
            }
        }

        // 현재는 항상 활성이라 호출되지 않지만, 무적 누수 방지를 위한 정리 진입점으로 남겨둔다.
        public void Exit()
        {
            StopActiveIntervals();
            _notifyFired = null;
            Ctx.Mover.ClearWarpTarget();
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

        // 현재 섹션(또는 config 공통 GlobalLinks)에 이 공격 입력을 받는 링크가 있는지.
        // 있으면 그 섹션이 입력을 '직접' 처리한다는 뜻 → 전역 폴백 트리거(강화 등)가 윈도우 전에 입력을
        // 가로채지 않도록 게이트하는 데 쓴다. (Attack==input 또는 Any를 받는 링크가 대상. None은 제외)
        public bool ActiveSectionHandles(ComboInput input)
        {
            if (_config == null || _active < 0 || _active >= _config.Clips.Count) return false;
            return HasInputLink(_config.Clips[_active].Links, input)
                || HasInputLink(_config.GlobalLinks, input);
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
