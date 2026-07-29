using UnityEngine;
using ZZZ;

namespace ZZZ.Monster
{
    // 몬스터 이동/회전 — IConfigMover 구현. v1(Idle+Hit, 제자리 재생)은 실제 이동이 없어
    // ConfigState가 세팅하는 값들을 '보관만' 하고 구동하지 않는다(no-op). FaceToward만 즉시 회전으로 구현.
    // 루트모션 이동/넉백·워프·추격은 후속(필요 시 PlayerController의 루트 추출 로직을 공유/이식).
    public class MonsterController : MonoBehaviour, IConfigMover
    {
        public Vector3 ViewForward => transform.forward;
        // ── ConfigState가 섹션마다 세팅 — v1에선 보관만 ──
        public bool  UseCodeMovement     { get; set; } = true;
        public bool  AllowRotation       { get; set; } = true;
        public bool  SmoothLoopSpeed     { get; set; }
        public bool  ExtractRootRotation { get; set; }
        public bool  RootRotationWindowActive { get; set; } = true;
        public float RootRotationScale { get; set; } = 1f;
        public float RootRotationTargetAngle { get; set; }
        public bool KillRootRotation { get; set; }
        public float BackMotionScale     { get; set; } = 1f;
        public bool  WarpWindowActive    { get; set; }
        public bool  FaceWindowActive    { get; set; }

        public Vector3 MoveDirection  => Vector3.zero;       // 입력 없음
        public MoveDir CurrentMoveDir => MoveDir.Neutral;
        public ZZZ.Combat.EnemySensor EnemySensor => null;   // 워프/추격 도입 시 연결

        public void FlushRootPos()      { }
        public void FlushRootRotation() { }
        public void ClearWarpTarget()   { }
        public void SetWarpTranslationTarget(Transform target, float stopDistance) { }
        public void SetFacingTarget(Transform target, float faceTurnSpeed) { }
        public void AddStartBoost(float speed, float duration) { }
        public void MoveBy(Vector3 worldDelta) { }

        // 즉시 yaw 회전 — worldDir(수평) 쪽을 바라본다. v1엔 호출 안 되지만 미리 구현.
        public void FaceToward(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(worldDir);
        }
    }
}
