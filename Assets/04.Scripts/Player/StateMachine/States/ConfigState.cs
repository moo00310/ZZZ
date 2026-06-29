using System.Collections.Generic;
using UnityEngine;
using ZZZ;

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
        private float           _clipTime; // 현재 섹션 진입 후 경과 시간(초) — 전환마다 0으로 리셋

        // OnEndIfMatched 링크의 윈도우 래치 상태 — 섹션 진입마다 비운다(섹션 스코프).
        private readonly HashSet<ClipLink> _latched = new HashSet<ClipLink>();

        // 링크 조건(LinkCondition) 평가용 컨텍스트 — 플레이어 입력/방향 질의를 공급한다.
        private readonly ILinkConditionContext _condCtx;

        // 링크 조건 접근. 모든 링크는 Condition을 갖지만, 비어 있는(가드 없는) 링크는 Always로 취급한다.
        private static readonly AlwaysCondition s_always = new AlwaysCondition();
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
            if (_config == null || _config.Clips.Count == 0) { _active = -1; return; }

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
            _clipTime += Time.deltaTime;
            float ntRaw = SectionNormalizedTime(tc);

            FireNotifies(tc, ntRaw);
            UpdateWarpWindow(tc, ntRaw);
            UpdateFaceWindow(tc, ntRaw);
            UpdateRotationWindows(tc, ntRaw);
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
            _clipTime = 0f;   // 새 섹션 진입 → 타임라인 리셋
            _latched.Clear(); // 새 섹션 → OnEndIfMatched 윈도우 래치 리셋
            Signals.Invulnerable = false;    // 섹션 진입 시 무적 해제 — i-frame 모듈이 윈도우 동안만 다시 켠다
            Signals.ParryActive  = false;    // 섹션 진입 시 패링 해제 — parry 모듈이 윈도우 동안만 다시 켠다

            var tc = _config.Clips[_active];
            if (tc.Clip == null) { _notifyFired = null; return; }

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
            Ctx.Mover.SmoothLoopSpeed = tc.SmoothLoopSpeed;   // 루프 전진 평속화 (틱 제거) — 섹션별 토글
            Ctx.Mover.BackMotionScale = tc.BackMotionScale;   // 후진(-Z) 루트모션 증폭 — 섹션별 배율
            Ctx.Mover.ExtractRootRotation = tc.SectionTurn;   // 턴 섹션이면 Root yaw를 transform에 적용
            if (tc.SectionTurn) Ctx.Mover.FlushRootRotation();   // 진입(재진입 포함) 시 회전 추출 baseline/누적 리셋
            // 회전 윈도우 초기값 (Update 전 1프레임 일관성) — 이후 매 프레임 UpdateRotationWindows가 갱신
            UpdateRotationWindows(tc, 0f);
            if (tc.MoveMode == MoveMode.RootMotion) Ctx.Mover.FlushRootPos();

            // 진입 스냅 — 이동 입력이 있으면 그쪽으로 즉시 회전 후(LockRotation이면) 고정.
            // 콤보가 새 섹션으로 진입하는 순간이 곧 "콤보 사이" 재조준 지점이 된다.
            // 입력이 있으면 아래 적 방향 조준(FaceTarget 진입 스냅)보다 우선한다.
            bool facedInput = false;
            if (tc.FaceInputOnEnter)
            {
                Vector3 inputDir = Ctx.Mover.MoveDirection;   // 카메라 기준 WASD 방향
                if (inputDir.sqrMagnitude > 0.0001f)
                {
                    Ctx.Mover.FaceToward(inputDir);
                    facedInput = true;
                }
            }

            // 타겟 기반 기능 — 이동 워프(EnableTracking)와 타겟 조준(FaceTarget)은 독립 토글.
            // 하나라도 켜져 있으면 적을 찾는다. 콤보 단마다 재탐색 → 적이 옆으로 빠져도 다음 타가 따라간다.
            Ctx.Mover.ClearWarpTarget();
            if (tc.MoveMode == MoveMode.RootMotion && (tc.EnableTracking || tc.FaceTarget))
            {
                var sensor = Ctx.Mover.EnemySensor;
                var target = sensor != null ? sensor.FindTarget() : null;
                if (target != null)
                {
                    // translate = 이동 워프(EnableTracking), face = 타겟 조준(FaceTarget) — 회전은 트래킹과 무관
                    Ctx.Mover.SetWarpTarget(target, tc.StopDistance,
                        tc.EnableTracking, tc.FaceTarget, tc.FaceTurnSpeed);
                    // 진입 1회 스냅 — FaceWindow가 0부터면 첫 프레임에 즉시 정렬(입력 조준이 있으면 양보).
                    // 이후 FaceWindow 동안의 지속 회전(락온)은 컨트롤러 UpdateWarpFacing이 담당.
                    if (tc.FaceTarget && tc.FaceWindowStart <= 0f && !facedInput)
                        Ctx.Mover.FaceToward(target.position - Ctx.Transform.position);
                }
            }

            // 시작 부스트 (0이면 내부에서 해제) — 매 섹션 진입마다 갱신
            Ctx.Mover.AddStartBoost(tc.StartBoostSpeed, tc.StartBoostTime);

            // 섹션 모듈 진입 (i-frame 등) — Warp가 참조할 수 있게 입력 조준 여부 전달
            _sc.FacedInputThisEnter = facedInput;
            for (int i = 0; i < tc.Modules.Count; i++)
                tc.Modules[i]?.OnEnter(tc, _sc);

            _notifyFired = new bool[tc.Notifies.Count];
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

        // 이동 워프 윈도우 — TrackWindow 안에서만 이동 재조준 작동 (타격 이후 적을 따라 휙 도는 것 방지)
        private void UpdateWarpWindow(TrackClip tc, float ntRaw)
        {
            if (!tc.EnableTracking) { Ctx.Mover.WarpWindowActive = false; return; }
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
            Ctx.Mover.WarpWindowActive = p >= tc.TrackWindowStart && p <= tc.TrackWindowEnd;
        }

        // 타겟 조준 윈도우 — FaceWindow 안에서 컨트롤러가 타겟을 향해 회전. [0,0]이면 진입 1회(스냅), 넓히면 락온.
        private void UpdateFaceWindow(TrackClip tc, float ntRaw)
        {
            if (!tc.FaceTarget) { Ctx.Mover.FaceWindowActive = false; return; }
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
            Ctx.Mover.FaceWindowActive = p >= tc.FaceWindowStart && p <= tc.FaceWindowEnd;
        }

        // 입력 회전 잠금(LockRotation)을 normalizedTime 구간에서만 작동시킨다. End<=Start면 섹션 전체.
        private void UpdateRotationWindows(TrackClip tc, float ntRaw)
        {
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : Mathf.Clamp01(ntRaw);

            if (tc.LockRotation)
            {
                bool windowed = tc.LockWindowEnd > tc.LockWindowStart;
                bool locked   = !windowed || (p >= tc.LockWindowStart && p <= tc.LockWindowEnd);
                Ctx.Mover.AllowRotation = !locked;
            }
            else Ctx.Mover.AllowRotation = true;
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
                if (_notifyFired[i]) continue;
                if (p >= tc.Notifies[i].NormalizedTime)
                {
                    _notifyFired[i] = true;
                    DispatchNotify(tc.Notifies[i]);
                }
            }
        }

        private void DispatchNotify(TrackNotify notify)
        {
            switch (notify.Type)
            {
                case NotifyType.Effect:
                    if (notify.EffectPrefab != null)
                        Object.Instantiate(notify.EffectPrefab,
                            Ctx.Transform.position, Ctx.Transform.rotation);
                    break;
                default:
                    if (!string.IsNullOrEmpty(notify.EventName))
                        Ctx.GameObject.SendMessage(
                            notify.EventName, SendMessageOptions.DontRequireReceiver);
                    break;
            }
        }

        // 현재는 항상 활성이라 호출되지 않지만, 무적 누수 방지를 위한 정리 진입점으로 남겨둔다.
        public void Exit()
        {
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
