using UnityEngine;

namespace ZZZ.Player.StateMachine.States
{
    public class LocomotionState : StateBase
    {
        private enum Sub
        {
            Idle, IdleLoop,
            WalkStart, WalkLoop, WalkEnd,
            RunLoop, RunEnd,
            TurnBack, Taunt
        }

        private Sub   _sub;
        private float _lastNormalizedTime;
        private float _subTimer;
        private const float k_maxSubDuration = 3f; // 최대 상태 유지 시간

        // 툴에서 읽는 라이브 상태
        public string CurrentSubState    => _sub.ToString();
        public float  CurrentNormalizedT => Ctx.Animator.GetNormalizedTime();

        private const float MoveThreshold = 0.1f;
        private const float TurnBackAngle = 135f;

        public LocomotionState(PlayerStateContext ctx, PlayerStateMachine machine)
            : base(ctx, machine) { }

        public override void Enter()
        {
            _sub = Sub.Idle;
            Ctx.Animator.PlayIdle();
        }

        public override void Update()
        {
            _subTimer += Time.deltaTime;

            switch (_sub)
            {
                case Sub.Idle:      OnIdle();      break;
                case Sub.IdleLoop:  OnIdleLoop();  break;
                case Sub.WalkStart: OnWalkStart(); break;
                case Sub.WalkLoop:  OnWalkLoop();  break;
                case Sub.WalkEnd:   OnWalkEnd();   break;
                case Sub.RunLoop:   OnRunLoop();   break;
                case Sub.RunEnd:    OnRunEnd();    break;
                case Sub.TurnBack:  OnTurnBack();  break;
                case Sub.Taunt:     OnTaunt();     break;
            }
        }

        // ── Sub-state handlers ────────────────────────────────────

        private void OnIdle()
        {
            if (IsMoving())
            {
                GoTo(NeedsTurnBack() ? Sub.TurnBack : Sub.WalkStart);
                return;
            }

            if (IsDone(PlayerAnimatorBridge.Idle))
                GoTo(Sub.IdleLoop);
        }

        private void OnIdleLoop()
        {
            if (!IsMoving()) return;

            if (NeedsTurnBack())
                GoTo(Sub.TurnBack);
            else if (Ctx.IsSprinting)
                GoTo(Sub.RunLoop);
            else
                GoTo(Sub.WalkStart);
        }

        private void OnWalkStart()
        {
            if (!IsDone(PlayerAnimatorBridge.WalkStart)) return;

            if (!IsMoving())          GoTo(Sub.WalkEnd);
            else if (Ctx.IsSprinting) GoTo(Sub.RunLoop);
            else                      GoTo(Sub.WalkLoop);
        }

        private void OnWalkLoop()
        {
            if (!IsMoving())          GoTo(Sub.WalkEnd);
            else if (Ctx.IsSprinting) GoTo(Sub.RunLoop);
        }

        private void OnWalkEnd()
        {
            if (!IsDone(PlayerAnimatorBridge.WalkEnd)) return;

            GoTo(IsMoving() ? Sub.WalkStart : Sub.Idle);
        }

        private void OnRunLoop()
        {
            if (!IsMoving())           GoTo(Sub.RunEnd);
            else if (!Ctx.IsSprinting) GoTo(Sub.WalkLoop);
        }

        private void OnRunEnd()
        {
            if (!IsDone(PlayerAnimatorBridge.RunEnd)) return;

            GoTo(IsMoving() ? Sub.WalkStart : Sub.Idle);
        }

        private void OnTurnBack()
        {
            if (!IsDone(PlayerAnimatorBridge.TurnBack)) return;

            GoTo(IsMoving() ? Sub.WalkStart : Sub.Idle);
        }

        private void OnTaunt()
        {
            if (IsDone(PlayerAnimatorBridge.Taunt))
                GoTo(Sub.Idle);
        }

        // ── Transitions ───────────────────────────────────────────

