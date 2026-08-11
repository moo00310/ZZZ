namespace ZZZ.Combat
{
    public interface IHittable
    {
        CombatTeam Team { get; }
        UnityEngine.Transform HitTransform { get; }
        HitResult ReceiveHit(in HitContext context);
    }
}
