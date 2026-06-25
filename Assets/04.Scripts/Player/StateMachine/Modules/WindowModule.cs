using UnityEngine;
using ZZZ.Player.StateMachine;

namespace ZZZ
{
    // 윈도우 기반 모듈 공통 베이스 — [Start, End] normalizedTime 구간 동안 무언가를 활성화한다.
    // 구간 값(Start/End)만 공유하고, 그 구간에 '무엇을 켜는지'(Tick)는 상속이 정의한다 (무적/패링 등).
    // 새 윈도우 모듈 = 이 클래스 상속 1개 추가 → 툴의 추가 메뉴/구간 편집이 자동으로 잡힌다.
    [System.Serializable]
    public abstract class WindowModule : SectionModule
    {
        [Range(0f, 1f)] public float Start = 0.1f;
        [Range(0f, 1f)] public float End   = 0.5f;

        // 현재 normalizedTime이 윈도우 안인지 (루프 wrap 처리 포함)
        protected bool InWindow(TrackClip tc, float nt)
        {
            float p = tc.IsLooping ? Mathf.Repeat(nt, 1f) : Mathf.Clamp01(nt);
            return p >= Start && p <= End;
        }
    }
}
