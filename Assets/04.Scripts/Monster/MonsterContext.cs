using UnityEngine;
using ZZZ;

namespace ZZZ.Monster
{
    // ConfigState에 넘기는 몬스터 컴포넌트 묶음 (PlayerStateContext의 몬스터판).
    public sealed class MonsterContext : IConfigContext
    {
        private readonly MonsterController _mover;
        private readonly IAnimatorBridge   _animator;
        private readonly Transform         _transform;

        public MonsterContext(MonsterController mover, IAnimatorBridge animator, Transform transform)
        {
            _mover     = mover;
            _animator  = animator;
            _transform = transform;
        }

        public IConfigMover    Mover      => _mover;
        public IAnimatorBridge Animator   => _animator;
        public Transform       Transform  => _transform;
        public GameObject      GameObject => _transform.gameObject;
    }
}
