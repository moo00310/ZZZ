using UnityEngine;


namespace ZZZ
{
    // ConfigState(애니메이션 config 러너)가 캐릭터에 요구하는 추상 표면들.
    // 플레이어(PlayerMotor/CharacterAnimatorBridge/PlayerActionController)와 몬스터가 각자 구현해
    // 같은 ConfigState 엔진을 공유한다. ConfigState는 이 인터페이스들에만 의존(구상 타입 비의존).

    // 이동/회전/루트모션/타겟 표면 — ConfigState.PlayActive 및 매 프레임 윈도우 갱신이 호출.
    // 몬스터는 워프/Face/StartBoost를 빈 구현(no-op)으로 두고 루트모션·회전만 실제 구현해도 된다.
    public interface IConfigMover
    {
        Vector3 ViewForward { get; }
        bool  UseCodeMovement    { get; set; }   // 루트모션 안 쓰고 코드 이동(중력만)인지
        bool  AllowRotation      { get; set; }   // false면 회전 잠금(피격/경직)
        bool  ExtractRootRotation{ get; set; }   // Bip001-Root 상대 회전을 적용하는 턴 섹션인지
        bool  RootRotationWindowActive { get; set; } // 상대 회전 yaw를 적용하는 구간
        RootMotionRotationAxis RootRotationSourceAxis { get; set; }
        float RootRotationScale { get; set; }
        float RootRotationTargetAngle { get; set; }
        bool KillRootRotation { get; set; }
        float BackMotionScale    { get; set; }   // 후진(-Z) 루트모션 증폭 배율
        bool  WarpWindowActive   { get; set; }   // 이동 워프 윈도우 안인지(매 프레임 갱신)
        bool  FaceWindowActive   { get; set; }   // 타겟 조준 윈도우 안인지(매 프레임 갱신)

        Vector3 MoveDirection  { get; }   // 카메라 기준 입력 방향(몬스터는 0)
        MoveDir CurrentMoveDir { get; }   // 현재 이동 방향(절대 enum)
        Transform FindTarget();   // 워프/조준에 사용할 현재 타겟 획득(없으면 null)

        void FlushRootRotation();   // 섹션 턴의 누적 회전량 리셋
        void FaceToward(Vector3 worldDir);   // 즉시/즉회전으로 worldDir을 향함
        void MoveBy(Vector3 worldDelta);     // 모듈이 요청한 추가 수평 이동
        void ClearWarpTarget();
        void SetWarpTranslationTarget(Transform target, float stopDistance);
        void SetFacingTarget(Transform target, float faceTurnSpeed);
        void AddStartBoost(float speed, float duration);   // 시작 부스트(0이면 해제)
    }

    // 애니메이터 클립 재생 표면 — config 섹션의 클립을 이름으로 CrossFade.
    public interface IAnimatorBridge
    {
        void ApplyAnimatorSpeed(float speed = 1f);
        void Play(string stateName, float crossFade = 0.01f, float fixedTimeOffset = 0f);
        void PlayHitShake();
    }

    // ConfigState가 접근하는 공유 컴포넌트 묶음 — 구상 데이터 홀더.
    // 인터페이스가 아니라 구상 클래스다: 다형성이 필요한 건 Mover/Animator 둘뿐이고(아래 필드 타입),
    // '묶음' 자체는 캐릭터마다 바꿔치기할 일이 없다. 플레이어/몬스터가 각자 채워 ConfigState에 넘긴다.
    public sealed class ConfigContext
    {
        public IConfigMover    Mover      { get; set; }
        public IAnimatorBridge Animator   { get; set; }

        public Transform       Transform  { get; set; }
    }

    // ConfigState가 머신에 보내는 신호 — 무적/패링(섹션 진입 시 리셋, 모듈이 윈도우 동안 재설정),
    // 입력 버퍼 소비(몬스터는 no-op).
    public interface IConfigSignals
    {
        bool Invulnerable { get; set; }
        bool ParryActive  { get; set; }
        void ConsumeInput();
    }

    // 피격 반응처럼 현재 이벤트를 일으킨 정확한 상대가 필요한 진입 모듈이 선택적으로 사용한다.
    public interface IReactionTargetProvider
    {
        Transform ReactionTarget { get; }
    }

    // 에디터/HUD 라이브 모니터가 읽는 런타임 상태 표면 — 플레이어/몬스터 머신이 공통 구현.
    // 모니터 툴은 이 인터페이스로만 머신을 다뤄, 씬의 어떤 캐릭터든 골라 추적할 수 있다.
    public interface ILiveMonitor
    {
        AnimationConfig CurrentConfig         { get; }
        int             CurrentClipIndex      { get; }
        string          CurrentSection        { get; }
        float           CurrentNormalizedTime { get; }
        MoveDir         CurrentMoveDir         { get; }
    }

    // 입력 버퍼/홀드 모니터 — 입력 있는 머신(플레이어)만 구현. 라이브바가 옵션으로 캐스팅해 표시.
    // 몬스터처럼 입력이 없는 머신은 ILiveMonitor만 구현하면 된다.
    public interface IInputMonitor
    {
        bool       HasBufferedInput { get; }
        ComboInput BufferedInput    { get; }
        bool       IsInputHeld(ComboInput input);
    }
}
