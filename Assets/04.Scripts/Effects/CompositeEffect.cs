using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        [FormerlySerializedAs("BindingKey")]
        public string EffectOriginKey = "";          // Hit가 이 실행 인스턴스를 판정 원점으로 찾는 캐릭터 스코프 키
        public float      StartDelay = 0f;            // 이 조합 안에서의 상대 시차(초)

        // 룩 통째 교체 노브 — 프리팹 렌더러(단일)의 sharedMaterial을 조합마다 스왑한다. null = 프리팹 기본값.
        // 텍스처+색+파라미터+블렌드를 한 번에 바꾸고 오서링은 네이티브 머티리얼 인스펙터에서(툴로 안 빨려듦).
        // sharedMaterial 참조 스왑이라 인스턴스화/릭 없음. 같은 셰이더 공유 규율(다른 셰이더/블렌드는 템플릿 프리팹).
        // 셰이더 노브(MPB)와 공존 — 미세 조정은 MPB, 룩 전체 스왑은 이 필드. 메모리 effect-knob-vs-template-criteria.
        [HideInInspector] public Material MaterialOverride;

        [Header("Playback")]
        [Tooltip("방출 지속(초). 0 = 프리팹 원래 길이. 지정하면 그 시점에 방출을 멈추고 잔여 파티클은 자연 소멸 — Looping 이펙트를 조합마다 다른 길이로 쓸 수 있다")]
        [HideInInspector] public float Duration = 0f;
        [Tooltip("재생 속도 배율. 프리팹에 구운 simulationSpeed에 곱해지며, 전체 길이도 1/배율로 줄어든다")]
        [HideInInspector] public float PlaybackSpeed = 1f;
        [Tooltip("파티클 Start Lifetime(초) 오버라이드. 0 = 프리팹 기본값(안 덮음). >0이면 덮어써 나오고 사라지는 전체 속도를 조절 — 작을수록 빠른 번쩍. Duration과 같은 '0=중립' 규칙이라 토글 없이 일반 필드")]
        [HideInInspector] public float StartLifetime = 0f;

        [Header("Placement")]
        public string  Socket         = "";           // 붙일 본/소켓 이름 (빈값=스폰 원점)
        public Vector3 PositionOffset = Vector3.zero;
        public Vector3 EulerOffset    = Vector3.zero;
        public Vector3 Scale          = Vector3.one;
        public bool    FollowSpawner  = false;        // true=소켓(부모)에 붙어 따라감, false=스폰 위치에 분리
        // 켜면: 소켓(손/무기 본) 위치·방향에서 스폰하되 부모는 스포너 루트(캐릭터)로 붙인다.
        // → 손 스윙(빠른 회전)은 무시하고 캐릭터 이동/방향만 따라감. FollowSpawner보다 우선.
        public bool    ParentToSpawnerRoot = false;
        // 켜면: 소켓의 '위치'만 쓰고 '회전'은 무시한다(월드 기준). 손/무기 본에 회전이 구워져 있어
        // EulerOffset 조준이 어려울 때 — EulerOffset이 월드 회전으로 직접 먹고, PositionOffset도 월드축.
        // (FollowSpawner=소켓 부모 모드에선 무효 — 그땐 소켓 회전을 계속 따라감)
        public bool    IgnoreSocketRotation = false;

        [SerializeReference]
        public List<EffectModule> Modules = new List<EffectModule>();

        // 풀 프리웜/상한은 엔트리가 아니라 프리팹의 EffectPoolConfig 컴포넌트에서 프리팹 단위로 설정한다
        // (풀은 프리팹 단위 전역 공유 — 용량은 프리팹의 속성). 소유권은 캐릭터가 config에서 유도해 등록(EffectOwnership).
        // 미설정(EffectPoolConfig 없는) 프리팹은 온디맨드 생성(프리웜 0·무제한).

        [Header("Despawn")]
        public DespawnMode Despawn  = DespawnMode.ParticleStopped;
        public float       Lifetime = 0f;             // Despawn=Fixed일 때만 사용(초)

        // 프리팹이 EffectParameterSet으로 선언한 셰이더 노브 중 이 조합에서 덮어쓴 것들(이름-값만).
        // 재생 시 PooledEffectHandle.Bind가 MPB로 적용 — 같은 프리팹/풀을 조합마다 다른 룩으로 쓴다.
        // sparse: 여기 없는 선언 파라미터는 프리팹 기본값(EffectParamDecl.Default*)을 쓴다.
        public List<EffectParamOverride> ParamOverrides = new List<EffectParamOverride>();

        // 프리팹 파티클(단일 PS)의 "조합별 델타" 노브. 셰이더 노브(ParamOverrides)와 같은 철학 —
        // 같은 프리팹/풀을 조합마다 다른 타이밍/색으로 쓴다. 필드별 On 토글 = sparse: 끈 필드는
        // Bind가 프리팹 기본값(베이스라인)으로 되돌린다(풀 재사용 누수 방지). 노브 판정 기준은
        // 메모리 effect-knob-vs-template-criteria 참조. (텍스처는 값이라 셰이더 MPB 노브로 처리)
        [HideInInspector] public ParticleParamOverride ParticleOverride = new ParticleParamOverride();
    }

    // 단일 ParticleSystem에 대한 조합별 "토글 오버라이드". 커브/색은 무해한 중립값이 없어
    // Override* 토글로 sparse하게 적용한다(스칼라인 StartLifetime은 '0=중립'이라 Entry 일반 필드로 뺐다).
    // 적용은 ParticleParamApplier(런타임 Bind + 에디터 프리뷰 공용). 모듈 struct에 직접 세팅한다.
    [Serializable]
    public class ParticleParamOverride
    {
        // 크기 커브(왼쪽=나오는 속도, 오른쪽=사라지는 속도) + 피크 배율. → sizeOverLifetime
        public bool           OverrideSizeCurve = false;
        public float          SizeMultiplier    = 1f;
        public AnimationCurve SizeCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));

        // 시작 색(HDR). Intensity를 올리면 블룸이 터진다. → main.startColor
        public bool                       OverrideStartColor = false;
        [ColorUsage(true, true)] public Color StartColor     = Color.white;

        public bool HasAny => OverrideSizeCurve || OverrideStartColor;
    }
}
