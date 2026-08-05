using ZZZ.Player.StateMachine;

namespace ZZZ
{
    [System.Serializable]
    public class RotationLockModule : WindowModule
    {
        public RotationLockModule()
        {
            Start = 0f;
            End = 1f;
        }

        public override void Tick(TrackClip tc, float nt, SectionContext c)
        {
            if (InWindow(tc, nt))
                c.Ctx.Mover.AllowRotation = false;
        }

        public override string MenuName => "회전 잠금";
        public override string DisplayName => $"회전 잠금  {Start:F2}~{End:F2}";
    }

    [System.Serializable]
    public class RootRotationKillModule : SectionModule
    {
        public override void OnEnter(TrackClip tc, SectionContext c)
            => c.Ctx.Mover.KillRootRotation = true;

        public override void Tick(TrackClip tc, float nt, SectionContext c)
            => c.Ctx.Mover.KillRootRotation = true;

        public override string MenuName => "루트 모션 회전 제거";
        public override string DisplayName => "루트 모션 회전 제거 · 진입 방향 유지";
    }
}
