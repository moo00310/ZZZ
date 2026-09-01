
namespace ZZZ
{
    // 패링 구간 — 윈도우(Start~End) 안에서 머신을 ParryActive로 만든다 (ParryAid_Start 스탠스).
    // 이 구간에 적 공격이 닿으면 HitTrigger가 피격 대신 쳐냄(ParryAid_L/H)으로 분기한다.
    // 섹션 진입 시 CharacterActionRunner가 ParryActive=false로 리셋하므로, 이 모듈이 윈도우 동안만 다시 켠다.
    // (IFrameModule과 대칭 — i-frame은 '무시', parry는 '반격으로 응수'.)
    [System.Serializable]
    public class ParryModule : WindowModule
    {
        public override void Tick(TrackClip tc, float nt, SectionContext c)
            => c.Machine.ParryActive = InWindow(tc, nt);

        public override string MenuName    => "패링 구간 (Parry)";
        public override string DisplayName => $"패링  {Start:F2}~{End:F2}";
    }
}
