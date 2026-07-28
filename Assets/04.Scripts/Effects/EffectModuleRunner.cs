using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Effects
{
    [DefaultExecutionOrder(100)]
    public sealed class EffectModuleRunner : MonoBehaviour
    {
        private readonly List<EffectModuleRuntime> _runtimes =
            new List<EffectModuleRuntime>();

        private EffectModuleContext _context;
        private IReadOnlyList<EffectModule> _modules;
        private Transform _characterRoot;

        internal void Bind(IReadOnlyList<EffectModule> modules, Transform characterRoot)
        {
            _modules = modules;
            _characterRoot = characterRoot;
        }

        private void OnEnable()
        {
            if (_modules == null || _modules.Count == 0 || _characterRoot == null) return;

            _context = new EffectModuleContext
            {
                Effect = transform,
                CharacterRoot = _characterRoot,
                ParticleSystems = GetComponentsInChildren<ParticleSystem>(true),
            };

            _runtimes.Clear();
            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i] == null) continue;
                _runtimes.Add(_modules[i].CreateRuntime());
            }
            _runtimes.Sort((a, b) => a.Order.CompareTo(b.Order));

            for (int i = 0; i < _runtimes.Count; i++)
                _runtimes[i].Start(_context);
        }

        private void Update()
        {
            if (_context == null) return;
            for (int i = 0; i < _runtimes.Count; i++)
                _runtimes[i].Tick(_context, Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (_context == null) return;
            for (int i = 0; i < _runtimes.Count; i++)
                _runtimes[i].LateTick(_context);
        }

        private void OnDisable()
        {
            if (_context != null)
                for (int i = _runtimes.Count - 1; i >= 0; i--)
                    _runtimes[i].Stop(_context);

            _runtimes.Clear();
            _context = null;
            _modules = null;
            _characterRoot = null;
        }
    }
}
