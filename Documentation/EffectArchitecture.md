# 이펙트 아키텍처

> **용어 안내** — **Notify** = 애니메이션 재생 중 특정 시점에 발동하는 애니메이션 이벤트
> ([애니메이션 문서](AnimationArchitecture.md) 내부 용어) · **조합**(`CompositeEffect`) = 여러 이펙트
> 프리팹을 시차·배치와 함께 묶은 에셋 · **Entry** = 조합 안의 프리팹 1개 항목.

## 전체 구조도

```
[ AnimationConfig — TrackNotify(Type=Effect) ]     ← "무엇을 언제 터뜨릴지"만 안다
    │   Notify는 CompositeEffect(SO) 하나만 참조 — 프리팹/풀/배치를 모른다
    │   시점(point) Notify = 스폰만 / 구간(interval, End>Start) Notify = [Start,End] 유지
    ▼   ConfigState.DispatchNotify → EffectService.Play(composite, spawner, trackForStop)
[ EffectService ]             ← static 런타임 진입점 (AnimConfig가 아는 유일한 이펙트 API)
    │   · 조합의 Entry들을 순회 — StartDelay > 0 이면 EffectServiceRunner(코루틴 호스트)로 지연 재생
    │   · 소켓 본 검색(FindSocket) → 배치(FollowSpawner / 스폰 위치 분리) → 파티클 재시작
    │   · 구간 이펙트면 스폰된 인스턴스를 EffectHandle에 모아 반환(단발은 null·무할당)
    ▼
[ EffectPool ]                ← 프리팹 단위 풀 (Get / Release, MaxSize 초과분 파괴)
    │   같은 프리팹을 여러 조합이 써도 풀은 공유된다 (키 = 프리팹 GameObject)
    │   프리웜은 캐릭터의 EffectPrewarmer가 로드 시 EffectService.Prewarm으로 채움
    ▼
[ 이펙트 인스턴스 ]
    ├── PooledEffectHandle        재생 제어(속도/방출 컷/노브) + 자기 풀 반납 (ParticleStopped / Fixed)
    └── ParticleStopRelay         최상위 ParticleSystem마다 부착 — Stop 콜백을 핸들로 릴레이
```

> **경계(계약)** — `Notify ─(CompositeEffect 참조)─▶ EffectService ─▶ EffectPool ─▶ 실제 이펙트`.
> AnimationConfig/Notify는 "무엇을 언제"만 알고, "어떻게 풀에서 꺼내 재생하느냐"는 이펙트 시스템이 소유한다.
> 덕분에 애니메이션 쪽 코드는 이펙트 구현이 바뀌어도 (`Instantiate` → 풀링 전환처럼) `DispatchNotify` 한 줄만 바뀐다.

---

## 설계 원칙 — 실행은 조합 단위, 풀링은 프리팹 단위

이 시스템의 핵심 결정은 **"실행 단위"와 "풀링 단위"를 분리**한 것이다.

| 축 | 단위 | 이유 |
|----|------|------|
| **실행 (Play)** | `CompositeEffect` (SO) | 하나의 연출(예: 폭발)은 화염+연기+파편처럼 **여러 프리팹이 시차를 두고** 터진다. Notify가 이 묶음을 하나로 참조해야 편집·발동이 단순해진다 |
| **풀링 (Get/Release)** | 프리팹 (GameObject) | 같은 서브 이펙트(예: hit_spark)가 **서로 다른 조합**에서 다른 시차/오프셋으로 재사용된다. 풀을 조합 단위로 잡으면 같은 프리팹 인스턴스가 조합 수만큼 중복 생성된다 |

### 왜 개별 이펙트용 SO를 따로 두지 않았나

