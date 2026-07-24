using ZZZ.Player.StateMachine;

namespace ZZZ
{
    [System.Serializable]
    public class SmoothLoopSpeedModule : SectionModule
    {
        public override void OnEnter(TrackClip tc, SectionContext c)
            => c.Ctx.Mover.SmoothLoopSpeed = true;

        public override string MenuName => "루프 이동 평속화";
        public override string DisplayName => "루프 이동 평속화";
    }

    [System.Serializable]
    public class BackMotionScaleModule : SectionModule
    {
        public float Scale = 2f;

        public override void OnEnter(TrackClip tc, SectionContext c)
            => c.Ctx.Mover.BackMotionScale = Scale;

        public override string MenuName => "후진 루트모션 배율";
        public override string DisplayName => $"후진 루트모션  ×{Scale:F2}";
    }
}
