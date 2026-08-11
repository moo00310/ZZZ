using UnityEngine;

namespace ZZZ.Combat
{
    public enum HitResult
    {
        Accepted,
        Ignored
    }

    public readonly struct HitContext
    {
        public HitData Definition { get; }
        public Transform Source { get; }
        public CombatTeam SourceTeam { get; }
        public Vector3 HitPoint { get; }
        public Vector3 Direction { get; }

        public HitContext(HitData definition, Transform source,
            CombatTeam sourceTeam, Vector3 hitPoint, Vector3 direction)
        {
            Definition = definition;
            Source = source;
            SourceTeam = sourceTeam;
            HitPoint = hitPoint;
            Direction = direction;
        }
    }

    public readonly struct HitExecutionContext
    {
        public Transform Source { get; }
        public Transform EffectOrigin { get; }
        public IHitOriginResolver OriginResolver { get; }
        public bool DebugDraw { get; }
        public float DebugDuration { get; }

        public HitExecutionContext(Transform source, Transform effectOrigin = null,
            IHitOriginResolver originResolver = null, bool debugDraw = false,
            float debugDuration = 0.1f)
        {
            Source = source;
            EffectOrigin = effectOrigin;
            OriginResolver = originResolver;
            DebugDraw = debugDraw;
            DebugDuration = Mathf.Max(0f, debugDuration);
        }
    }

    public interface IHitOriginResolver
    {
        bool TryResolve(string key, out Transform origin);
    }

    public interface IHitSource
    {
        CombatTeam Team { get; }
    }
}
