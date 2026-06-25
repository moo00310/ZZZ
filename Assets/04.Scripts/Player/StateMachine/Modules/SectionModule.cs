using ZZZ.Player.StateMachine;

namespace ZZZ
{
    // 한 섹션(TrackClip)에 붙는 기능 단위 — 진입 시 1회(OnEnter) + 매 프레임(Tick) 구동.
    // TrackClip.Modules에 [SerializeReference]로 폴리모픽 직렬화된다.
    // 새 연출/판정 기능 = 이 클래스 상속 1개 추가 (ConfigState는 안 건드림).
    [System.Serializable]
    public abstract class SectionModule
    {
        public virtual void OnEnter(TrackClip tc, SectionContext c) { }
        public virtual void Tick(TrackClip tc, float nt, SectionContext c) { }

        // 인스펙터/툴 표시용 — DisplayName은 값 포함 인스턴스 요약, MenuName은 추가 메뉴에 뜨는 타입 이름.
        public virtual string DisplayName => GetType().Name;
        public virtual string MenuName    => GetType().Name;
    }
}
