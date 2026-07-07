using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Effects
{
    // 캐릭터(또는 스포너)에 붙여, 그 캐릭터가 쓰는 이펙트 프리팹의 전역 풀을 로드 시 미리 채운다.
    // 풀은 프리팹 단위 전역 공유(EffectService) — 이 컴포넌트는 '무엇을 몇 개' 프리웜할지만 한 곳에 모은다.
    // (엔트리별 산발 설정을 대체) 여러 캐릭터가 같은 프리팹을 프리웜해도 free를 count까지 보충할 뿐 중복 풀은 안 생긴다.
    public class EffectPrewarmer : MonoBehaviour
    {
        [System.Serializable]
        public class Warm
        {
            public GameObject Prefab;
            [Min(0)] public int Count   = 8;   // 미리 만들어둘 인스턴스 수(첫 스폰 히칭/GC 방지)
            [Min(0)] public int MaxSize = 0;   // 풀 상한(0=무제한). 풀 최초 생성 시에만 적용
        }

        [Tooltip("이 캐릭터가 쓰는 이펙트 프리팹 + 프리웜 개수. 전역 풀을 프리팹 단위로 채운다.")]
        [SerializeField] private List<Warm> _prewarm = new List<Warm>();

        private void Start()
        {
            for (int i = 0; i < _prewarm.Count; i++)
            {
                var w = _prewarm[i];
                if (w != null && w.Prefab != null)
                    EffectService.Prewarm(w.Prefab, w.Count, w.MaxSize);
            }
        }
    }
}
