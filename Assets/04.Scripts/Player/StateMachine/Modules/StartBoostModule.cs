using ZZZ.Player.StateMachine;

namespace ZZZ
{
    [System.Serializable]
    public class StartBoostModule : SectionModule
    {
        public float Speed = 1f;
        public float Duration = 0.15f;

        public override void OnEnter(TrackClip tc, SectionContext c)
            => c.Ctx.Mover.AddStartBoost(Speed, Duration);

        public override string MenuName => "시작 부스트";
        public override string DisplayName => $"시작 부스트  {Speed:F2}m/s · {Duration:F2}s";
    }
}
