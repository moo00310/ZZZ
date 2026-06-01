using UnityEngine;

namespace ZZZ.Player.StateMachine.States
{
    public class NormalComboState : StateBase
    {
        private int   _comboIndex;
        private float _comboTimer;
        private bool  _nextQueued;

        private const int   MaxCombo       = 5;
        private const float ComboWindowEnd = 0.7f; // 클립 normalizedTime 이후 입력 수용
        private const float ComboResetTime = 1.2f;

        public NormalComboState(PlayerStateContext ctx, PlayerStateMachine machine)
            : base(ctx, machine) { }

        public override void Enter()
        {
            _comboIndex  = 1;
            _comboTimer  = 0f;
            _nextQueued  = false;
            Ctx.Animator.PlayNormalAttack(_comboIndex);
        }

        public override void Update()
        {
            _comboTimer += Time.deltaTime;

            float normalizedTime = Ctx.Animator.GetCurrentNormalizedTime();

            // 콤보 입력이 큐에 있고, 클립이 일정 이상 진행됐을 때 다음 타 실행
            if (_nextQueued && normalizedTime >= ComboWindowEnd)
            {
                _nextQueued = false;

                if (_comboIndex >= MaxCombo)
                {
                    Machine.ChangeState<LocomotionState>();
                    return;
                }

                _comboIndex++;
                _comboTimer = 0f;
                Ctx.Animator.PlayNormalAttack(_comboIndex);
            }

            // 콤보 타임아웃
            if (_comboTimer >= ComboResetTime && !_nextQueued)
                Machine.ChangeState<LocomotionState>();
        }

        // PlayerCombat에서 공격 입력 시 호출
        public void QueueNext() => _nextQueued = true;

        public override void Exit()
        {
            _comboIndex = 0;
            _nextQueued = false;
        }
    }
}
