using UnityEngine;
using ZZZ.Player.StateMachine;

namespace ZZZ
{
    [System.Serializable]
    public class FaceTargetModule : WindowModule
    {
        public float TurnSpeed = 720f;

        public FaceTargetModule()
        {
            Start = 0f;
            End = 0f;
        }

        public override void OnEnter(TrackClip tc, SectionContext c)
        {
            if (tc.MoveMode != MoveMode.RootMotion) return;
            var sensor = c.Ctx.Mover.EnemySensor;
            Transform target = sensor != null ? sensor.FindTarget() : null;
            if (target == null) return;

            c.Ctx.Mover.SetFacingTarget(target, TurnSpeed);
            if (Start <= 0f && !c.FacedInputThisEnter)
                c.Ctx.Mover.FaceToward(target.position - c.Ctx.Transform.position);
        }

        public override void Tick(TrackClip tc, float nt, SectionContext c)
            => c.Ctx.Mover.FaceWindowActive |= End > Start && InWindow(tc, nt);

        public override string MenuName => "타깃 조준";
        public override string DisplayName =>
            $"타깃 조준  {Start:F2}~{End:F2} · {TurnSpeed:F0}°/s";
    }
}
