using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace ZZZ.Effects
{
    [Serializable]
    public sealed class BakeToWorldEffectModule : EffectModule
    {
        [FormerlySerializedAs("_followEffectBeforeBake")]
        [SerializeField, Tooltip("World 베이크 전까지 파티클을 캐릭터 루트 기준으로 유지합니다. 끄면 이펙트/소켓을 따라갑니다.")]
        private bool _followRoot = true;

        internal override int Order => 300;
        internal override EffectModuleRuntime CreateRuntime() => new Runtime(this);

        private sealed class Runtime : EffectModuleRuntime
        {
            private readonly BakeToWorldEffectModule _config;
            private ParticleSystem.Particle[] _particles;
            private bool[] _emissionEnabled;
            private bool _completionSeen;
            private bool _bakePending;
            private bool _finished;

            public Runtime(BakeToWorldEffectModule config)
            {
                _config = config;
            }

            internal override int Order => _config.Order;

            internal override void Start(EffectModuleContext context)
            {
                _completionSeen = false;
                _bakePending = false;
                _finished = false;
                _emissionEnabled = new bool[context.ParticleSystems.Length];
                for (int i = 0; i < context.ParticleSystems.Length; i++)
                    _emissionEnabled[i] = context.ParticleSystems[i].emission.enabled;
                SetSimulationSpace(context, !_config._followRoot);
            }

            internal override void Tick(EffectModuleContext context, float deltaTime)
            {
                if (!context.MotionCompleted || _finished) return;
                if (!_completionSeen)
                {
                    _completionSeen = true;
                    return;
                }

                StopEmission(context);
                _bakePending = true;
                _finished = true;
            }

            internal override void LateTick(EffectModuleContext context)
            {
                if (!_bakePending) return;
                _bakePending = false;

                BakeAll(context);
            }

            internal override void RequestStop(EffectModuleContext context)
            {
                if (_finished && !_bakePending) return;

                StopEmission(context);
                _bakePending = false;
                _finished = true;
                BakeAll(context);
            }

            internal override void Stop(EffectModuleContext context)
            {
                if (_emissionEnabled == null) return;
                for (int i = 0; i < context.ParticleSystems.Length
                    && i < _emissionEnabled.Length; i++)
                {
                    ParticleSystem.EmissionModule emission =
                        context.ParticleSystems[i].emission;
                    emission.enabled = _emissionEnabled[i];
                }
            }

            private static void SetSimulationSpace(
                EffectModuleContext context, bool followEffect)
            {
                for (int i = 0; i < context.ParticleSystems.Length; i++)
                {
                    ParticleSystem.MainModule main = context.ParticleSystems[i].main;
                    main.customSimulationSpace = followEffect ? null : context.CharacterRoot;
                    main.simulationSpace = followEffect
                        ? ParticleSystemSimulationSpace.Local
                        : ParticleSystemSimulationSpace.Custom;
                }
            }

            private static void StopEmission(EffectModuleContext context)
            {
                for (int i = 0; i < context.ParticleSystems.Length; i++)
                {
                    ParticleSystem.EmissionModule emission =
                        context.ParticleSystems[i].emission;
                    emission.enabled = false;
                }
            }

            private void Bake(ParticleSystem particleSystem)
            {
                ParticleSystem.MainModule main = particleSystem.main;
                Transform simulationRoot = main.simulationSpace == ParticleSystemSimulationSpace.Local
                    ? particleSystem.transform
                    : main.customSimulationSpace;
                if (main.simulationSpace == ParticleSystemSimulationSpace.World
                    || simulationRoot == null)
                    return;

                int particleCount = particleSystem.particleCount;
                if (_particles == null || _particles.Length < particleCount)
                    _particles = new ParticleSystem.Particle[particleCount];

                int count = particleSystem.GetParticles(_particles);
                for (int i = 0; i < count; i++)
                {
                    _particles[i].position = simulationRoot.TransformPoint(_particles[i].position);
                    _particles[i].velocity = simulationRoot.TransformVector(_particles[i].velocity);
                    _particles[i].axisOfRotation =
                        simulationRoot.TransformDirection(_particles[i].axisOfRotation);
                }

                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.customSimulationSpace = null;
                if (count > 0) particleSystem.SetParticles(_particles, count);
            }

            private void BakeAll(EffectModuleContext context)
            {
                for (int i = 0; i < context.ParticleSystems.Length; i++)
                    Bake(context.ParticleSystems[i]);
            }
        }
    }
}
