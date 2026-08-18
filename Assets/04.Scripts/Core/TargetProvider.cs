using System;
using UnityEngine;

namespace ZZZ
{
    public abstract class TargetProvider : MonoBehaviour
    {
        public abstract Transform CurrentTarget { get; }
        public abstract event Action<Transform> TargetChanged;
    }
}
