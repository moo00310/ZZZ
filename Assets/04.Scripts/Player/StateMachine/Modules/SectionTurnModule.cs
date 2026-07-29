using UnityEngine;
using ZZZ.Player.StateMachine;

namespace ZZZ
{
    [System.Serializable]
    public class SectionTurnModule : WindowModule
    {
        [SerializeField] private float _rotationScale = 1f;
        [SerializeField] private float _targetAngle;

        public float RotationScale
        {
            get => _rotationScale;
            set => _rotationScale = value;
        }

        public float TargetAngle
        {
            get => _targetAngle;
            set => _targetAngle = value;
        }

        public SectionTurnModule()
        {
            Start = 0f;
            End = 1f;
        }

        public override void OnEnter(TrackClip tc, SectionContext c)
        {
            c.Ctx.Mover.ExtractRootRotation = true;
            c.Ctx.Mover.RootRotationScale = Mathf.Max(0f, _rotationScale);
            c.Ctx.Mover.RootRotationTargetAngle = Mathf.Max(0f, _targetAngle);
            c.Ctx.Mover.RootRotationWindowActive = true;
            c.Ctx.Mover.FlushRootRotation();
        }

        public override void Tick(TrackClip tc, float nt, SectionContext c)
        {
            c.Ctx.Mover.ExtractRootRotation = true;
            c.Ctx.Mover.RootRotationScale = Mathf.Max(0f, _rotationScale);
            c.Ctx.Mover.RootRotationTargetAngle = Mathf.Max(0f, _targetAngle);
            c.Ctx.Mover.RootRotationWindowActive = InWindow(tc, nt);
        }

        public override string MenuName => "섹션 턴 (Root)";
        public override string DisplayName => _targetAngle > 0f
            ? $"섹션 턴  {Start:F2}~{End:F2} · {_targetAngle:F0}°"
            : $"섹션 턴  {Start:F2}~{End:F2} · x{_rotationScale:F3}";
    }
}
