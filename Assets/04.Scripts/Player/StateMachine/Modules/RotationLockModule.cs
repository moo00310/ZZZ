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
}
