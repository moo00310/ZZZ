using UnityEngine;

namespace ZZZ.Effects
{
    // PooledEffectHandle이 인스턴스 내 최상위 ParticleSystem마다 부착한다.
    // Unity의 OnParticleSystemStopped 메시지는 ParticleSystem이 붙은 그 GameObject에게만 오고
    // 부모로 전파되지 않아서, 이 자리에서 받아 루트의 핸들로 전달하는 역할만 한다.
    public class ParticleStopRelay : MonoBehaviour
    {
        public PooledEffectHandle Owner;

        private void OnParticleSystemStopped() => Owner.NotifyChildStopped();
    }
}
