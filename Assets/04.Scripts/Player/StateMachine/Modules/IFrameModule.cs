using ZZZ.Player.StateMachine;

namespace ZZZ
{
    // 무적 구간 — 윈도우(Start~End) 안에서 머신을 무적으로 만들어 TriggerHit를 무시한다 (회피 등).
    // 섹션 진입 시 ConfigState가 Invulnerable=false로 리셋하므로, 이 모듈이 윈도우 동안만 다시 켠다.
    [System.Serializable]
    public class IFrameModule : WindowModule
    {
        public override void Tick(TrackClip tc, float nt, SectionContext c)
            => c.Machine.Invulnerable = InWindow(tc, nt);

        public override string MenuName    => "무적 구간 (I-Frame)";
        public override string DisplayName => $"무적  {Start:F2}~{End:F2}";
    }
}
