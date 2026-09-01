using UnityEngine;

namespace ZZZ
{
    [System.Serializable]
    public class FaceInputModule : SectionModule
    {
        [SerializeField] private bool _followInput;

        public override void OnEnter(TrackClip tc, SectionContext c)
        {
            if (_followInput) c.Ctx.Mover.KillRootRotation = true;
            FaceInput(c, true);
        }

        public override void Tick(TrackClip tc, float nt, SectionContext c)
        {
            if (!_followInput) return;
            c.Ctx.Mover.KillRootRotation = true;
            FaceInput(c, false);
        }

        private static void FaceInput(SectionContext c, bool markEntry)
        {
            Vector3 direction = c.Ctx.Mover.MoveDirection;
            if (direction.sqrMagnitude <= 0.0001f) return;

            c.Ctx.Mover.FaceToward(direction);
            if (markEntry) c.FacedInputThisEnter = true;
        }

        public override string MenuName => "입력 방향 조준 (진입)";
        public override string DisplayName => "입력 방향 조준 · 진입 1회";
    }

    [System.Serializable]
    public class FaceViewModule : SectionModule
    {
        public override void OnEnter(TrackClip tc, SectionContext c)
        {
            c.EntryViewForward = c.Ctx.Mover.ViewForward;
            FaceView(c);
        }

        public override void Tick(TrackClip tc, float nt, SectionContext c)
            => FaceView(c);

        private static void FaceView(SectionContext c)
        {
            c.Ctx.Mover.AllowRotation = false;
            c.Ctx.Mover.KillRootRotation = true;
            c.Ctx.Mover.FaceToward(c.EntryViewForward);
        }

        public override string MenuName => "시점 정면 고정";
        public override string DisplayName => "시점 정면 고정";
    }
}
