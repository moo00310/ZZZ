using UnityEngine;

namespace ZZZ
{
    // ConfigState(애니메이션 config 러너)가 캐릭터에 요구하는 추상 표면들.
    // 플레이어(PlayerController/AnimatorBridge/PlayerStateMachine)와 몬스터가 각자 구현해
    // 같은 ConfigState 엔진을 공유한다. ConfigState는 이 인터페이스들에만 의존(구상 타입 비의존).

    // 이동/회전/루트모션/타겟 표면 — ConfigState.PlayActive 및 매 프레임 윈도우 갱신이 호출.
    // 몬스터는 워프/Face/StartBoost를 빈 구현(no-op)으로 두고 루트모션·회전만 실제 구현해도 된다.
    public interface IConfigMover
    {
        bool  UseCodeMovement    { get; set; }   // 루트모션 안 쓰고 코드 이동(중력만)인지
        bool  AllowRotation      { get; set; }   // false면 회전 잠금(피격/경직)
        bool  SmoothLoopSpeed    { get; set; }   // 루프 전진 평속화(틱 제거)
        bool  ExtractRootRotation{ get; set; }   // Root yaw를 transform에 추출(턴 섹션)
        float BackMotionScale    { get; set; }   // 후진(-Z) 루트모션 증폭 배율
        bool  WarpWindowActive   { get; set; }   // 이동 워프 윈도우 안인지(매 프레임 갱신)
        bool  FaceWindowActive   { get; set; }   // 타겟 조준 윈도우 안인지(매 프레임 갱신)

        Vector3 MoveDirection  { get; }   // 카메라 기준 입력 방향(몬스터는 0)
        MoveDir CurrentMoveDir { get; }   // 현재 이동 방향(절대 enum)
        ZZZ.Combat.EnemySensor EnemySensor { get; }   // 워프/조준 타겟 탐색기(없으면 null)

        void FlushRootPos();        // 루트모션 위치 추출 baseline 리셋(섹션 진입)
        void FlushRootRotation();   // 루트 회전 추출 baseline/누적 리셋(섹션 진입)
        void FaceToward(Vector3 worldDir);   // 즉시/즉회전으로 worldDir을 향함
        void ClearWarpTarget();
        void SetWarpTarget(Transform target, float stopDistance, bool translate,
            bool face = false, float faceTurnSpeed = 720f);
        void AddStartBoost(float speed, float duration);   // 시작 부스트(0이면 해제)
    }

    // 애니메이터 클립 재생 표면 — config 섹션의 클립을 이름으로 CrossFade.
    public interface IAnimatorBridge
    {
        void ApplyAnimatorSpeed(float speed = 1f);
        void Play(string stateName, float crossFade = 0.01f, float fixedTimeOffset = 0f);
    }

    // ConfigState가 접근하는 공유 컴포넌트 묶음(PlayerStateContext의 일반형).
    public interface IConfigContext
    {
        IConfigMover    Mover      { get; }
        IAnimatorBridge Animator   { get; }
        Transform       Transform  { get; }
        GameObject      GameObject { get; }   // Notify(SendMessage) 대상
    }

    // ConfigState가 머신에 보내는 신호 — 무적/패링(섹션 진입 시 리셋, 모듈이 윈도우 동안 재설정),
    // 입력 버퍼 소비(몬스터는 no-op).
    public interface IConfigSignals
    {
        bool Invulnerable { get; set; }
        bool ParryActive  { get; set; }
        void ConsumeInput();
    }
}
