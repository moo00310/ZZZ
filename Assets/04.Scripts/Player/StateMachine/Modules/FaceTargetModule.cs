using UnityEngine;
using ZZZ.Player.StateMachine;

namespace ZZZ
{
    [System.Serializable]
    public class FaceTargetModule : WindowModule
    {
        public float TurnSpeed = 720f;
        [SerializeField] private bool _smoothEntry;

        public bool SmoothEntry
        {
            get => _smoothEntry;
            set => _smoothEntry = value;
        }

        public FaceTargetModule()
        {
            Start = 0f;
            End = 0f;
        }

        public override void OnEnter(TrackClip tc, SectionContext c)
        {
            if (tc.MoveMode != MoveMode.RootMotion) return;
            Transform target = c.Ctx.Mover.FindTarget();
            if (target == null) return;

            c.Ctx.Mover.SetFacingTarget(target, TurnSpeed);
            if (!_smoothEntry && Start <= 0f && !c.FacedInputThisEnter)
                c.Ctx.Mover.FaceToward(target.position - c.Ctx.Transform.position);
        }

        public override void Tick(TrackClip tc, float nt, SectionContext c)
            => c.Ctx.Mover.FaceWindowActive |= End > Start && InWindow(tc, nt);

        public override string MenuName => "타깃 조준";
        public override string DisplayName =>
            $"타깃 조준  {Start:F2}~{End:F2} · {TurnSpeed:F0}°/s"
            + (_smoothEntry ? " · 부드러운 진입" : "");
    }

    [System.Serializable]
    public class FaceOppositeTargetModule : SectionModule
    {
        public override void OnEnter(TrackClip tc, SectionContext c)
        {
            Transform target = c.Machine is IReactionTargetProvider provider
                ? provider.ReactionTarget
                : null;
            if (target == null)
                target = c.Ctx.Mover.FindTarget();
            if (target == null) return;

            Vector3 oppositeLook = -target.forward;
            oppositeLook.y = 0f;
            c.Ctx.Mover.FaceToward(oppositeLook);
        }

        public override string MenuName => "타깃 Look 반대 정렬 (진입)";
        public override string DisplayName => "타깃 Look 반대 정렬 · 진입 1회";
    }
}
