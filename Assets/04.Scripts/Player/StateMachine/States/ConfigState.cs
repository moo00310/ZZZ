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
            if (_config == null || _active < 0 || _active >= _config.Clips.Count) return;

            var   tc    = _config.Clips[_active];
            float ntRaw = Ctx.Animator.GetCurrentNormalizedTime();

            FireNotifies(tc, ntRaw);

            MoveDir moveDir = Ctx.Controller.CurrentMoveDir;

            foreach (var link in tc.Links)
            {
                if (!MoveMatches(link.Move, moveDir)) continue;

                bool fire = false;
                switch (link.Trigger)
                {
                    case LinkTrigger.Input:
                        float p = tc.Loop ? Mathf.Repeat(ntRaw, 1f) : ntRaw;
                        fire = Machine.HasBufferedInput
                            && InputMatches(link.Input, Machine.BufferedInput)
                            && p >= link.WindowStart && p <= link.WindowEnd;
                        break;

                    case LinkTrigger.OnEnd:
                        fire = !tc.Loop && ntRaw >= DoneThreshold();
                        break;

                    case LinkTrigger.WhileMove:
                        fire = true;   // MoveMatches가 이미 통과
                        break;
                }

                if (fire)
                {
                    if (link.Trigger == LinkTrigger.Input) Machine.ConsumeInput();
                    TakeLink(link);
                    return;
                }
            }
        }

        private float DoneThreshold()
            => _config.DoneThreshold > 0f ? _config.DoneThreshold : 0.95f;

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
        private static bool InputMatches(ComboInput required, ComboInput pressed)
            => required == ComboInput.Any || required == pressed;

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
    }
}
