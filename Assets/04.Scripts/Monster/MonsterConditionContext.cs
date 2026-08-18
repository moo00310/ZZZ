using System;
using UnityEngine;
using ZZZ;

namespace ZZZ.Monster
{
    public enum MonsterDecision
    {
        None,
        ContinueCombo,
        Cancel,
        SpecialFollowUp,
    }

    public interface IMonsterDecisionContext : ILinkConditionContext
    {
        bool HasDecision(MonsterDecision decision);
        void ConsumeDecision(MonsterDecision decision);
    }

    public sealed class MonsterConditionContext : IMonsterDecisionContext
    {
        private MonsterDecision _bufferedDecision;
        private float _decisionTimeRemaining;

        public bool HasBufferedInput => false;
        public ComboInput BufferedInput => ComboInput.None;
        public bool IsHeld(ComboInput input) => false;
        public void ConsumeInput() { }

        public MoveDir CurrentMoveDir => MoveDir.Neutral;
        public Vector3 InputDir => Vector3.zero;
        public Vector3 Forward => Vector3.zero;

        public void Tick(float deltaTime)
        {
            if (_bufferedDecision == MonsterDecision.None) return;

            _decisionTimeRemaining -= deltaTime;
            if (_decisionTimeRemaining <= 0f)
                ClearDecision();
        }

        public void BufferDecision(MonsterDecision decision, float duration)
        {
            if (decision == MonsterDecision.None || duration <= 0f)
            {
                ClearDecision();
                return;
            }

            _bufferedDecision = decision;
            _decisionTimeRemaining = duration;
        }

        public bool HasDecision(MonsterDecision decision)
        {
            return decision != MonsterDecision.None
                && _bufferedDecision == decision
                && _decisionTimeRemaining > 0f;
        }

        public void ConsumeDecision(MonsterDecision decision)
        {
            if (_bufferedDecision == decision)
                ClearDecision();
        }

        public void ClearDecision()
        {
            _bufferedDecision = MonsterDecision.None;
            _decisionTimeRemaining = 0f;
        }
    }

    [Serializable]
    public sealed class AIDecisionCondition : LinkCondition
    {
        public MonsterDecision Decision = MonsterDecision.ContinueCombo;

        public override bool Matches(ILinkConditionContext context)
        {
            return context is IMonsterDecisionContext monsterContext
                && monsterContext.HasDecision(Decision);
        }

        public override void Consume(ILinkConditionContext context)
        {
            if (context is IMonsterDecisionContext monsterContext)
                monsterContext.ConsumeDecision(Decision);
        }

        public override string DisplayName => $"AI {Decision}";
        public override string MenuName => "Monster/AI Decision";
    }
}
