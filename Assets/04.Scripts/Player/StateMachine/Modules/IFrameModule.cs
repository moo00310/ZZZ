using UnityEngine;
using ZZZ.Player.StateMachine;

namespace ZZZ
{
    // 무적 구간 — 윈도우(normalizedTime) 안에서 머신을 무적으로 만들어 TriggerHit를 무시한다 (회피 등).
    // 섹션 진입 시 ConfigState가 Invulnerable=false로 리셋하므로, 이 모듈이 윈도우 동안만 다시 켠다.
    [System.Serializable]
    public class IFrameModule : SectionModule
    {
        [Range(0f, 1f)] public float Start = 0.1f;
        [Range(0f, 1f)] public float End   = 0.55f;

        public override void Tick(TrackClip tc, float nt, SectionContext c)
        {
            float p = tc.IsLooping ? Mathf.Repeat(nt, 1f) : Mathf.Clamp01(nt);
            c.Machine.Invulnerable = p >= Start && p <= End;
        }

        public override string DisplayName => $"I-Frame  {Start:F2}~{End:F2}";
    }
}