초기안은 `EffectDefinition`(개별 이펙트 1개 = SO 1개) + `CompositeEffect`(조합)의 2단 SO였다.
그러나 **개별 이펙트마다 에셋을 만들어 관리하는 비용**이 실익보다 컸다 — 개별 이펙트의 설정(배치/풀링/반납)은
대부분 "그 조합 안에서의" 값이지 프리팹의 고유 속성이 아니었기 때문이다. 그래서 `EffectDefinition`은 폐기하고,
`CompositeEffectEntry`가 **프리팹을 직접 참조**하며 설정을 자체 보유한다. 단일 이펙트도 Entry 1개짜리
조합으로 표현하므로 Notify 쪽엔 타입 분기가 없다.

> 트레이드오프 — Entry는 "그 조합 안에서의" 값(배치/시차/반납/노브)만 들고, 프리팹의 고유 속성인
> **풀 설정(프리웜 수·상한)은 Entry에서 뺐다**. 풀은 프리팹당 하나뿐인데 Entry마다 풀 설정을 두면
> "어느 Entry 값이 이기냐"가 모호했기 때문 — 지금은 캐릭터의 [EffectPrewarmer](#풀-프리웜-effectprewarmer)가
> 프리팹 단위로 한곳에 모아 선언한다(단일 출처). 덕분에 에셋 수가 줄고 편집 동선이 조합 하나로 끝난다.

### 시차(딜레이)의 두 층위

| 층위 | 저장 위치 | 편집 |
|------|-----------|------|
| **조합 내 프리팹 간 시차** | `CompositeEffectEntry.StartDelay` (SO) | EffectTool / Effect 탭 타임라인 드래그 |
| **프리팹 내부 서브파티클 시차** | 각 `ParticleSystem.main.startDelay` (프리팹 자체) | 타임라인 드래그가 프리팹에 직접 굽는다 |

내부 시차를 자체 시퀀스 SO나 런타임 재생기(PlayableDirector 등)로 만들지 않은 이유:
저장 데이터가 파티클 Start Delay 그 자체면 **런타임 비용이 0**이고, 풀링 단위(프리팹 = 1유닛)가 자명해진다.
Unity Timeline은 외부 Instantiate·인스턴스 리바인딩이 필요한 오케스트레이션엔 부적합해 쓰지 않았다.

---

## 이펙트 조합 에셋 (CompositeEffect — SO)

[CompositeEffect.cs](../Assets/04.Scripts/Effects/CompositeEffect.cs) — `List<CompositeEffectEntry>` 하나가 전부다.

| Entry 필드 | 의미 |
|------------|------|
| `Prefab` | 재생할 이펙트 프리팹 (서브파티클 + 내부 Start Delay 번들) |
| `StartDelay` | 이 조합 안에서의 상대 시차(초) |
| `Duration` | 방출 지속(초). 0 = 프리팹 원래 길이. 지정하면 그 시점에 **방출만 멈추고** 잔여 파티클은 자연 소멸 → `ParticleStopped`면 이어서 자동 반납. **Looping 이펙트를 조합마다 다른 길이로** 쓸 수 있다 |
| `PlaybackSpeed` | 재생 속도 배율 — 프리팹에 구운 `simulationSpeed`에 곱해진다(원본은 캐시로 보존, 풀 재사용 시 매 재생 재적용). 전체 길이도 1/배율로 축소 |
| `Socket` | 붙일 본/소켓 이름 (빈값 = 스포너 원점). 스포너 계층에서 이름으로 재귀 검색 |
| `PositionOffset` / `EulerOffset` / `Scale` | 소켓 기준 로컬 배치 |
| `FollowSpawner` | true = 소켓에 부모로 붙어 따라감 / false = 스폰 순간 위치에 분리(투사체 잔상 등) |
| `ParentToSpawnerRoot` | 소켓(손/무기 본) 위치·방향에서 스폰하되 **부모는 스포너 루트(캐릭터)** — 손 스윙(빠른 회전)은 무시하고 캐릭터 이동/방향만 따라감(발사/빔용). `FollowSpawner`보다 우선 |
| `IgnoreSocketRotation` | 소켓의 **위치만** 쓰고 회전은 무시 — 본에 구운 회전 대신 캐릭터 facing 기준으로 조준(`EulerOffset`이 그 프레임 기준으로 먹음). `FollowSpawner`(소켓 부모) 모드에선 무효 |
| `Despawn` | `ParticleStopped`(파티클 전부 정지 시 자동 반납, 권장) / `Fixed`(Lifetime 초 뒤 강제 반납) |
| `Lifetime` | `Fixed`일 때만 사용(초) — Looping 등 스스로 안 멈추는 이펙트용 |
| `ParamOverrides` | 이 조합에서 덮어쓴 셰이더 노브(이름-값, sparse) — [셰이더 노브 오버라이드](#셰이더-노브-오버라이드--조합별-룩) 참조 |

> 풀 프리웜/상한(과거 `PrewarmCount`/`MaxSize`)은 Entry가 아니라 캐릭터의
> [EffectPrewarmer](#풀-프리웜-effectprewarmer) 컴포넌트에서 프리팹 단위로 설정한다.

---

## 런타임 진입점 (EffectService)

[EffectService.cs](../Assets/04.Scripts/Effects/EffectService.cs) — static 클래스. `Play(composite, spawner, trackForStop=false)`가 공개 API다.
`trackForStop=true`(구간 이펙트)면 스폰된 인스턴스를 모은 `EffectHandle`을 반환하고, 단발(point)은 무할당으로 `null`을 반환한다.

```
Play(composite, spawner, trackForStop)
    │  trackForStop이면 EffectHandle 1개 할당(아니면 null)
    │  Entry 순회
    ├── StartDelay ≤ 0  → 즉시 PlayEntry → handle?.Add(인스턴스)
    └── StartDelay > 0  → EffectServiceRunner.Delay(코루틴) → PlayEntry → handle?.Add
                           (지연 중 spawner가 파괴되면 스킵)
PlayEntry(entry, spawner)
    ├── GetOrCreatePool(prefab)      풀이 없으면 온디맨드 생성(상한 0=무제한) — 프리웜은 별도(EffectPrewarmer)
    ├── pool.Get()                   재사용 or 신규 인스턴스
    ├── FindSocket → PlaceInstance   소켓 본 검색 + FollowSpawner/ParentToSpawnerRoot/IgnoreSocketRotation 배치
    ├── PooledEffectHandle.Bind      재생 제어(PlaybackSpeed·Duration 방출 컷·노브 MPB) + 반납 방식 바인딩 (매 재생마다)
    └── SetActive + RestartParticles 루트 파티클만 Play(true) → 자식은 내부 Start Delay로 순차 재생
```

- **풀 보관 루트** — 비활성 인스턴스는 `DontDestroyOnLoad`된 `EffectPool` 오브젝트 아래에 정리된다.
- **코루틴 호스트** — static 클래스는 코루틴을 못 돌리므로, 지연 재생은 풀 루트에 붙인
  [EffectServiceRunner](../Assets/04.Scripts/Effects/EffectServiceRunner.cs)가 대신 돌린다.
- **Enter Play Mode 대응** — 도메인 리로드를 끈 설정에서도 이전 플레이의 static 풀/러너가 새지 않도록
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`에서 상태를 리셋한다.

---

## 풀 프리웜 (EffectPrewarmer)

풀은 프리팹 단위 **전역 공유**(EffectService)라, "무엇을 몇 개 미리 만들지"도 프리팹 단위로 한곳에 모으는 게 자연스럽다.
[EffectPrewarmer.cs](../Assets/04.Scripts/Effects/EffectPrewarmer.cs)를 캐릭터(스포너)에 붙여, 그 캐릭터가 쓰는
이펙트 프리팹 + 프리웜 개수·상한을 리스트로 선언하면 `Start`에서 `EffectService.Prewarm(prefab, count, maxSize)`을 호출한다.

| 필드 | 의미 |
|------|------|
| `Prefab` | 프리웜할 이펙트 프리팹 |
| `Count` | 미리 만들어둘 인스턴스 수(첫 스폰 히칭/GC 방지) |
| `MaxSize` | 풀 상한(0=무제한). **풀 최초 생성 시에만** 적용 |

- **왜 Entry가 아니라 컴포넌트인가** — 풀이 프리팹당 하나뿐이라, 같은 프리팹을 여러 Entry가 써도 프리웜 값은 하나여야 한다.
  Entry에 두면 "어느 Entry 값이 이기냐"가 모호했다 → 프리팹 단위 선언으로 단일 출처를 만들었다(위 [설계 원칙](#설계-원칙--실행은-조합-단위-풀링은-프리팹-단위) 참조).
- **중복 안전** — 여러 캐릭터가 같은 프리팹을 프리웜해도 풀은 하나만 만들고 free 인스턴스를 `Count`까지 보충할 뿐이다.
- **미프리웜 프리팹** — 선언 안 한 프리팹은 첫 재생 때 온디맨드로 풀이 생기고 상한은 무제한(0)이다.

---

## 구간(Interval) 이펙트 — 시점이 아니라 [Start, End]

기본 Notify는 **한 시점**에 터뜨리고 끝이다. 트레일/오라/차지처럼 **구간 동안 유지**되는 연출을 위해
`TrackNotify`에 `EndNormalizedTime`을 두고, `End > NormalizedTime`이면 **구간 이펙트**로 취급한다(`IsInterval`).
([AnimationConfig.cs](../Assets/04.Scripts/Core/AnimationConfig.cs) `TrackNotify`)

```
FireNotifies (ConfigState, 매 프레임)
    ├── p ≥ NormalizedTime 도달   → DispatchNotify → EffectService.Play(effect, tr, trackForStop: IsInterval)
    │                               구간이면 반환된 EffectHandle을 _notifyActive[i]에 보관
    └── p ≥ EndNormalizedTime 도달 → _notifyActive[i].Stop()   방출만 멈춤(잔여 파티클 자연 소멸)
섹션 이탈·인터럽트·Exit → StopActiveIntervals()  진행 중인 구간 이펙트 전부 Stop (누수 방지)
```

- **EffectHandle** — [EffectHandle.cs](../Assets/04.Scripts/Effects/EffectHandle.cs)는 한 번의 `Play`로 스폰된
  인스턴스(지연 StartDelay 엔트리 포함)를 모은 정지용 토큰이다. `Stop()`은 전부 `StopWindowed()`로 **방출만** 끊고,
  잔여 파티클은 기존 반납 기계(`ParticleStopped`)로 자연 소멸 후 풀로 돌아간다.
- **늦게 스폰된 엔트리 처리** — StartDelay 엔트리는 나중에 등록된다. 이미 `Stop()`된 뒤 도착한 인스턴스는
  붙잡지 않고 즉시 정지시킨다(누수 방지).
- **단발은 무할당** — 시점 Notify는 `trackForStop=false`라 핸들을 만들지 않고 스폰만 한다(전투 스폰 GC 회피).
- **프리팹 권장 설정** — 구간 이펙트 프리팹은 **루프 방출 + `DespawnMode.ParticleStopped`** (End에서 방출 정지 → 자연 소멸 → 자동 반납).

---

## 자동 반납 — PooledEffectHandle + ParticleStopRelay

풀링에서 제일 까다로운 건 "언제 돌려놓느냐"다. 파티클의 **Stop Action = Destroy**는 풀에서 못 쓰므로
(인스턴스가 진짜 파괴됨), **Stop Action = Callback** + `OnParticleSystemStopped` 콜백으로 반납한다.

[PooledEffectHandle.cs](../Assets/04.Scripts/Effects/PooledEffectHandle.cs)가 인스턴스 루트에 붙어 반납을 담당한다.

| DespawnMode | 동작 |
|-------------|------|
| `ParticleStopped` | 인스턴스 안의 **최상위 ParticleSystem 전부**(다른 파티클의 자식이 아닌 것)에 [ParticleStopRelay](../Assets/04.Scripts/Effects/ParticleStopRelay.cs)를 붙여 각자의 Stop 콜백을 받고, **전부 멈추면** 카운트다운이 끝나 반납 |
| `Fixed` | `Lifetime`초 뒤 강제 반납 (Looping 등 자동 정지가 없는 이펙트용) |

핸들은 반납 외에 **Entry별 재생 제어**도 담당한다 — 풀 인스턴스가 Entry 간 공유되므로, Entry마다 달라지는
값은 매 `Bind`에서 적용한다: `PlaybackSpeed`(프리팹 원본 `simulationSpeed` 캐시 × 배율),
`Duration`(경과 시 `Stop(StopEmitting)` — 방출만 끊고 잔여 파티클은 자연 소멸 → `ParticleStopped` 반납으로 연결),
셰이더 노브 MPB(아래 [셰이더 노브 오버라이드](#셰이더-노브-오버라이드--조합별-룩)). 구간 이펙트의 외부 정지 진입점
`StopWindowed()`도 여기 있다 — `EffectHandle.Stop()`이 불러 방출만 멈춘다(Fixed 모드면 방출 정지 후 즉시 반납).

**왜 릴레이가 필요한가** — Unity의 `OnParticleSystemStopped` 메시지는 ParticleSystem이 붙은
**그 GameObject에게만** 오고 부모로 전파되지 않는다. 프리팹 루트엔 파티클이 없고 여러 자식
(예: Dom/Test1/Test2)에 나눠 붙은 구조에서는, 루트의 핸들이 콜백을 받을 방법이 없다.
그래서 최상위 파티클마다 릴레이를 붙여 핸들로 전달한다. 중첩 서브이미터(파티클의 자식 파티클)는
부모 정지에 딸려간다고 보고 카운트에서 제외한다. 인스턴스 구조는 재사용 내내 바뀌지 않으므로
최상위 목록은 **최초 1회만** 수집해 캐시한다.

### 문제와 해결

| 문제 | 왜 생기나 | 해결 |
|------|-----------|------|
| 파티클이 여러 자식에 나눠 붙으면 정지 콜백을 못 받음 | `OnParticleSystemStopped`은 부모로 전파 안 됨 | 최상위 파티클마다 `ParticleStopRelay` 부착 → 핸들이 카운트다운 |
| static 서비스가 지연 재생(코루틴)을 못 함 | static엔 MonoBehaviour 수명이 없음 | 풀 루트 오브젝트에 `EffectServiceRunner`를 붙여 대신 실행 |
| Enter Play Mode(도메인 리로드 off)에서 이전 플레이 풀이 잔존 | static 필드는 리로드 없이는 안 지워짐 | `SubsystemRegistration` 시점에 `ResetState()` |
| 지연 재생 대기 중 스포너(캐릭터)가 파괴됨 | 코루틴이 캡처한 Transform이 죽음 | 발동 직전 null 체크 후 스킵 |
| 풀이 무한정 커짐 (히트 이펙트 난사) | Get은 부족하면 계속 생성 | `MaxSize` 초과분은 **반납 시점에 파괴** — 피크만 흡수하고 평시 크기 유지 |
| 재사용 인스턴스에서 서브파티클 시차가 씹힘 | `Play(true)`를 전 시스템에 걸면 Start Delay 무시 재생 | **루트 파티클만** `Play(withChildren: true)` → 자식은 구운 Start Delay로 순차 재생 |
| 반납 대상 파티클이 하나도 없는 프리팹 | 잘못 만든 에셋이면 인스턴스가 풀에 영영 안 돌아옴 | 경고 로그 + 즉시 반납 폴백 |

---

## 타격 판정 연동 — EffectHitVolume

공격 이펙트 프리팹에 [EffectHitVolume](../Assets/04.Scripts/Combat/EffectHitVolume.cs)을 붙이면
스폰 순간 자기 범위(SphereCollider) 안의 `IHittable`을 때린다 — **이펙트 비주얼 범위 = 타격 범위**.
이펙트가 Notify로 스폰되므로, 타격 타이밍도 자연히 애니 데이터(Notify 시점)를 따른다.
(센서 기반 판정은 `MeleeHitter`가 별도로 담당 — 이펙트 없는 근접 공격용.)

## 셰이더 연출 훅 — EffectProgressDriver

[EffectProgressDriver](../Assets/04.Scripts/Combat/EffectProgressDriver.cs)는 이펙트 프리팹에 붙어
파티클 재생 시간에 맞춰 셰이더 `_Progress`를 0→1로 흘려준다 (FX_FlameBurst의 디졸브/회색 전환 연출 구동).

- **MaterialPropertyBlock** 으로 렌더러 단위 격리 — 같은 `.mat`을 풀의 여러 인스턴스가 공유해도 값이 안 섞인다 (풀링 전제와 맞물리는 선택)
- **`[ExecuteAlways]`** — 씬 프리뷰(파티클 패널 Play)에서도 연출이 보인다. 없으면 프리뷰 땐 `_Progress`가 기본값에 멈춰 "셰이더가 안 먹는 것처럼" 보임

## 셰이더 노브 오버라이드 — 조합별 룩

**문제**: 같은 이펙트 프리팹(`Eff_FlameBurst`)을 여러 조합이 돌려쓰는데, 조합마다 색/시드 같은 룩을
다르게 주고 싶다. 그렇다고 raw 셰이더 프로퍼티를 툴에 통째로 여는 건 이펙트마다 의미가 달라 관심사가
깨지고, 머티리얼을 복제하면 **프리팹 단위 풀 공유**가 무너진다.

**설계**: 프리팹이 노출할 노브를 스스로 선언하고, 조합은 값만 저장하고, 재생 시 **MaterialPropertyBlock**으로
적용한다. 머티리얼 복제 없음 → 풀 공유 유지, GC 0.

| 조각 | 역할 | 위치 |
|------|------|------|
| [EffectParameterSet](../Assets/04.Scripts/Effects/EffectParameterSet.cs) | 프리팹이 "노출할 셰이더 노브"를 선언(표시명·Reference·타입·기본값·범위). **에디터용 메타데이터** | 프리팹 루트 컴포넌트 |
| `EffectParamOverride` | 조합 Entry가 저장하는 **이름-값 오버라이드**(sparse — 덮은 것만). MPB 아니라 직렬화 데이터 | `CompositeEffectEntry.ParamOverrides` |
| [EffectParamApplier](../Assets/04.Scripts/Effects/EffectParamApplier.cs) | 오버라이드(없으면 프리팹 기본값)를 MPB로 렌더러에 적용. **런타임·에디터 프리뷰 공용** | static 헬퍼 |

**핵심 규칙**
- **선언은 프리팹에서 읽고, 값은 Entry에 쓴다** — 툴은 선택된 프리팹의 `EffectParameterSet`을 읽어 **선언된 노브만** 동적으로 그린다([EffectEditorShared.DrawParamOverrides](../Assets/05.Editor/Effects/EffectEditorShared.cs)). 선언 없는 프리팹엔 아무것도 안 뜬다. 새 노브 추가 = 프리팹 선언만, 툴/Entry 코드 0줄.
- **`GetPropertyBlock` 기반 읽고-덮어-되쓰기** — [EffectProgressDriver](../Assets/04.Scripts/Combat/EffectProgressDriver.cs)의 `_Progress` 등 같은 렌더러의 다른 MPB 값과 공존(우리 선언 키만 덮음)
- **선언된 파라미터는 매 적용마다 전부 명시** — 오버라이드 없는 것도 기본값으로 써준다. 풀 인스턴스는 조합 간 공유되므로, 안 그러면 이전 조합의 값이 남는다
- **매 재생 랜덤(Float)** — `EffectParamOverride.Randomize`가 켜지면 재생마다 범위 내 랜덤값을 굴린다. 렌더러 루프 **전에 1회** 굴려 한 인스턴스의 모든 렌더러가 같은 값. 랜덤은 `Bind`(재생당 1회)에서만(`allowRandomize`), **에디터 프리뷰는 안 굴린다**(매 프레임 호출이라 튐)
- **프리뷰 실시간 반영** — 두 툴의 프리뷰 시뮬 루프가 같은 `EffectParamApplier`를 불러 인게임과 룩 일치

**셰이더 쪽 전제** (FX_FlameBurst 사례)
- 노브는 **Boolean/Float 프로퍼티**여야 MPB로 먹는다. **키워드로 만들면 MPB가 못 건드림**
- Entry 선언의 `ShaderProperty`는 셰이더의 **Reference와 정확히 일치**해야 함(`_isGray` ≠ `_Gray`)
- 컬러/회색 램프는 **한 텍스처로 합쳐**(위=컬러/아래=회색) `V = lerp(...)`로 골라 **샘플 1회**(샘플러/페치 절반). ⚠️ Unity는 텍스처 **V축이 PNG 상하 반대**라 lerp 값(0.25/0.75)이 뒤집혀 보일 수 있음
- 얇은 램프 아틀라스(4px)는 **밉맵/압축이 밴드를 섞으므로** Mip Off·Compression None·Wrap Clamp

---

## 에디터 툴

### EffectTool (`ZZZ/Effect Tool`) — 조합 전용 편집 창

[Assets/05.Editor/EffectTool/](../Assets/05.Editor/EffectTool/) — partial class로 영역 분할.

| 영역 | 기능 |
|------|------|
| 목록 (`List`) | 프로젝트의 모든 `CompositeEffect` 브라우징 + New Composite 생성 |
| 타임라인 (`Timeline`) | Entry를 시간축 막대로 표시 — **막대 드래그 = `StartDelay`(시차), 우측 엣지 드래그 = `Duration`(방출 컷)** 을 데이터에 굽는다. 막대 길이는 `PlaybackSpeed`/`Duration` 반영 |
| 인스펙터 (`Inspector`) | Entry별 프리팹/배치/반납/셰이더 노브 편집 (풀 프리웜은 EffectPrewarmer로 분리) |
| 씬 프리뷰 (`Preview`) | `ParticleSystem.Simulate` 스크럽으로 플레이 진입 없이 조합 연출 확인 |
| 풀 개요 (`Pool`) | 플레이 중 프리팹별 풀 상태(Free/Live/Created/Max) 모니터 |

### AnimationConfigTool "Effect" 탭 — 애니와 같은 시간축에서 편집

이펙트 타이밍의 기준은 결국 **애니메이션 프레임**이다. 그래서 조합 편집 기능을
[AnimationConfigTool.EffectPreview.cs](../Assets/05.Editor/AnimationTool/AnimationConfigTool.EffectPreview.cs)
(Effect 탭)로 흡수해, 캐릭터 애니 프리뷰를 보면서 한 자리에서 조정한다.

- **발동 시점 편집** — 선택한 Effect Notify의 `NormalizedTime`을 프레임 표시와 함께 슬라이더/마커 드래그로 조정
- **소켓 프리뷰** — 각 Entry의 발동 시점(`섹션 시작 + NormalizedTime×클립길이 + StartDelay`)에 맞춰
  조합의 개별 이펙트들을 **캐릭터 소켓 본에 붙여 `Simulate`** — 트랙 스크럽/편집 중에도 현재 플레이헤드에 즉시 반영
- **인라인 조합 편집** — Entry별 소켓/오프셋/시차/반납/노브 + StartDelay 타임라인 + 풀 개요를 탭 인스펙터에 내장
- **Combo 프리뷰 모드에선 비활성** — 콤보는 분기·동적 타이밍이라 절대 시간 계산이 불가

> 소켓·프리팹 변경은 구조 변경이라 프리뷰 인스턴스를 재생성하고, 오프셋/시차/NormalizedTime은
> 매 프레임 실시간 반영한다. 프리뷰 인스턴스는 `HideFlags.DontSave`로 스폰해 씬을 오염시키지 않는다.

### EffectEditorShared — 두 툴의 공용 로직

[EffectEditorShared.cs](../Assets/05.Editor/Effects/EffectEditorShared.cs) — 지속시간 계산, Entry 필드 그리기
(+ Stop Action 검증), StartDelay 타임라인(드래그/룰러/플레이헤드), 풀 테이블, 에셋 생성을 static 헬퍼로
모아 EffectTool과 Effect 탭이 **같은 그리기·계산 코드를 공유**한다 (중복 제거).

---

## 파일 구조

```
Assets/04.Scripts/Effects/               런타임
├── CompositeEffect.cs        조합 SO + Entry(프리팹 직접 참조 + 배치/반납 + 셰이더 노브 오버라이드)
├── EffectService.cs          ★ 진입점 — Play(조합, trackForStop) / Prewarm / 프리팹별 풀 관리 / 소켓 검색·배치
├── EffectPool.cs             프리팹 단위 인스턴스 풀 (Get/Release + 프리워밍 + MaxSize)
├── EffectPrewarmer.cs        캐릭터에 부착 — 프리팹 단위 풀 프리웜/상한 선언 (Start에서 EffectService.Prewarm)
├── EffectHandle.cs           구간 이펙트 정지 토큰 — 한 Play로 스폰된 인스턴스 묶음을 Stop
├── EffectServiceRunner.cs    StartDelay 지연 실행용 코루틴 호스트 (풀 루트에 부착)
├── PooledEffectHandle.cs     인스턴스 재생 제어(속도/방출 컷/노브 MPB) + 풀 반납 (ParticleStopped / Fixed) + StopWindowed
├── ParticleStopRelay.cs      최상위 파티클의 Stop 콜백을 핸들로 릴레이
├── EffectParameterSet.cs     프리팹이 노출할 셰이더 노브 선언 (에디터 메타데이터)
└── EffectParamApplier.cs     오버라이드→MPB 적용 (런타임/프리뷰 공용, 매 재생 랜덤 지원)

Assets/04.Scripts/Combat/                이펙트 연동
├── EffectHitVolume.cs        스폰 시 자기 범위 타격 (이펙트 범위 = 타격 범위)
└── EffectProgressDriver.cs   파티클 시간 → 셰이더 _Progress 구동 (MPB 격리)

Assets/05.Editor/
├── EffectTool/               ★ 전용 툴 (ZZZ/Effect Tool) — partial 분할
│   ├── EffectTool.cs             창 골격 + 툴바 + 목록/타임라인/인스펙터 배치
│   ├── EffectTool.List.cs        조합 브라우징
│   ├── EffectTool.Timeline.cs    StartDelay 타임라인
│   ├── EffectTool.Inspector.cs   Entry 편집
│   ├── EffectTool.Preview.cs     씬 Simulate 프리뷰
│   └── EffectTool.Pool.cs        풀 개요
├── Effects/
│   └── EffectEditorShared.cs     두 툴 공용 그리기/계산 헬퍼
└── AnimationTool/
    └── AnimationConfigTool.EffectPreview.cs   Effect 탭 (애니 시간축 위 이펙트 편집/프리뷰)
```

---

## 남은 것 / 로드맵

- **구간형(지속) 노티파이** — ✅ 구현됨. `TrackNotify.EndNormalizedTime`로 `[Start, End]` 구간 유지 → [구간 이펙트](#구간interval-이펙트--시점이-아니라-start-end) 참조. 남은 건 플레이 모드 실전 검증(트레일)
- **`SendMessage` → 이벤트 릴레이** — Effect 외 Notify(`EventName`) 디스패치는 아직 SendMessage (리플렉션 할당) → 캐릭터별 이벤트 릴레이(강타입 `event Action<string>`, 인스턴스 스코프)로 교체 예정 ([TODO.md](TODO.md))
- **Addressables 전환** — 모바일 대비, 스킬 VFX를 사용 직전 로드/종료 후 Release ([TODO.md](TODO.md) 모바일 절)
