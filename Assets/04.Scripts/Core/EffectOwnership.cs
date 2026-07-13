using System.Collections.Generic;
using UnityEngine;
using ZZZ.Effects;

namespace ZZZ
{
    // 캐릭터가 '무엇을 쓰는지'는 이미 AnimationConfig(→ Effect Notify → CompositeEffect → Entry.Prefab)에 있다.
    // 그래서 소유권을 손으로 다시 나열하지 않고 config에서 유도한다 — config가 곧 단일 진실원(중복/drift 없음).
    // 캐릭터 로드 시 Register, 파괴 시 Unregister를 부르면 전역 풀(EffectService)이 그 프리팹들을 프리웜/회수한다.
    // 프리웜 개수·상한은 각 프리팹의 EffectPoolConfig가 정한다 — 여기선 '누가 소유하냐'만 다룬다.
    public static class EffectOwnership
    {
        public static void Register(Object owner, IEnumerable<AnimationConfig> configs)
        {
            if (owner == null) return;
            foreach (GameObject prefab in CollectPrefabs(configs))
                EffectService.RegisterOwner(prefab, owner);
        }

        public static void Unregister(Object owner, IEnumerable<AnimationConfig> configs)
        {
            if (owner == null) return;
            foreach (GameObject prefab in CollectPrefabs(configs))
                EffectService.UnregisterOwner(prefab, owner);
        }

        // 편의 오버로드 — 개별 config를 그대로 넘길 때(예: 몬스터의 idle/hit).
        public static void Register(Object owner, params AnimationConfig[] configs)
            => Register(owner, (IEnumerable<AnimationConfig>)configs);
        public static void Unregister(Object owner, params AnimationConfig[] configs)
            => Unregister(owner, (IEnumerable<AnimationConfig>)configs);

        // config들의 Effect Notify가 참조하는 프리팹을 distinct로 모은다.
        // Notify는 클립(섹션)별로 달리므로 Clips → Notifies 순으로 훑는다.
        private static HashSet<GameObject> CollectPrefabs(IEnumerable<AnimationConfig> configs)
        {
            var set = new HashSet<GameObject>();
            if (configs == null) return set;

            foreach (AnimationConfig config in configs)
            {
                if (config == null) continue;
                foreach (TrackClip clip in config.Clips)
                {
                    if (clip == null) continue;
                    foreach (TrackNotify notify in clip.Notifies)
                    {
                        if (notify == null || notify.Type != NotifyType.Effect || notify.Effect == null) continue;
                        foreach (CompositeEffectEntry entry in notify.Effect.Entries)
                            if (entry != null && entry.Prefab != null)
                                set.Add(entry.Prefab);
                    }
                }
            }
            return set;
        }
    }
}
