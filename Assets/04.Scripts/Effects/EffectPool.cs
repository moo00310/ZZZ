using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Effects
{
    public class EffectPool
    {
        private readonly GameObject _prefab;
        private readonly int _maxSize;
        private readonly Transform _root;
        private readonly Stack<GameObject> _free = new Stack<GameObject>();
        private readonly HashSet<GameObject> _instances = new HashSet<GameObject>();
        private readonly HashSet<GameObject> _borrowed = new HashSet<GameObject>();
        private readonly HashSet<Object> _owners = new HashSet<Object>();
        private readonly List<GameObject> _retired = new List<GameObject>();

        public bool TearingDown { get; private set; }
        public GameObject Prefab => _prefab;
        public int MaxSize => _maxSize;
        public int FreeCount => _free.Count;
        public int LiveCount => _borrowed.Count;
        public int CreatedCount => _instances.Count;
        public int OwnerCount => _owners.Count;
        public IReadOnlyList<GameObject> RetiredInstances => _retired;

        public EffectPool(GameObject prefab, int prewarmCount, int maxSize, Transform root)
        {
            _prefab = prefab;
            _maxSize = maxSize;
            _root = root;
            Prewarm(prewarmCount);
        }

        public GameObject Get()
        {
            if (TearingDown) return null;
            GameObject instance = null;
            while (_free.Count > 0 && instance == null)
            {
                instance = _free.Pop();
                if (instance == null) _instances.Remove(instance);
            }
            if (instance == null) instance = CreateInstance();
            _borrowed.Add(instance);
            return instance;
        }

        public void Prewarm(int count)
        {
            if (TearingDown) return;
            while (_free.Count < count && (_maxSize <= 0 || _instances.Count < _maxSize))
                _free.Push(CreateInstance());
        }

        public void Release(GameObject instance)
        {
            if (instance == null || !_borrowed.Remove(instance)) return;
            instance.SetActive(false);
            instance.transform.SetParent(_root, false);
            if (TearingDown || (_maxSize > 0 && _free.Count >= _maxSize))
            {
                Retire(instance);
                return;
            }
            _free.Push(instance);
        }

        public void AddOwner(Object owner)
        {
            if (owner == null || TearingDown) return;
            _owners.Add(owner);
        }

        public void RemoveOwner(Object owner)
        {
            if (!_owners.Remove(owner) || _owners.Count != 0) return;
            TearingDown = true;
            _free.Clear();
            _borrowed.Clear();
            var instances = new List<GameObject>(_instances);
            foreach (GameObject instance in instances)
            {
                if (instance != null)
                {
                    PooledEffectHandle handle = instance.GetComponent<PooledEffectHandle>();
                    if (handle != null) handle.StopForTeardown();
                }
                Retire(instance);
            }
        }

        private void Retire(GameObject instance)
        {
            _instances.Remove(instance);
            if (instance == null) return;
            _retired.RemoveAll(retired => retired == null);
            _retired.Add(instance);
            Object.Destroy(instance);
        }

        private GameObject CreateInstance()
        {
            GameObject instance = Object.Instantiate(_prefab, _root);
            instance.SetActive(false);
            _instances.Add(instance);
            return instance;
        }
    }
}