        private void GoTo(Sub next)
        {
            _sub = next;
            _lastNormalizedTime = 0f;
            _subTimer = 0f;

            // Config에서 루트 모션 여부 읽기
            bool useRoot = next switch
            {
                Sub.WalkStart => Ctx.Config != null && Ctx.Config.UseRootMotion_WalkStart,
                Sub.WalkEnd   => Ctx.Config != null && Ctx.Config.UseRootMotion_WalkEnd,
                Sub.RunEnd    => Ctx.Config != null && Ctx.Config.UseRootMotion_RunEnd,
                _             => false,
            };
            Ctx.Controller.UseCodeMovement = !useRoot;
            if (useRoot || next == Sub.RunLoop) Ctx.Controller.FlushRootPos();

            switch (next)
            {
                case Sub.Idle:      Ctx.Animator.PlayIdle();      break;
                case Sub.IdleLoop:  Ctx.Animator.PlayIdleLoop();  break;
                case Sub.WalkStart: Ctx.Animator.PlayWalkStart(); break;
                case Sub.WalkLoop:  Ctx.Animator.PlayWalkLoop();  break;
                case Sub.WalkEnd:   Ctx.Animator.PlayWalkEnd();   break;
                case Sub.RunLoop:   Ctx.Animator.PlayRunLoop();   break;
                case Sub.RunEnd:    Ctx.Animator.PlayRunEnd();    break;
                case Sub.TurnBack:  Ctx.Animator.PlayTurnBack();  break;
                case Sub.Taunt:     Ctx.Animator.PlayTaunt();     break;
            }
        }

        // 외부에서 Taunt 요청 (ex. 입력 이벤트)
        public void RequestTaunt()
        {
            if (_sub == Sub.IdleLoop)
                GoTo(Sub.Taunt);
        }

        // ── Helpers ───────────────────────────────────────────────

        // 이번 프레임 normalizedTime 변화량 × 곡선 면적 비율로 정확한 이동량 계산
        // → distance가 곡선 모양/길이와 무관하게 총 이동 거리를 보장
        private void ApplyCurveMove(AnimationCurve curve, float distance)
        {
            float curT   = Mathf.Clamp01(Ctx.Animator.GetNormalizedTime());
            float deltaT = curT - _lastNormalizedTime;
            _lastNormalizedTime = curT;

            if (deltaT <= 0f) return;

            // 이번 프레임 곡선 평균값 (사다리꼴 근사)
            float curveVal = curve != null
                ? (curve.Evaluate(_lastNormalizedTime) + curve.Evaluate(curT)) * 0.5f
                : 1f;

            // 곡선 전체 면적 (총 이동 거리 정규화 기준)
            float totalArea = SampleCurveIntegral(curve);

            float movement = totalArea > 0f
                ? distance * (curveVal * deltaT) / totalArea
                : 0f;

            Vector3 dir = IsMoving() ? Ctx.MoveDirection : Ctx.Transform.forward;
            Ctx.CC.Move(dir * movement);
        }

        // 곡선 면적 근사 (N개 샘플)
        private static float SampleCurveIntegral(AnimationCurve curve, int steps = 20)
        {
            if (curve == null) return 1f;
            float sum = 0f;
            float dt  = 1f / steps;
            for (int i = 0; i < steps; i++)
                sum += curve.Evaluate((i + 0.5f) * dt) * dt;
            return sum;
        }

        private bool IsMoving() => Ctx.MoveSpeed > MoveThreshold;

        // 클립 완료 또는 타이머 초과 시 강제 전환
        private bool IsDone(string clipName) =>
            Ctx.Animator.IsStateDone(clipName) || _subTimer >= k_maxSubDuration;

        private bool NeedsTurnBack()
        {
            if (Ctx.MoveDirection == Vector3.zero) return false;
            float angle = Vector3.Angle(Ctx.Transform.forward, Ctx.MoveDirection);
            return angle > TurnBackAngle;
        }
    }
}
