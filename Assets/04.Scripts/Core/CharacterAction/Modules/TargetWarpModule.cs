using UnityEngine;

namespace ZZZ
{
    [System.Serializable]
    public class TargetWarpModule : WindowModule
    {
        public float StopDistance = 1.2f;

        public TargetWarpModule()
        {
            Start = 0f;
            End = 0.4f;
        }

        public override void OnEnter(TrackClip tc, SectionContext c)
        {
            if (tc.MoveMode != MoveMode.RootMotion) return;
            Transform target = c.Ctx.Mover.FindTarget();
            if (target != null)
                c.Ctx.Mover.SetWarpTranslationTarget(target, StopDistance);
        }

        public override void Tick(TrackClip tc, float nt, SectionContext c)
            => c.Ctx.Mover.WarpWindowActive |= InWindow(tc, nt);

        public override string MenuName => "타깃 이동 워프";
        public override string DisplayName =>
            $"타깃 워프  {Start:F2}~{End:F2} · 정지 {StopDistance:F2}m";
    }
}
