using UnityEngine;

namespace ZZZ.Combat
{
    public interface IHittable
    {
        void TakeDamage(float damage, Vector3 hitPoint);
    }
}
