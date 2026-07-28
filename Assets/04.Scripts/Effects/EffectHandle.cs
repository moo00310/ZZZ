using System.Collections.Generic;

namespace ZZZ.Effects
{
    // EffectService.Play가 반환하는 런타임 토큰 — 한 번의 CompositeEffect 재생으로 스폰된 인스턴스
    // (지연 StartDelay 엔트리 포함)를 모아, 구간 이펙트가 끝날 때 한꺼번에 방출을 멈추기 위한 손잡이.
    // 단발(point) 재생은 이 토큰을 그냥 버린다(기존 자동 반납에 맡김) — 구간 이펙트만 Stop을 호출한다.
    public sealed class EffectHandle
    {
        private readonly List<InstanceRef> _instances = new List<InstanceRef>();
        private bool _stopped;

        private readonly struct InstanceRef
        {
            public readonly PooledEffectHandle Handle;
            public readonly int BindingVersion;

            public InstanceRef(PooledEffectHandle handle)
            {
                Handle = handle;
                BindingVersion = handle.BindingVersion;
            }
        }

        internal bool IsStopped => _stopped;

        // 지연(StartDelay) 엔트리는 나중에 스폰되므로 그때 등록된다.
        // 이미 Stop된 뒤에 늦게 스폰된 인스턴스는 붙잡지 않고 즉시 정지시킨다(누수 방지).
        internal void Add(PooledEffectHandle handle)
        {
            if (handle == null) return;
            var instance = new InstanceRef(handle);
            if (_stopped)
            {
                handle.StopWindowed(instance.BindingVersion);
                return;
            }
            _instances.Add(instance);
        }

        // 구간 종료 / 섹션 이탈·인터럽트 — 스폰된 모든 인스턴스의 방출을 멈춘다.
        // 잔여 파티클은 자연 소멸 후 기존 반납 기계(ParticleStopped)로 풀에 돌아간다.
        public void Stop()
        {
            _stopped = true;
            for (int i = 0; i < _instances.Count; i++)
                if (_instances[i].Handle != null)
                    _instances[i].Handle.StopWindowed(_instances[i].BindingVersion);
            _instances.Clear();
        }
    }
}
