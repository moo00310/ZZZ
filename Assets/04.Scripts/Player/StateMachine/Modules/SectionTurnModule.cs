using ZZZ.Player.StateMachine;

namespace ZZZ
{
    [System.Serializable]
    public class SectionTurnModule : WindowModule
    {
        public SectionTurnModule()
        {
            Start = 0f;
            End = 1f;
        }

        public override void OnEnter(TrackClip tc, SectionContext c)
        {
            if (tc.MoveMode != MoveMode.RootMotion) return;
            c.Ctx.Mover.ExtractRootRotation = true;
            c.Ctx.Mover.RootRotationWindowActive = true;
            c.Ctx.Mover.FlushRootRotation();
        }

        public override void Tick(TrackClip tc, float nt, SectionContext c)
        {
            c.Ctx.Mover.ExtractRootRotation = true;
            c.Ctx.Mover.RootRotationWindowActive = InWindow(tc, nt);
        }

        public override string MenuName => "섹션 턴 (Root)";
        public override string DisplayName => $"섹션 턴  {Start:F2}~{End:F2}";
    }
}
