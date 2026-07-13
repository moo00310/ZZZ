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

        // 이 프리팹을 붙들고 있는 소유자(캐릭터 — EffectOwnership이 config에서 유도해 등록). 풀은 프리팹 단위 전역 공유라
        // 여러 캐릭터가 같은 프리팹을 쓰면 owner가 여럿 — 마지막 owner가 떠날 때만 회수해야 공유가 안 깨진다.
        private readonly HashSet<Object> _owners = new HashSet<Object>();

        // owner 0명이 되면 teardown 모드로 진입: free는 즉시 파괴, 아직 재생 중인 live 인스턴스는
        // 반납(Release)되는 순간 풀에 안 남기고 파괴한다. 다시 owner가 생기면(AddOwner) 유지 모드로 복귀.
        public bool TearingDown { get; private set; }

        // ── 풀 개요 모니터용 읽기 전용 노출 ──
        public GameObject Prefab       => _prefab;
        public int        MaxSize      => _maxSize;
        public int        FreeCount    => _free.Count;
        public int        LiveCount    => _created - _free.Count;
        public int        CreatedCount => _created;
        public int        OwnerCount   => _owners.Count;

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

        // free 인스턴스를 count까지 보충(이미 충분하면 무동작). 상한(maxSize)이 있으면 그 안에서만.
        public void Prewarm(int count)
        {
            while (_free.Count < count && (_maxSize <= 0 || _created < _maxSize))
                _free.Push(CreateInstance());
        }

        // 반납 시 파괴 조건: (1) teardown 중(owner 0) — 재생이 끝나는 대로 실제로 회수, 또는
        // (2) MaxSize(0=무제한) 초과분 — 풀이 무한정 커지지 않게.
        public void Release(GameObject instance)
        {
            instance.SetActive(false);
            instance.transform.SetParent(_root, false);

            if (TearingDown || (_maxSize > 0 && _free.Count >= _maxSize))
            {
                _created--;
                Object.Destroy(instance);
                return;
            }
            _free.Push(instance);
        }

        // 소유자 등록 — 이 프리팹을 쓰는 캐릭터가 로드될 때. teardown 중이었다면 유지 모드로 되돌린다.
        public void AddOwner(Object owner)
        {
            if (owner == null) return;
            _owners.Add(owner);
            TearingDown = false;
        }

        // 소유자 해제 — 캐릭터 언로드 시. 마지막 owner가 빠지면 teardown 시작:
        // 대기(free) 인스턴스는 지금 바로 파괴하고, 재생 중인 것들은 Release 때 파괴된다.
        // 풀 객체 자체는 s_pools에 남겨둔다(빈 껍데기, 오버헤드 미미) — owner가 다시 오면 Prewarm으로 재충전.
        public void RemoveOwner(Object owner)
        {
            _owners.Remove(owner);
            if (_owners.Count == 0) BeginTeardown();
        }

        private void BeginTeardown()
        {
            TearingDown = true;
            while (_free.Count > 0)
            {
                Object.Destroy(_free.Pop());
                _created--;
            }
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
