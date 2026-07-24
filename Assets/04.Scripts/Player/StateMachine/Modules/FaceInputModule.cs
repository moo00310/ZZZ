using UnityEngine;
using ZZZ.Player.StateMachine;

namespace ZZZ
{
    [System.Serializable]
    public class FaceInputModule : SectionModule
    {
        public override void OnEnter(TrackClip tc, SectionContext c)
        {
            Vector3 direction = c.Ctx.Mover.MoveDirection;
            if (direction.sqrMagnitude <= 0.0001f) return;

            c.Ctx.Mover.FaceToward(direction);
            c.FacedInputThisEnter = true;
        }

        public override string MenuName => "입력 방향 조준 (진입)";
        public override string DisplayName => "입력 방향 조준 · 진입 1회";
    }
}
