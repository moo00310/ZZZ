namespace ZZZ.Player.StateMachine
{
    // 섹션 모듈이 받는 런타임 핸들 묶음. ConfigState가 1개 만들어 재사용한다.
    public class SectionContext
    {
        public IConfigContext Ctx;       // 공유 컴포넌트 (모듈이 현재 미사용 — 추후 이전용)
        public IConfigSignals Machine;   // 무적/패링 신호 (i-frame/parry 모듈이 사용)

        // 진입 스크래치 — MovementSetup이 입력 방향으로 회전했는지. Warp가 SnapRotation 생략 판단에 쓴다(추후 이전).
        public bool FacedInputThisEnter;
    }
}
