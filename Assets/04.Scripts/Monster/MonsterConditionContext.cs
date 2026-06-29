using UnityEngine;
using ZZZ;

namespace ZZZ.Monster
{
    // 몬스터용 링크 조건 컨텍스트 — 입력 개념이 없으므로 입력 질의는 전부 빈 값.
    // ConfigState 생성자가 ILinkConditionContext를 요구하므로 stub 하나는 필요하다.
    // (Idle+Hit는 입력 조건이 없어 None 조건이 자동 통과, OnEnd로 복귀한다.)
    // 거리/체력 등 AI 조건이 생기면 IMonsterConditionContext로 확장해 여기에 채운다.
    public sealed class MonsterConditionContext : ILinkConditionContext
    {
        public bool       HasBufferedInput => false;
        public ComboInput BufferedInput    => ComboInput.None;
        public bool       IsHeld(ComboInput input) => false;
        public void       ConsumeInput() { }

        public MoveDir CurrentMoveDir => MoveDir.Neutral;
        public Vector3 InputDir       => Vector3.zero;
        public Vector3 Forward        => Vector3.zero;
    }
}
