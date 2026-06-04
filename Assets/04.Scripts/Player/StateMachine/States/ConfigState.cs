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

            var   tc    = _config.Clips[_active];
            float ntRaw = Ctx.Animator.GetCurrentNormalizedTime();

            FireNotifies(tc, ntRaw);

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

                float p = tc.Loop ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
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
                        fire = !tc.Loop && ntRaw >= EndThreshold(tc);
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
            var tc = _config.Clips[_active];
            if (tc.Clip == null) { _notifyFired = null; return; }

            Ctx.Animator.Play(tc.Clip.name, blend);

            // 이동 방식 적용
            Ctx.Controller.UseCodeMovement = tc.MoveMode != MoveMode.RootMotion;
            if (tc.MoveMode == MoveMode.RootMotion) Ctx.Controller.FlushRootPos();

            _notifyFired = new bool[tc.Notifies.Count];
        }

        private void FireNotifies(TrackClip tc, float ntRaw)
        {
            if (_notifyFired == null) return;
            float p = tc.Loop ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
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
        }

        // ── 에디터 라이브 모니터용 읽기 전용 노출 ──────────────────
        public AnimationConfig CurrentConfig => _config;
        public int             ActiveIndex   => _active;
        public string ActiveSection =>
            (_config != null && _active >= 0 && _active < _config.Clips.Count)
                ? _config.Clips[_active].SectionName : null;
        public float   CurrentNormalizedTime => Ctx.Animator.GetCurrentNormalizedTime();
        public MoveDir CurrentMoveDir        => Ctx.Controller.CurrentMoveDir;
    }
}
