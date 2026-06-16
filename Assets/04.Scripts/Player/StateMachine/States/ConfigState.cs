using System.Collections.Generic;
using UnityEngine;
using ZZZ;

namespace ZZZ.Player.StateMachine.States
{
    // AnimationConfig(Clips + Links)를 파싱해 구동하는 범용 상태.
    // 링크가 다른 config를 가리키면 현재 config를 갈아끼운다.
    // 모든 State(걷기/콤보/대시 등)를 이 한 클래스 + config로 표현한다.
    public class ConfigState : StateBase
    {
        private readonly AnimationConfig _homeConfig;   // 진입/복귀 기본 config

        private AnimationConfig _config;   // 현재 구동 중 config
        private int             _active;   // 현재 클립 인덱스
        private bool[]          _notifyFired;
        private float           _clipTime; // 현재 섹션 진입 후 경과 시간(초) — 전환마다 0으로 리셋

        public ConfigState(PlayerStateContext ctx, PlayerStateMachine machine,
            AnimationConfig homeConfig)
            : base(ctx, machine)
        {
            _homeConfig = homeConfig;
        }

        public override void Enter()
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
        private void SwitchConfig(AnimationConfig config, string section, float blend)
        {
            _config = config;
            if (_config == null || _config.Clips.Count == 0) { _active = -1; return; }

            int idx = !string.IsNullOrEmpty(section)
                ? _config.IndexOfSection(section)
                : _config.IndexOfSection(_config.EntrySection);
            _active = idx >= 0 ? idx : 0;
            PlayActive(blend);
        }

        public override void Update()
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
            UpdateSectionTurn(tc, ntRaw);

            MoveDir moveDir = Ctx.Controller.CurrentMoveDir;

            // 클립 고유 링크 먼저, 그 다음 config 공통 링크(Global) 평가
            if (TryLinks(tc.Links, tc, ntRaw, moveDir)) return;
            if (_config.GlobalLinks != null && TryLinks(_config.GlobalLinks, tc, ntRaw, moveDir)) return;
        }

