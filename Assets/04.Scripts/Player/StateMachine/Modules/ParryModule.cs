using UnityEngine;
using ZZZ.Player.StateMachine;

namespace ZZZ
{
    // 패링 활성 구간 — 윈도우(normalizedTime) 안에서 머신을 ParryActive로 만든다 (ParryAid_Start 스탠스).
    // 이 구간에 적 공격이 닿으면 HitTrigger가 피격 대신 쳐냄(ParryAid_L/H)으로 분기한다.
    // 섹션 진입 시 ConfigState가 ParryActive=false로 리셋하므로, 이 모듈이 윈도우 동안만 다시 켠다.
    // (IFrameModule과 대칭 — i-frame은 '무시', parry는 '반격으로 응수'.)
    [System.Serializable]
    public class ParryModule : SectionModule
    {
        [Range(0f, 1f)] public float Start = 0.05f;
        [Range(0f, 1f)] public float End   = 0.45f;

        public override void Tick(TrackClip tc, float nt, SectionContext c)
        {
            float p = tc.IsLooping ? Mathf.Repeat(nt, 1f) : Mathf.Clamp01(nt);
            c.Machine.ParryActive = p >= Start && p <= End;
        }

        public override string DisplayName => $"Parry  {Start:F2}~{End:F2}";
    }
}
