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
        private readonly PlayerStateContext Ctx;
        private readonly PlayerStateMachine Machine;
        private readonly AnimationConfig    _homeConfig;   // 진입/복귀 기본 config
        private readonly SectionContext     _sc;           // 섹션 모듈에 넘기는 핸들 묶음

        private AnimationConfig _config;   // 현재 구동 중 config
        private int             _active;   // 현재 클립 인덱스
        private bool[]          _notifyFired;
        private float           _clipTime; // 현재 섹션 진입 후 경과 시간(초) — 전환마다 0으로 리셋

        // OnEndIfMatched 링크의 윈도우 래치 상태 — 섹션 진입마다 비운다(섹션 스코프).
        private readonly HashSet<ClipLink> _latched = new HashSet<ClipLink>();

        public ConfigState(PlayerStateContext ctx, PlayerStateMachine machine,
            AnimationConfig homeConfig)
        {
            Ctx         = ctx;
            Machine     = machine;
            _homeConfig = homeConfig;
            _sc         = new SectionContext { Ctx = ctx, Machine = machine };
        }

        public void Enter()
        {
            SwitchConfig(_homeConfig, null, 0f);
            Machine.ConsumeInput();
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

            // 섹션 자체 경과 시간으로 nt 계산 (CrossFade 중 애니메이터가 옛 클립 시간을
            // 반환하는 문제 회피 → 전환마다 0부터 다시 시작)
            _clipTime += Time.deltaTime;
            float ntRaw = SectionNormalizedTime(tc);

            FireNotifies(tc, ntRaw);
            UpdateWarpWindow(tc, ntRaw);
            UpdateFaceWindow(tc, ntRaw);
            UpdateRotationWindows(tc, ntRaw);
            TickModules(tc, ntRaw);

            MoveDir moveDir = Ctx.Controller.CurrentMoveDir;

            // 클립 고유 링크 먼저, 그 다음 config 공통 링크(Global) 평가
            if (TryLinks(tc.Links, tc, ntRaw, moveDir)) return;
            if (_config.GlobalLinks != null && TryLinks(_config.GlobalLinks, tc, ntRaw, moveDir)) return;
        }

        // links를 순서대로 평가해 첫 발동 링크를 타고 전이한다. 전이했으면 true.
        private bool TryLinks(List<ClipLink> links, TrackClip tc, float ntRaw, MoveDir moveDir)
        {
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
            foreach (var link in links)
            {
                // OnEndIfMatched: 조건을 '발동 시점'이 아니라 '윈도우 구간'에서 보고 래치한다.
                // 그래서 top의 ConditionMatches 게이트를 거치지 않고 따로 처리(끝에선 입력이 이미 사라짐).
                if (link.Timing == LinkTiming.OnEndIfMatched)
                {
                    if (TryLatchLink(link, tc, p, moveDir)) return true;
                    continue;
                }

                // OnRelease: 이 링크 Attack 키를 뗀 순간 발동(홀드 차지 → 릴리스). press 버퍼를 보는
                // ConditionMatches 게이트를 거치지 않고(릴리스는 press가 아님) 릴리스 신호를 직접 본다.
                if (link.Timing == LinkTiming.OnRelease)
                {
                    if (TryReleaseLink(link, p, moveDir)) return true;
                    continue;
                }

                // 조건(공격+방향)이 안 맞으면 어떤 타이밍이든 발동 안 함
                if (!ConditionMatches(link, moveDir)) continue;

                bool fire = false;
                switch (link.Timing)
                {
                    case LinkTiming.WhenMatched:
                        fire = p >= link.WindowStart && p <= link.WindowEnd;
                        break;

                    case LinkTiming.OnEnd:
                        // 루프 클립도 사이클 끝(wrap된 p)에서 탈출 조건을 검사한다.
                        // 조건(Direction 등)이 안 맞으면 위 ConditionMatches에서 이미 걸러짐.
                        fire = p >= EndThreshold(tc);
                        break;
                }

                if (fire)
                {
                    // 실제 공격 입력을 요구한 링크만 입력 버퍼 소비
                    if (link.Attack != ComboInput.None) Machine.ConsumeInput();
                    TakeLink(link);
                    return true;
                }
            }
            return false;
        }

        // OnEndIfMatched 처리 — 윈도우[Start,End] 안에서 조건이 충족되면 래치(입력은 즉시 소비해
        // 같은 입력이 다른 링크를 오발동시키지 않게 함). 섹션 끝(EndThreshold)에서 래치돼 있으면 전이.
        // 래치는 섹션 진입마다 리셋(_latched). 반환 true = 전이함.
        private bool TryLatchLink(ClipLink link, TrackClip tc, float p, MoveDir moveDir)
        {
            if (!_latched.Contains(link)
                && p >= link.WindowStart && p <= link.WindowEnd
                && ConditionMatches(link, moveDir))
            {
                _latched.Add(link);
                if (link.Attack != ComboInput.None) Machine.ConsumeInput();
            }

            if (p >= EndThreshold(tc) && _latched.Contains(link))
            {
                TakeLink(link);
                return true;
            }
            return false;
        }

        // OnRelease 처리 — [WindowStart,End] 안에서 이 링크의 Attack 키가 '안 눌린'(떼진) 상태면 전이.
        // press 버퍼가 아니라 실제 홀드 상태를 본다: 누르고 있으면 대기(차지 지속), 떼면 발동(홀드 차지 → 발사).
        // 윈도우 시작(WindowStart)이 최소 차지 — 그 전에 떼도 무시되어 windup이 끊기지 않는다. 방향 조건은 동일 검사.
        private bool TryReleaseLink(ClipLink link, float p, MoveDir moveDir)
        {
            if (p < link.WindowStart || p > link.WindowEnd) return false;
            if (Machine.IsInputHeld(link.Attack)) return false;   // 아직 누르고 있음 → 차지 지속
            if (!MoveConditionMatches(link, moveDir)) return false;

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
            Machine.Invulnerable = false;    // 섹션 진입 시 무적 해제 — i-frame 모듈이 윈도우 동안만 다시 켠다
            Machine.ParryActive  = false;    // 섹션 진입 시 패링 해제 — parry 모듈이 윈도우 동안만 다시 켠다

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
            Ctx.Controller.UseCodeMovement = tc.MoveMode != MoveMode.RootMotion;
            Ctx.Controller.SmoothLoopSpeed = tc.SmoothLoopSpeed;   // 루프 전진 평속화 (틱 제거) — 섹션별 토글
            Ctx.Controller.BackMotionScale = tc.BackMotionScale;   // 후진(-Z) 루트모션 증폭 — 섹션별 배율
            Ctx.Controller.ExtractRootRotation = tc.SectionTurn;   // 턴 섹션이면 Root yaw를 transform에 적용
            if (tc.SectionTurn) Ctx.Controller.FlushRootRotation();   // 진입(재진입 포함) 시 회전 추출 baseline/누적 리셋
            // 회전 윈도우 초기값 (Update 전 1프레임 일관성) — 이후 매 프레임 UpdateRotationWindows가 갱신
            UpdateRotationWindows(tc, 0f);
            if (tc.MoveMode == MoveMode.RootMotion) Ctx.Controller.FlushRootPos();

            // 진입 스냅 — 이동 입력이 있으면 그쪽으로 즉시 회전 후(LockRotation이면) 고정.
            // 콤보가 새 섹션으로 진입하는 순간이 곧 "콤보 사이" 재조준 지점이 된다.
            // 입력이 있으면 아래 적 방향 조준(FaceTarget 진입 스냅)보다 우선한다.
            bool facedInput = false;
            if (tc.FaceInputOnEnter)
            {
                Vector3 inputDir = Ctx.Controller.MoveDirection;   // 카메라 기준 WASD 방향
                if (inputDir.sqrMagnitude > 0.0001f)
                {
                    Ctx.Controller.FaceToward(inputDir);
                    facedInput = true;
                }
            }

            // 타겟 기반 기능 — 이동 워프(EnableTracking)와 타겟 조준(FaceTarget)은 독립 토글.
            // 하나라도 켜져 있으면 적을 찾는다. 콤보 단마다 재탐색 → 적이 옆으로 빠져도 다음 타가 따라간다.
            Ctx.Controller.ClearWarpTarget();
            if (tc.MoveMode == MoveMode.RootMotion && (tc.EnableTracking || tc.FaceTarget))
            {
                var sensor = Ctx.Controller.EnemySensor;
                var target = sensor != null ? sensor.FindTarget() : null;
                if (target != null)
                {
                    // translate = 이동 워프(EnableTracking), face = 타겟 조준(FaceTarget) — 회전은 트래킹과 무관
                    Ctx.Controller.SetWarpTarget(target, tc.StopDistance,
                        tc.EnableTracking, tc.FaceTarget, tc.FaceTurnSpeed);
                    // 진입 1회 스냅 — FaceWindow가 0부터면 첫 프레임에 즉시 정렬(입력 조준이 있으면 양보).
                    // 이후 FaceWindow 동안의 지속 회전(락온)은 컨트롤러 UpdateWarpFacing이 담당.
                    if (tc.FaceTarget && tc.FaceWindowStart <= 0f && !facedInput)
                        Ctx.Controller.FaceToward(target.position - Ctx.Transform.position);
                }
            }

            // 시작 부스트 (0이면 내부에서 해제) — 매 섹션 진입마다 갱신
            Ctx.Controller.AddStartBoost(tc.StartBoostSpeed, tc.StartBoostTime);

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
            if (!tc.EnableTracking) { Ctx.Controller.WarpWindowActive = false; return; }
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
            Ctx.Controller.WarpWindowActive = p >= tc.TrackWindowStart && p <= tc.TrackWindowEnd;
        }

        // 타겟 조준 윈도우 — FaceWindow 안에서 컨트롤러가 타겟을 향해 회전. [0,0]이면 진입 1회(스냅), 넓히면 락온.
        private void UpdateFaceWindow(TrackClip tc, float ntRaw)
        {
            if (!tc.FaceTarget) { Ctx.Controller.FaceWindowActive = false; return; }
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
            Ctx.Controller.FaceWindowActive = p >= tc.FaceWindowStart && p <= tc.FaceWindowEnd;
        }

        // 입력 회전 잠금(LockRotation)을 normalizedTime 구간에서만 작동시킨다. End<=Start면 섹션 전체.
        private void UpdateRotationWindows(TrackClip tc, float ntRaw)
        {
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : Mathf.Clamp01(ntRaw);

            if (tc.LockRotation)
            {
                bool windowed = tc.LockWindowEnd > tc.LockWindowStart;
                bool locked   = !windowed || (p >= tc.LockWindowStart && p <= tc.LockWindowEnd);
                Ctx.Controller.AllowRotation = !locked;
            }
            else Ctx.Controller.AllowRotation = true;
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
                        Ctx.Controller.gameObject.SendMessage(
                            notify.EventName, SendMessageOptions.DontRequireReceiver);
                    break;
            }
        }

        // ── 조건 매칭 ──────────────────────────────────────────────
        // 링크의 공격+방향 조건이 현재 입력 상태와 모두 맞는지
        private bool ConditionMatches(ClipLink link, MoveDir moveDir)
        {
            return AttackMatches(link) && MoveConditionMatches(link, moveDir);
        }

        // 방향 조건만 (Attack 제외). Reverse는 카메라 절대 방향이 아니라 현재 facing과의 관계 → dot으로 별도 판정.
        private bool MoveConditionMatches(ClipLink link, MoveDir moveDir)
            => link.Direction == MoveDir.Reverse ? IsReverseInput()
                                                 : MoveMatches(link.Direction, moveDir);

        // 입력이 현재 진행(facing) 방향의 반대쪽(>135도)인가 — 180 턴 전이 조건
        private bool IsReverseInput()
        {
            Vector3 inputDir = Ctx.Controller.MoveDirection;   // 카메라 기준 월드 입력 방향
            if (inputDir.sqrMagnitude < 0.0001f) return false; // 입력 없으면 반대 아님
            return Vector3.Dot(Ctx.Transform.forward, inputDir) < -0.707f;
        }

        // 공격 입력 조건. 기본은 누름 버퍼(짧은 선입력) 기준.
        // RequireHeld면 그 키가 "지금 눌려있는지(held)"로 판정 — 차지 루프(OnEnd 자기-루프) 등.
        private bool AttackMatches(ClipLink link)
        {
            var required = link.Attack;

            // 홀드 조건 — 특정 키가 현재 눌려있으면 충족. None/Any는 홀드 개념이 없어 버퍼 폴백.
            if (link.RequireHeld && required != ComboInput.None && required != ComboInput.Any)
                return Machine.IsInputHeld(required);

            switch (required)
            {
                case ComboInput.None: return !Machine.HasBufferedInput;             // 공격 없을 때
                case ComboInput.Any:  return Machine.HasBufferedInput;              // 아무 공격
                default:              return Machine.HasBufferedInput               // 특정 공격
                                          && Machine.BufferedInput == required;
            }
        }

        private static bool MoveMatches(MoveDir required, MoveDir current)
        {
            switch (required)
            {
                case MoveDir.Any:    return true;
                case MoveDir.Moving: return current != MoveDir.Neutral;
                default:             return required == current;
            }
        }

        // 현재는 항상 활성이라 호출되지 않지만, 무적 누수 방지를 위한 정리 진입점으로 남겨둔다.
        public void Exit()
        {
            _notifyFired = null;
            Ctx.Controller.ClearWarpTarget();
            Machine.Invulnerable = false;
            Machine.ParryActive  = false;
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
        public MoveDir CurrentMoveDir        => Ctx.Controller.CurrentMoveDir;

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
                if (l.Attack == input || l.Attack == ComboInput.Any) return true;
            return false;
        }
    }
}