        // links를 순서대로 평가해 첫 발동 링크를 타고 전이한다. 전이했으면 true.
        private bool TryLinks(List<ClipLink> links, TrackClip tc, float ntRaw, MoveDir moveDir)
        {
            foreach (var link in links)
            {
                // 조건(공격+방향)이 안 맞으면 어떤 타이밍이든 발동 안 함
                if (!ConditionMatches(link, moveDir)) continue;

                float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
                bool fire = false;
                switch (link.Timing)
                {
                    case LinkTiming.WhenMatched:
                        fire = p >= link.WindowStart && p <= link.WindowEnd;
                        break;

                    case LinkTiming.OnWindowMiss:
                        // 윈도우 끝을 지나도록 조건이 유지되면 발동 (캔슬/타임아웃) — WhenMatched 링크보다 뒤에 둘 것
                        fire = p > link.WindowEnd;
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
            // 다른 config로 전이
            if (link.TargetConfig != null && link.TargetConfig != _config)
            {
                SwitchConfig(link.TargetConfig, link.TargetSection, link.BlendDuration);
                return;
            }

            // 같은 config 내 섹션 전이 (대상 없으면 home으로 복귀)
            int ti = _config.IndexOfSection(link.TargetSection);
            if (ti < 0)
            {
                SwitchConfig(_homeConfig, null, link.BlendDuration);
                return;
            }
            _active = ti;
            PlayActive(link.BlendDuration);
        }

        private void PlayActive(float blend)
        {
            _clipTime = 0f;   // 새 섹션 진입 → 타임라인 리셋
            Ctx.Controller.SetBodyYaw(0f);   // 직전 섹션의 연출 회전 해제 → 원래 facing 복귀

            var tc = _config.Clips[_active];
            if (tc.Clip == null) { _notifyFired = null; return; }

            Ctx.Animator.Play(tc.Clip.name, blend);
            // 비주얼 재생 속도를 로직 타임라인(SectionNormalizedTime의 Speed)과 일치시킨다.
            // 안 맞추면 애니는 1배속으로 끝나 freeze되고 로직만 Speed배로 흘러 OnEnd가 늦게/일찍 발동(전환 딜레이).
            Ctx.Animator.ApplyAnimatorSpeed(tc.Speed);

            // 이동 방식 적용
            Ctx.Controller.UseCodeMovement = tc.MoveMode != MoveMode.RootMotion;
            Ctx.Controller.AllowRotation   = !tc.LockRotation;
            if (tc.MoveMode == MoveMode.RootMotion) Ctx.Controller.FlushRootPos();

            // 타겟 워프 — 전방 적이 있으면 루트모션을 적 방향으로 보정 (없으면 원본 그대로)
            // 콤보 단마다 재탐색 → 적이 옆으로 빠져도 다음 타가 따라간다
            Ctx.Controller.ClearWarpTarget();
            if (tc.MoveMode == MoveMode.RootMotion && tc.EnableTracking)
            {
                var sensor = Ctx.Controller.EnemySensor;
                var target = sensor != null ? sensor.FindTarget() : null;
                if (target != null)
                {
                    Ctx.Controller.SetWarpTarget(target, tc.StopDistance);
                    if (tc.SnapRotation)
                        Ctx.Controller.FaceToward(target.position - Ctx.Transform.position);
                }
            }

            // 시작 부스트 (0이면 내부에서 해제) — 매 섹션 진입마다 갱신
            Ctx.Controller.AddStartBoost(tc.StartBoostSpeed, tc.StartBoostTime);

            _notifyFired = new bool[tc.Notifies.Count];
        }

        // 섹션 진입 후 경과 시간을 normalizedTime으로 변환 (Speed 반영, 루프는 계속 증가)
        private float SectionNormalizedTime(TrackClip tc)
        {
            if (tc.Clip == null || tc.Clip.length <= 0f) return 1f;
            return _clipTime * Mathf.Max(0.01f, tc.Speed) / tc.Clip.length;
        }

        // 트래킹 윈도우 안에서만 워프 작동 — 타격 이후 적을 따라 휙 도는 것 방지
        private void UpdateWarpWindow(TrackClip tc, float ntRaw)
        {
            if (!tc.EnableTracking) return;
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
            Ctx.Controller.WarpWindowActive = p >= tc.TrackWindowStart && p <= tc.TrackWindowEnd;
        }

        // 공격 연출용 회전 — 윈도우 진행도에 비례한 절대 yaw를 bip001에 얹는다.
        // 매 프레임 절대값으로 세팅(애니 포즈 위 오프셋이라 누적 불필요). 윈도우 이후엔
        // TurnAngle을 유지하다가 섹션 종료 시 PlayActive에서 0으로 복귀.
        private void UpdateSectionTurn(TrackClip tc, float ntRaw)
        {
            if (!tc.SectionTurn) return;
            float p = tc.IsLooping ? Mathf.Repeat(ntRaw, 1f) : Mathf.Clamp01(ntRaw);
            if (p < tc.TurnWindowStart) return;

            float winLen = Mathf.Max(1e-4f, tc.TurnWindowEnd - tc.TurnWindowStart);
            float wp     = Mathf.Clamp01((p - tc.TurnWindowStart) / winLen);   // 윈도우 내 진행도 0~1
            Ctx.Controller.SetBodyYaw(tc.TurnAngle * wp);
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
            => AttackMatches(link.Attack) && MoveMatches(link.Direction, moveDir);

        // 공격 입력 조건 (버퍼된 입력 기준)
        private bool AttackMatches(ComboInput required)
        {
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

        public override void Exit()
        {
            _notifyFired = null;
            Ctx.Controller.ClearWarpTarget();
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
    }
}
