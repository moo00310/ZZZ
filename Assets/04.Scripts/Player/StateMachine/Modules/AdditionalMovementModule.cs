using UnityEngine;
using ZZZ.Player.StateMachine;

namespace ZZZ
{
    public enum AdditionalMoveDirection
    {
        Forward,
        Backward,
        MoveInput
    }

    // 지정 구간에 걸쳐 Distance만큼 루트모션과 별도로 이동한다.
    // normalizedTime 진행량으로 분배하므로 프레임률이나 클립 재생 속도가 달라도 구간 총량은 같다.
    [System.Serializable]
    public class AdditionalMovementModule : WindowModule
    {
        public float Distance = 1f;
        public AdditionalMoveDirection Direction = AdditionalMoveDirection.Forward;

        public override void Tick(TrackClip tc, float nt, SectionContext c)
        {
            if (c.Ctx?.Mover == null || c.Ctx.Transform == null) return;

            float windowLength = End - Start;
            if (windowLength <= 0f || Mathf.Approximately(Distance, 0f)) return;

            float covered = CoveredWindowAmount(tc, c.PreviousNormalizedTime, nt);
            if (covered <= 0f) return;

            Vector3 direction = ResolveDirection(c);
            if (direction.sqrMagnitude <= 0.0001f) return;

            c.Ctx.Mover.MoveBy(direction.normalized * (Distance * covered / windowLength));
        }

        private Vector3 ResolveDirection(SectionContext c)
        {
            switch (Direction)
            {
                case AdditionalMoveDirection.Backward:
                    return -c.Ctx.Transform.forward;
                case AdditionalMoveDirection.MoveInput:
                    return c.Ctx.Mover.MoveDirection;
                default:
                    return c.Ctx.Transform.forward;
            }
        }

        private float CoveredWindowAmount(TrackClip tc, float previousNt, float currentNt)
        {
            if (!tc.IsLooping)
                return Mathf.Max(0f, Mathf.Min(currentNt, End) - Mathf.Max(previousNt, Start));

            int firstCycle = Mathf.FloorToInt(previousNt);
            int lastCycle = Mathf.FloorToInt(currentNt);
            float covered = 0f;
            for (int cycle = firstCycle; cycle <= lastCycle; cycle++)
            {
                float cycleStart = cycle + Start;
                float cycleEnd = cycle + End;
                covered += Mathf.Max(0f,
                    Mathf.Min(currentNt, cycleEnd) - Mathf.Max(previousNt, cycleStart));
            }
            return covered;
        }

        public override string MenuName => "추가 이동";
        public override string DisplayName =>
            $"추가 이동  {Distance:F2}m · {Direction} · {Start:F2}~{End:F2}";
    }
}
