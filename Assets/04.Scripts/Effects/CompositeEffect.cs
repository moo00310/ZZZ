using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZZZ.Effects
{
    public enum DespawnMode
    {
        ParticleStopped,   // 파티클이 다 끝나면 OnParticleSystemStopped 콜백으로 풀 반납 (권장)
        Fixed,             // Lifetime 초 뒤 강제 반납 (Looping 등 자동정지 안 되는 이펙트용)
    }

    // 이펙트 프리팹 여러 개를 상대 시차/배치로 묶어 "하나의 연출"로 재생하는 조합 데이터.
    // 원자(개별 이펙트)는 별도 SO 없이 프리팹을 직접 참조하고, 풀링/배치/반납 설정은 각 Entry가 들고 있다.
    // 풀링은 프리팹 단위(EffectPool)로 이뤄져, 같은 프리팹을 여러 조합이 써도 풀은 공유된다.
    // Notify는 이 SO 하나만 참조한다(단일 이펙트도 Entry 1개짜리 조합으로 표현).
    [CreateAssetMenu(menuName = "ZZZ/Effects/Composite Effect", fileName = "Cmp_")]
    public class CompositeEffect : ScriptableObject
    {
        public List<CompositeEffectEntry> Entries = new List<CompositeEffectEntry>();
    }

    [Serializable]
    public class CompositeEffectEntry
    {
        public GameObject Prefab;                    // 재생할 이펙트 프리팹 (서브파티클 + 내부 Start Delay 포함)
        public float      StartDelay = 0f;            // 이 조합 안에서의 상대 시차(초)

        [Header("Playback")]
        [Tooltip("방출 지속(초). 0 = 프리팹 원래 길이. 지정하면 그 시점에 방출을 멈추고 잔여 파티클은 자연 소멸 — Looping 이펙트를 조합마다 다른 길이로 쓸 수 있다")]
        public float Duration      = 0f;
        [Tooltip("재생 속도 배율. 프리팹에 구운 simulationSpeed에 곱해지며, 전체 길이도 1/배율로 줄어든다")]
        public float PlaybackSpeed = 1f;

        [Header("Placement")]
        public string  Socket         = "";           // 붙일 본/소켓 이름 (빈값=스폰 원점)
        public Vector3 PositionOffset = Vector3.zero;
        public Vector3 EulerOffset    = Vector3.zero;
        public Vector3 Scale          = Vector3.one;
        public bool    FollowSpawner  = false;        // true=부모에 붙어 따라감, false=스폰 위치에 분리

        [Header("Pooling")]
        public int PrewarmCount = 4;                  // 로드 시 미리 생성(첫 스폰 히칭/GC 방지) — 프리팹당 최초 1회
        public int MaxSize      = 0;                  // 풀 상한(0=무제한). 같은 프리팹 공유 시 최초 생성값 사용

        [Header("Despawn")]
        public DespawnMode Despawn  = DespawnMode.ParticleStopped;
        public float       Lifetime = 0f;             // Despawn=Fixed일 때만 사용(초)
    }
}
