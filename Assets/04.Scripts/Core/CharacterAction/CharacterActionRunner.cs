using System.Collections.Generic;
using UnityEngine;

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
        private readonly CharacterNotifyRunner _notifyRunner;

        private AnimationConfig _config;   // 현재 구동 중 config
        private int             _active;   // 현재 클립 인덱스
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
        public CharacterActionRunner(CharacterActionContext ctx, ICharacterActionSignals signals,
            ILinkConditionContext condCtx, AnimationConfig homeConfig,
            bool showHitGizmos = false, float hitGizmoDuration = 0.1f)
        {
            Ctx         = ctx;
            Signals     = signals;
            _homeConfig = homeConfig;
            _condCtx    = condCtx;
            _sc         = new SectionContext { Ctx = ctx, Machine = signals };
            _notifyRunner = new CharacterNotifyRunner(
                ctx, showHitGizmos, hitGizmoDuration);
        }

        public void Enter()
        {
            SwitchConfig(_homeConfig, null, 0f);
            Signals.ConsumeInput();
        }

        public void SetHitDebug(bool showHitGizmos, float hitGizmoDuration)
        {
            _notifyRunner.SetHitDebug(showHitGizmos, hitGizmoDuration);
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
                _notifyRunner.StopPlayback();
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

            // 현재 시점의 클립(섹션)
            var tc = _config.Clips[_active];

            float deltaTime = Time.deltaTime * Mathf.Max(0f, localTimeScale);
            float previousNtRaw = SectionNormalizedTime(tc);
            _clipTime += deltaTime;
            float ntRaw = SectionNormalizedTime(tc);

            _notifyRunner.Tick(tc, previousNtRaw, ntRaw, deltaTime);
            PrepareModuleTick(previousNtRaw);                   // Module 실행 전 상태 초기화
            TickModules(tc, ntRaw);                             // 현재 Section의 Module들을 갱신

            // 클립 고유 링크(상태 전이) 먼저, 그 다음 config 공통 링크(Global) 평가
            if (TryLinks(tc.Links, tc, ntRaw)) return;
            if (_config.GlobalLinks != null && TryLinks(_config.GlobalLinks, tc, ntRaw)) return;
        }

        // links를 순서대로 평가해 첫 발동 링크를 타고 전이한다. 전이했으면 true.
        private bool TryLinks(List<ClipLink> links, TrackClip tc, float ntRaw)
        {
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw; // 현재 반복 주기 안에서의 진행도
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

                // 이 Link의 조건이 현재 입력·방향·상태와 일치하지 않으면 다음 Link로 넘어가라.
                // 버퍼 입력이 Normal인가? 현재 방향 입력이 Forward인가?
                if (!Cond(link).Matches(_condCtx)) continue;

                // 이번 프레임에 이 Link를 실행할 것인가?
                bool fire = false;

                switch (link.Timing)
                {
                    case LinkTiming.WhenMatched:
                        fire = (link.WindowStart <= p  && p <= link.WindowEnd);
                        break;

                    case LinkTiming.OnEnd:
                        fire = (p >= EndThreshold(tc));
                        break;
                }

                if (fire)
                {
                    Cond(link).Consume(_condCtx);   // 입력을 요구한 조건만 버퍼 소비(InputCondition)
                    TakeLink(link);                 // Link를 따라 실제 다음 Section으로 이동
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

        // OnEnd 발동 기준. 수동값이 없으면 클립의 마지막 프레임 시작 지점을 사용한다.
        private float EndThreshold(TrackClip tc)
        {
            float configuredThreshold =
                _config != null ? _config.DoneThreshold : 0f;
            if (0f < configuredThreshold && configuredThreshold < 1f)
                return configuredThreshold;

            if (tc.Clip != null && tc.Clip.frameRate > 0f)
            {
                float frameCount = tc.Clip.length * tc.Clip.frameRate;
                if (frameCount > 1f)
                    return Mathf.Clamp01(1f - 1f / frameCount);
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

            // 같은 클립(섹션) 이라면
            bool sameSectionReentry = (ti == _active);
            _active = ti;
            PlayActive(link.BlendDuration, link.EntryOffset, sameSectionReentry);
        }

        // startOffset(normalizedTime, 0~1) = 대상 클립을 그 지점부터 재생(중간 프레임 진입). 0 = 처음부터.
        private void PlayActive(float blend, float startOffset = 0f,
            bool sameSectionReentry = false)
        {
            string destinationSection = _config.Clips[_active].SectionName;
            _notifyRunner.PrepareForSection(
                destinationSection, sameSectionReentry);
            ResetSectionEntryState();

            TrackClip tc = _config.Clips[_active];
            if (tc.Clip == null)
            {
                _notifyRunner.ClearSectionStateForMissingClip();
                return;
            }

            float offsetSeconds = SetSectionStartTime(tc, startOffset);
            PlaySectionAnimation(tc, blend, offsetSeconds);
            ResetMoverForSection(tc);
            EnterSectionModules(tc);
            _notifyRunner.EnterSection(
                tc, startOffset, sameSectionReentry);
        }

        private void ResetSectionEntryState()
        {
            _clipTime = 0f;
            _latched.Clear();

            // 무적과 패링은 해당 SectionModule의 활성 구간에서만 다시 켜진다.
            Signals.Invulnerable = false;
            Signals.ParryActive = false;
        }

        private float SetSectionStartTime(TrackClip tc, float startOffset)
        {
            if (startOffset <= 0f || tc.Clip.length <= 0f) return 0f;

            float normalizedOffset = Mathf.Clamp01(startOffset);
            _clipTime = normalizedOffset * tc.Clip.length
                / Mathf.Max(0.01f, tc.Speed);

            // Animator의 시작 위치는 재생 속도와 무관한 클립 초 단위다.
            return normalizedOffset * tc.Clip.length;
        }

        private void PlaySectionAnimation(TrackClip tc, float blend, float offsetSeconds)
        {
            Ctx.Animator.Play(tc.Clip.name, blend, offsetSeconds);

            // 비주얼과 로직의 재생 속도가 다르면 OnEnd 전환 시점이 어긋난다.
            Ctx.Animator.ApplyAnimatorSpeed(tc.Speed);
        }

        private void ResetMoverForSection(TrackClip tc)
        {
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
        }

        private void EnterSectionModules(TrackClip tc)
        {
            // 기본 상태를 만든 뒤 모듈이 필요한 Section 기능만 순서대로 켠다.
            _sc.FacedInputThisEnter = false;
            _sc.EntryForward = Ctx.Transform != null
                ? Ctx.Transform.forward
                : Vector3.forward;
            _sc.EntryMoveDirection = Ctx.Mover.MoveDirection;
            _sc.PreviousNormalizedTime = SectionNormalizedTime(tc);
            for (int i = 0; i < tc.Modules.Count; i++)
                tc.Modules[i]?.OnEnter(tc, _sc);
        }

        // 섹션 진입 후 경과 시간을 normalizedTime으로 변환 (Speed 반영, 루프는 계속 증가)
        private float SectionNormalizedTime(TrackClip tc)
        {
            if (tc.Clip == null || tc.Clip.length <= 0f) return 1f;
            return _clipTime * Mathf.Max(0.01f, tc.Speed) / tc.Clip.length;
        }

        // 모듈이 매 프레임 결과를 다시 계산하도록 이전 프레임의 임시 상태를 기본값으로 되돌린다.
        private void PrepareModuleTick(float previousNormalizedTime)
        {
            Ctx.Mover.AllowRotation = true;
            Ctx.Mover.WarpWindowActive = false;
            Ctx.Mover.FaceWindowActive = false;
            Ctx.Mover.RootRotationWindowActive = false;
            _sc.PreviousNormalizedTime = previousNormalizedTime;
        }

        // 섹션 모듈 매 프레임 구동 (i-frame 등). 있는 모듈만 실행.
        private void TickModules(TrackClip tc, float ntRaw)
        {
            var mods = tc.Modules;
            for (int i = 0; i < mods.Count; i++)
                mods[i]?.Tick(tc, ntRaw, _sc);
        }

        // 현재는 항상 활성이라 호출되지 않지만, 무적 누수 방지를 위한 정리 진입점으로 남겨둔다.
        public void Exit()
        {
            _notifyRunner.Exit();
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
