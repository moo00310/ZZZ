using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Effects
{
    // 하나의 프리팹에 대한 인스턴스 풀. Get/Release + 프리워밍만 담당하고
    // 배치(소켓/오프셋)와 반납 시점 판단은 EffectService/PooledEffectHandle이 결정한다.
    // 풀 단위는 프리팹 — 같은 프리팹을 여러 조합/Entry가 써도 풀은 공유된다.
    public class EffectPool
    {
        private readonly GameObject         _prefab;
        private readonly int                _maxSize;   // 0=무제한
        private readonly Transform          _root;      // 비활성 인스턴스 보관용 부모(하이어라키 정리)
        private readonly Stack<GameObject>  _free = new Stack<GameObject>();
        private int _created;   // 지금까지 이 풀이 만든 총 인스턴스 수(파괴분 제외)

        // ── 풀 개요 모니터용 읽기 전용 노출 ──
        public GameObject Prefab       => _prefab;
        public int        MaxSize      => _maxSize;
        public int        FreeCount    => _free.Count;
        public int        LiveCount    => _created - _free.Count;
        public int        CreatedCount => _created;

        public EffectPool(GameObject prefab, int prewarmCount, int maxSize, Transform root)
        {
            _prefab  = prefab;
            _maxSize = maxSize;
            _root    = root;
            for (int i = 0; i < prewarmCount; i++)
                _free.Push(CreateInstance());
        }

        public GameObject Get()
        {
            return _free.Count > 0 ? _free.Pop() : CreateInstance();
        }

        // MaxSize(0=무제한) 초과분은 반납 시점에 파괴 — 풀이 무한정 커지지 않게.
        public void Release(GameObject instance)
        {
            instance.SetActive(false);
            instance.transform.SetParent(_root, false);

            if (_maxSize > 0 && _free.Count >= _maxSize)
            {
                _created--;
                Object.Destroy(instance);
                return;
            }
            _free.Push(instance);
        }

        private GameObject CreateInstance()
        {
            var go = Object.Instantiate(_prefab, _root);
            go.SetActive(false);
            _created++;
            return go;
        }
    }
}
