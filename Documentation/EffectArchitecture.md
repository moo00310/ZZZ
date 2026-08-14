# 이펙트 아키텍처

애니메이션 Notify에서 이펙트를 재생하고 풀로 반납하는 과정을 설명한다.

| 용어 | 의미 |
|---|---|
| Notify | 애니메이션 재생 중 특정 시점이나 구간에 실행되는 이벤트 |
| CompositeEffect | 여러 이펙트 프리팹과 재생 설정을 묶은 에셋 |
| Entry | CompositeEffect 안의 프리팹 한 항목 |

## 전체 구조도

```
[ AnimationConfig — TrackNotify(Type=Effect) ]     ← "무엇을 언제 터뜨릴지"만 안다
    │   Notify는 CompositeEffect(SO) 하나만 참조 — 프리팹/풀/배치를 모른다
    │   시점(point) Notify = 스폰만 / 구간(interval, End>Start) Notify = [Start,End] 유지
    ▼   ConfigState.DispatchNotify → EffectService.Play(composite, context, trackForStop)
[ EffectService ]             ← static 런타임 진입점 (AnimConfig가 아는 유일한 이펙트 API)
    │   · 조합의 Entry들을 순회 — StartDelay > 0 이면 EffectServiceRunner(코루틴 호스트)로 지연 재생
    │   · 소켓 본 검색(FindSocket) → 배치(FollowSpawner / 스폰 위치 분리) → 파티클 재시작
    │   · 구간 이펙트면 스폰된 인스턴스를 EffectHandle에 모아 반환(단발은 null·무할당)
    ▼
[ EffectPool ]                ← 프리팹 단위 풀 (Get / Release, MaxSize 초과분 파괴)
    │   같은 프리팹을 여러 조합이 써도 풀은 공유된다 (키 = 프리팹 GameObject)
    │   용량(프리웜 수·상한)은 프리팹의 EffectPoolConfig가 정의
    │   소유권 refcount — 캐릭터가 config에서 유도해 등록(EffectOwnership),
    │   마지막 소유자가 떠나면 teardown(회수)
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
> "어느 Entry 값이 이기냐"가 모호했기 때문 — 지금은 프리팹의 [EffectPoolConfig](#풀-용량과-소유권-effectpoolconfig--effectownership)가
> 프리팹 단위로 선언한다(단일 출처). 덕분에 에셋 수가 줄고 편집 동선이 조합 하나로 끝난다.

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
| `BindingKey` | 같은 캐릭터의 Hit Notify가 실제 실행 중인 Entry Transform을 찾는 선택적 키 |
| `StartDelay` | 이 조합 안에서의 상대 시차(초) |
| `Duration` | 활성 재생 구간(초). 0 = 프리팹 원래 길이. 지정하면 그 시점에 모듈 종료와 Hit 연동을 먼저 끝내고 방출을 멈춘다. 잔여 파티클은 자연 소멸하며 `ParticleStopped`면 이어서 자동 반납된다. **Looping 이펙트를 조합마다 다른 길이로** 쓸 수 있다 |
| `PlaybackSpeed` | 재생 속도 배율 — 프리팹에 구운 `simulationSpeed`에 곱해진다(원본은 캐시로 보존, 풀 재사용 시 매 재생 재적용). 전체 길이도 1/배율로 축소 |
| `StartLifetime` | 파티클 Start Lifetime(초) 오버라이드. **0 = 프리팹 기본값**(안 덮음), >0이면 덮어써 나오고 사라지는 전체 속도 조절(작을수록 빠른 번쩍). `Duration`과 같은 '0=중립' 규칙이라 토글 없는 일반 필드 |
| `MaterialOverride` | 렌더러 `sharedMaterial`을 조합마다 통째 스왑(텍스처+색+파라미터+블렌드). **null = 프리팹 기본**. 참조 스왑이라 인스턴스화/GC 없음 — [조합별 오버라이드](#조합별-오버라이드--노브-3층) 참조 |
| `Socket` | 붙일 본/소켓 이름 (빈값 = 스포너 원점). 스포너 계층에서 이름으로 재귀 검색 |
| `PositionOffset` / `EulerOffset` / `Scale` | 소켓 기준 로컬 배치 |
| `FollowSpawner` | true = 소켓의 보정된 포즈를 매 프레임 따라감 / false = 스폰 순간 위치에 분리(투사체 잔상 등). 런타임은 Animator 평가 중 `Bip001`에 남는 원본 루트 이동이 파티클 방출 위치로 새지 않도록 소켓의 직접 자식 대신 `EffectSocketFollower`를 사용한다 |
| `ParentToSpawnerRoot` | 소켓(손/무기 본) 위치·방향에서 스폰하되 **부모는 스포너 루트(캐릭터)** — 손 스윙(빠른 회전)은 무시하고 캐릭터 이동/방향만 따라감(발사/빔용). `FollowSpawner`보다 우선 |
| `IgnoreSocketRotation` | 소켓의 **위치만** 쓰고 회전은 무시 — 본에 구운 회전 대신 캐릭터 facing 기준으로 조준(`EulerOffset`이 그 프레임 기준으로 먹음). `FollowSpawner`(소켓 부모) 모드에선 무효 |
| `Despawn` | `ParticleStopped`(파티클 전부 정지 시 자동 반납, 권장) / `Fixed`(Lifetime 초 뒤 강제 반납) |
| `Lifetime` | `Fixed`일 때만 사용(초) — Looping 등 스스로 안 멈추는 이펙트용 |
| `ParamOverrides` | 이 조합에서 덮어쓴 셰이더 노브(이름-값, sparse, MPB 적용) — [조합별 오버라이드](#조합별-오버라이드--노브-3층) 참조 |
| `ParticleOverride` | 파티클 모듈 토글 오버라이드(Size over Lifetime 커브 / Start Color HDR). 커브·색은 중립값이 없어 토글 sparse |

> 풀 프리웜/상한(과거 `PrewarmCount`/`MaxSize`)은 Entry가 아니라 프리팹의
> [EffectPoolConfig](#풀-용량과-소유권-effectpoolconfig--effectownership) 컴포넌트에서 프리팹 단위로 설정한다.

### 소켓 추종과 루트모션 평가 순서

Burnice 클립은 `Animator.deltaPosition`으로 최상위 캐릭터를 이동시키면서, 같은 프레임의
`Bip001` 포즈에도 원본 수평 이동이 남는다. `PlayerController.LateUpdate`가 `Bip001`의 X·Z를
제거하므로 최종 모델은 정상 위치에 보이지만, 이펙트를 소켓의 직접 자식으로 두면 Animator 평가
중간의 보정 전 좌표가 파티클 방출 위치로 들어갈 수 있다.

`FollowSpawner`는 이를 피하기 위해 인스턴스를 소켓 계층에서 분리하고
`EffectSocketFollower`로 포즈를 복사한다.

```text
Update              직전 프레임의 보정된 소켓 포즈 복사
Animator 평가       Bip001 원본 이동은 소켓 계층에만 적용 — 이펙트에는 전파되지 않음
Player LateUpdate   Bip001 수평 이동 제거
Follower LateUpdate 현재 프레임의 보정된 소켓 포즈 복사
```

- `FaceOutwardEffectModule`이 있으면 팔로워는 위치만 갱신하고 회전은 모듈에 맡긴다.
- `ArcMotionEffectModule`이 있으면 모듈이 위치와 부모를 직접 구동하므로 기존 모듈 경로를 유지한다.
- 풀 재사용 시 이전 소켓 참조는 `Unbind`하고 현재 Entry의 소켓과 오프셋을 다시 바인딩한다.

---

## 런타임 진입점 (EffectService)

[EffectService.cs](../Assets/04.Scripts/Effects/EffectService.cs) — static 클래스. `Play(composite, context, trackForStop=false)`가 공개 API다.
`trackForStop=true`(구간 이펙트)면 스폰된 인스턴스를 모은 `EffectHandle`을 반환하고, 단발(point)은 무할당으로 `null`을 반환한다.

```
Play(composite, context, trackForStop)
    │  trackForStop이면 EffectHandle 1개 할당(아니면 null)
    │  Entry 순회
    ├── StartDelay ≤ 0  → 즉시 PlayEntry → handle?.Add(인스턴스)
    └── StartDelay > 0  → EffectServiceRunner.Delay(코루틴) → PlayEntry → handle?.Add
                           (지연 중 spawner가 파괴되면 스킵)
PlayEntry(entry, spawner)
    ├── GetOrCreatePool(prefab)      풀이 없으면 온디맨드 생성(상한 0=무제한) — 프리웜/상한은 별도(RegisterOwner가 EffectPoolConfig로)
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

## 풀 용량과 소유권 (EffectPoolConfig / EffectOwnership)

풀은 프리팹 단위 **전역 공유**(EffectService)다. 여기엔 성격이 다른 두 데이터가 얽혀 있어 **분리**했다:
**용량**(프리웜 수·상한)은 *프리팹의 속성*이고, **소유권**(누가 이 프리팹을 쓰나)은 *캐릭터의 속성*이다.

### ① 용량 — 프리팹의 [EffectPoolConfig](../Assets/04.Scripts/Effects/EffectPoolConfig.cs)

이펙트 프리팹 루트에 붙이는 컴포넌트. 풀 용량은 프리팹당 하나로 정의돼야 하므로(공유 자원) 프리팹 자신이 들고 있는다.

| 필드 | 의미 |
|------|------|
| `PrewarmCount` | 미리 만들어둘 인스턴스 수(첫 스폰 히칭/GC 방지). 0 = 프리웜 안 함(온디맨드) |
| `MaxSize` | 풀 상한(0=무제한). 초과분은 반납 시 파괴 |

- **왜 프리팹인가** — 풀이 프리팹당 하나뿐이라 용량도 하나여야 한다. 과거엔 캐릭터마다 선언해
  같은 프리팹의 `MaxSize`가 **로드 순서로 갈리던** 잠재 버그가 있었다 → 프리팹 속성으로 옮겨 단일 출처화.
- **Config 없는 프리팹** — 온디맨드(프리웜 0·무제한)로 동작한다.

### ② 소유권 + 회수 — [EffectOwnership](../Assets/04.Scripts/Core/EffectOwnership.cs) (config 유도)

"이 캐릭터가 어떤 이펙트를 쓰나"는 이미 캐릭터의 `AnimationConfig`(→ Effect Notify → CompositeEffect → Entry.Prefab)에
있다. 그래서 **손으로 리스트를 만들지 않고 config에서 유도**한다(단일 진실원, 중복/drift 없음). state machine이:

- **로드**(`Awake`) — `EffectOwnership.Register(this, 내 config들)` → distinct 프리팹마다
  `EffectService.RegisterOwner`(프리팹 `EffectPoolConfig`대로 프리웜 + owner 집합에 추가).
- **파괴**(`OnDestroy`) — `EffectOwnership.Unregister` → `EffectService.UnregisterOwner`.

**refcount 회수(teardown)** — 풀은 owner 집합을 들고, 여러 캐릭터가 같은 프리팹을 공유하면 owner가 여럿이다.
마지막 owner가 빠질 때만 회수한다(공유 이펙트가 안 깨짐):

- owner 0 → `EffectPool`이 teardown 진입: 대기(free) 인스턴스는 즉시 파괴, 재생 중이던 것도 반납되는 순간 파괴.
- 풀 객체 자체는 s_pools에 남겨둔다(빈 껍데기, 오버헤드 미미) — owner가 다시 오면 Prewarm으로 재충전.
- **에셋 메모리**(텍스처 등)까지 실제로 내리려면 씬 전환 등 적절한 시점에 `Resources.UnloadUnusedAssets()`를 별도 호출.
  teardown은 "붙든 참조를 끊는" 단계까지만 한다.

> **모바일 상주 대응** — 이 refcount 덕분에 안 쓰는 캐릭터의 전용 이펙트는 그 캐릭터가 언로드되면 풀에서 빠진다.
> 공유 이펙트는 마지막 사용자가 떠날 때까지 유지 → 상주와 공유를 둘 다 만족.

---

## 구간(Interval) 이펙트 — 시점이 아니라 [Start, End]

기본 Notify는 **한 시점**에 터뜨리고 끝이다. 트레일/오라/차지처럼 **구간 동안 유지**되는 연출을 위해
`TrackNotify`에 `EndNormalizedTime`을 두고, `End > NormalizedTime`이면 **구간 이펙트**로 취급한다(`IsInterval`).
([AnimationConfig.cs](../Assets/04.Scripts/Core/AnimationConfig.cs) `TrackNotify`)

```
FireNotifies (ConfigState, 매 프레임)
    ├── p ≥ NormalizedTime 도달   → DispatchNotify → EffectService.Play(effect, context, trackForStop: IsInterval)
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

### 프리팹 반납 체크리스트

- `Despawn = ParticleStopped`이면 핸들이 추적하는 **모든 최상위 ParticleSystem**의 `Stop Action`을 `Callback`으로 둔다. 하나라도 `None`이면 그 시스템의 정지 메시지가 오지 않아 인스턴스가 계속 늘어날 수 있다.
- Looping 파티클은 구간 Notify의 End, Entry의 `Duration`, 또는 외부 `EffectHandle.Stop()` 중 하나로 방출을 끝내야 한다. 종료 시점을 보장할 수 없으면 `Despawn = Fixed`와 `Lifetime`을 사용한다.
- `EffectPoolConfig.MaxSize`는 동시 생성 자체를 막는 하드 캡이 아니다. 피크 때 상한을 넘겨 생성할 수 있고, 초과 인스턴스를 **반납할 때 파괴**해 대기 풀의 크기를 정리한다.
- 풀 재사용 확인은 Hierarchy의 Clone 개수뿐 아니라 `OnParticleSystemStopped → ParticleStopRelay → PooledEffectHandle → EffectPool.Release` 흐름이 끝나는지 함께 본다.

핸들은 반납 외에 **Entry별 재생 제어**도 담당한다 — 풀 인스턴스가 Entry 간 공유되므로, Entry마다 달라지는
값은 매 `Bind`에서 적용한다: `PlaybackSpeed`(프리팹 원본 `simulationSpeed` 캐시 × 배율),
`Duration`(경과 시 재생 리스너와 모듈에 종료를 알리고 `Stop(StopEmitting)` — 잔여 파티클은 자연 소멸 → `ParticleStopped` 반납으로 연결),
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

## Hit Notify 원점 바인딩

이펙트와 공격 데이터는 서로 소유하지 않는다. Effect Notify는 `CompositeEffect`만 재생하고,
Hit Notify가 별도의 `HitData`와 판정 타이밍을 소유한다. 이동하는 파동·장판처럼 판정 원점만
이펙트 인스턴스를 따라야 할 때 두 Notify를 Entry의 `BindingKey`로 연결한다.

```text
Effect Notify → CompositeEffect Entry.BindingKey = FlameWave
               스폰 시 EffectBindingScope에 실제 Transform 등록

Hit Notify    → Origin = Effect, EffectKey = FlameWave
               등록된 Transform을 원점으로 HitService 실행
```

바인딩은 전역 문자열 테이블이 아니라 `ConfigState`가 가진 캐릭터별 스코프다. 같은 키를 여러 캐릭터가
동시에 사용해도 섞이지 않는다. 풀 인스턴스가 정지·반납되면 `PooledEffectHandle`이 자신이 등록한 ID만
해제한다. 같은 키의 인스턴스가 겹치면 가장 최근에 생성된 활성 인스턴스를 사용한다.

Effect가 `PlayAfterAnimation`에서 생성되므로 같은 프레임에 Hit이 먼저 평가될 수 있다. 이때
`HitHandle`은 루트로 폴백하지 않고 원점 등록을 기다렸다가 다음 Tick에서 판정을 시작한다.

## 셰이더 연출 훅 — EffectProgressDriver

[EffectProgressDriver](../Assets/04.Scripts/Combat/EffectProgressDriver.cs)는 이펙트 프리팹에 붙어
파티클 재생 시간에 맞춰 셰이더 `_Progress`를 0→1로 흘려준다 (FX_FlameBurst의 디졸브/회색 전환 연출 구동).

- **MaterialPropertyBlock** 으로 렌더러 단위 격리 — 같은 `.mat`을 풀의 여러 인스턴스가 공유해도 값이 안 섞인다 (풀링 전제와 맞물리는 선택)
- **`[ExecuteAlways]`** — 씬 프리뷰(파티클 패널 Play)에서도 연출이 보인다. 없으면 프리뷰 땐 `_Progress`가 기본값에 멈춰 "셰이더가 안 먹는 것처럼" 보임

## 조합별 오버라이드 — 노브 3층

같은 프리팹/풀을 여러 조합이 돌려쓰되, **조합마다 룩·타이밍을 다르게** 준다. 층이 셋이고 적용 방식이 다르다:

| 층 | 무엇을 바꾸나 | 적용 방식 | 중립값(안 덮음) |
|----|--------------|-----------|------------------|
| **머티리얼** | 룩 통째(텍스처+색+파라미터+블렌드) | `renderer.sharedMaterial` 스왑 | `null` = 프리팹 기본 |
| **파티클 모듈** | StartLifetime / Size over Lifetime 커브 / Start Color(HDR) | 모듈 struct에 직접 세팅 | StartLifetime `0`=기본 · Size/Color는 토글 off |
| **셰이더(MPB)** | 셰이더 프로퍼티 미세값(색/시드/float) | `MaterialPropertyBlock` | 프리팹 선언 기본값 |

- **머티리얼 = 룩 전체 스왑**(네이티브 머티리얼 인스펙터에서 오서링) vs **셰이더 노브 = 새 에셋 없이 미세 조정**. 공존한다 — MPB는 sharedMaterial 위에 얹힌다. 규율: 스왑 머티리얼은 **같은 셰이더/블렌드** 공유(다르면 템플릿 프리팹).
- **풀 재사용 누수 방지** — 세 층 모두 "오버라이드 안 하면 프리팹 기본값(**baseline**)으로 되돌린다". baseline은 덮기 전 **최초 1회** 캡처(`ParticleBaseline` / 기본 머티리얼 / 셰이더 선언 기본값). 안 그러면 풀 인스턴스에 이전 조합 값이 남는다.
- **단일 대상 전제** — 아톰 = 단일 PS/렌더러라 첫 `ParticleSystem`/`Renderer` 하나에 적용한다. 멀티 PS 프리팹이면 **첫 것에만** 먹는다([아톰 단위](#아톰-단위granularity--언제-쪼개나) 참조).
- **왜 텍스처가 아니라 머티리얼인가** — 텍스처만 MPB로 스왑하면 셰이더별 프로퍼티 이름 선언이 필요하고 오서링이 툴로 들어온다. 머티리얼 참조 스왑은 상위집합(텍스처+색+파라미터)이고 오서링이 네이티브에 남아 더 낫다. 성능도 파티클엔 무승부, 메시엔 SRP Batcher 유지로 유리(`sharedMaterial`이라 인스턴스화/GC 없음).

> 적용 코드는 [EffectParamApplier.cs](../Assets/04.Scripts/Effects/EffectParamApplier.cs)에 3층이 모여 있다 — `EffectParamApplier`(MPB) · `ParticleParamApplier`+`ParticleBaseline`(모듈) · `EffectMaterialApplier`(머티리얼). 셋 다 런타임 `Bind`와 두 툴 프리뷰가 공유해 룩을 일치시킨다.

### 셰이더 노브(MPB) — 상세

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

## 오버라이드 판정 기준 — 노브 vs 템플릿 vs Variant

무엇을 노브(데이터)로 빼고 무엇을 프리팹으로 남길지의 경계. 목적은 **툴이 ParticleSystem을
재구현(inner-platform effect)하지 않게** 하는 것 — 노브를 계속 늘리면 결국 SO에 파티클을 통째
재직렬화하는 꼴이 된다.

**게이트 (3질문 전부 YES여야 노브, 하나라도 NO면 프리팹/템플릿)**

1. **조합별 분산** — 같은 풀을 공유하며 조합마다 값이 달라야 하나? (NO → 프리팹 **Variant**: 별 풀 허용)
2. **값 vs 구조** — scalar/curve/color/gradient/material 인가? 모듈 on-off·sub-emitter·셰이더/블렌드는 **구조** (NO → 새 **템플릿 프리팹**)
3. **상위 빈도** — 자주 만지는 소수인가? 한 번 세팅하고 마는 롱테일이면 프리팹 (NO → Variant)

**증가 규칙** — 모듈을 켜고 싶다 → 노브 추가가 아니라 *템플릿 프리팹 추가*(토폴로지는 5~10개서 포화).
값이 조합마다 다르다 → *노브*(소수서 포화). 두 축의 증가 속도가 달라 폭주하지 않고 수렴한다.

**툴 정체성(넘으면 안 되는 선)** — 툴 = "조합 + 조합별 델타" 레이어. 이펙트 authoring은 Unity
ParticleSystem이 소유한다. 텍스처를 개별 노브로 빼는 대신 [머티리얼 스왑](#조합별-오버라이드--노브-3층)으로
간 것도 이 원칙(오서링을 네이티브에 남김)의 실천이다.

> 기술적으로는 모듈 대부분이 런타임 API로 설정 가능하다(불가능해서가 아니라, 전부 빼면 프리팹을
> 더 나쁜 형태로 재구현하는 셈이라 안 하는 것). **진짜 불가는 sub-emitter뿐**(실 자식 GameObject 필요).
> 현재 노브 화이트리스트: Material / StartLifetime / Size 커브 / Start Color / (셰이더 미세값 MPB).
> 강제 캡은 없다 — 이 문서가 게이트를 지키는 유일한 방어선이다.

---

## 아톰 단위(granularity) — 언제 쪼개나

**아톰 = "파티클 1개"가 아니라 "독립 관리가 필요한 최소 단위"**다. composition이 두 층에서 일어난다:

| 층 | composition | 소유 |
|----|-------------|------|
| 레이어(Core+Sparks+Smoke…) | 한 프리팹에 파티클 여러 개로 묶음 | **아티스트/네이티브** (ParticleSystem 계층) |
| 아톰 | 프리팹들을 시차·배치·노브로 조합 | **데이터** (`CompositeEffect`) |

멀티 파티클 프리팹(에셋 스토어 등)은 **VFX의 표준**이고, **통째로 아톰 하나**로 써도 된다 —
재생/풀링은 이미 멀티 PS를 지원한다([자동 반납](#자동-반납--pooledeffecthandle--particlestoprelay)의 릴레이 구조).
전부 단일 PS로 쪼갤 필요 없다.

**쪼개는 기준 (하나라도 해당 → 아톰 분리)**

| 분리(고운 아톰) | 통째(거친 아톰) |
|-----------------|-----------------|
| 여러 이펙트가 공유하는 **완전 동일** 레이어(공유 풀로 메모리 절약) | 그 이펙트 **고유** 레이어(쪼개면 GameObject 오버헤드만↑) |
| 레이어별 독립 시차/배치/풀링/노브 | 한 룩으로 고정·통짜 튜닝됨 |
| 서로 **다른 소켓** / Entry-레벨 추종(`FollowSpawner` 등)이 다름 | 같은 소켓+추종(통째로 붙음) |
| — | **Sub-emitter 포함**(쪼갤 수 없음) / "일부만 월드에 남김"은 PS **Simulation Space**로 |

**풀링 단위 = 프리팹 인스턴스 하나**(내부 PS 개수 무관). 풀은 프리팹 단위 공유. Entry 1개 = 그 풀에서
인스턴스 1개. 아톰 N개로 쪼개면 풀 N개 + 스폰당 인스턴스 N개.

**성능 관점** — 렌더링(드로우콜/오버드로우)은 패키징 무관(6 PS = 6 PS, 파티클은 SRP Batch도 없음).
차이는 스폰당 CPU 관리(SetActive/Bind ×N)뿐인데, **풀링이 Instantiate/Destroy/GC를 없애 격차가 작다**
(Get/Release는 스택 pop 수준). 그러니 **granularity는 성능이 아니라 재사용/메모리/제어로 결정**한다.
진짜 성능 레버는 **오버드로우/필레이트**(파티클 크기·개수·Additive 겹침)지 패키징이 아니다.

**메모리 관점** — 쪼개기 자체가 아니라 **공유 가능한 동일 레이어를 공유 풀로 합칠 때** 절약된다
(풀 크기가 "모든 이펙트의 합"이 아니라 "동시 재생 피크"로 잡히므로). **고유 레이어 쪼개기는 오히려 손해**
(파티클 버퍼 동일 + 루트 GameObject/컴포넌트만 추가). 텍스처·메시 **에셋**은 참조 공유라 패키징 무관.
콜드 이펙트는 풀링을 안 하거나 작게(온디맨드) 잡는 것도 별개 메모리 레버.

> **풀링의 의미** = "객체 생성/파괴 스파이크 + GC 제거"를 **상주 메모리와 맞바꾸는** 것. 렌더링/처리량
> 도구가 아니다. 프리웜으로 그 비용을 로드 타임에 앞당길 수 있다. 이득은 **스폰 빈도에 비례**한다.

결론: **"자주 재사용되는 완전 동일 레이어만 아톰으로 뽑고, 고유/통짜 이펙트는 통째로 쓴다."** 고운 아톰과
거친 아톰이 한 시스템에 공존하는 게 정상이다. (상세 결정 근거는 프로젝트 메모리 `effect-knob-vs-template-criteria`)

---

## 에디터 툴

### EffectTool (`ZZZ/Effect Tool`) — 조합 전용 편집 창

[Assets/05.Editor/EffectTool/](../Assets/05.Editor/EffectTool/) — partial class로 영역 분할.

| 영역 | 기능 |
|------|------|
| 목록 (`List`) | 프로젝트의 모든 `CompositeEffect` 브라우징 + New Composite 생성 |
| 타임라인 (`Timeline`) | Entry를 시간축 막대로 표시 — **막대 드래그 = `StartDelay`(시차), 우측 엣지 드래그 = `Duration`(방출 컷)** 을 데이터에 굽는다. 막대 길이는 `PlaybackSpeed`/`Duration` 반영 |
| 인스펙터 (`Inspector`) | Entry별 편집 — 접기 그룹으로 정리: Option(추종) / 파티클 노브(Duration·StartLifetime·배치·Size·Color) / 쉐이더 노브 + 상단 Material 스왑 (풀 용량은 프리팹 EffectPoolConfig로 분리) |
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

[EffectEditorShared.cs](../Assets/05.Editor/Effects/EffectEditorShared.cs) — 지속시간 계산, **Entry 필드 그리기
(`DrawEntryFields` — 확정 표시 순서 + 접기 그룹, 두 툴 공용)**, Stop Action 검증, StartDelay 타임라인
(드래그/룰러/플레이헤드), 풀 테이블, 에셋 생성을 static 헬퍼로 모아 EffectTool과 Effect 탭이 **같은
그리기·계산 코드를 공유**한다 (중복 제거). 필드 순서/그룹을 바꿀 땐 이 한 곳만 고치면 두 툴에 반영된다.

---

## 파일 구조

```
Assets/04.Scripts/Effects/               런타임
├── CompositeEffect.cs        조합 SO + Entry(프리팹 직접 참조 + 배치/반납 + 노브 3층: 머티리얼/파티클/셰이더) + ParticleParamOverride
├── EffectService.cs          진입점 — Play / Prewarm / 소유권 등록 / 프리팹별 풀 관리 / 배치
├── EffectPool.cs             프리팹 단위 인스턴스 풀 (Get/Release + 프리워밍 + MaxSize + owner refcount/teardown)
├── EffectPoolConfig.cs       이펙트 프리팹 루트에 부착 — 풀 용량(PrewarmCount/MaxSize) 선언 (프리팹 속성)
├── EffectHandle.cs           구간 이펙트 정지 토큰 — 한 Play로 스폰된 인스턴스 묶음을 Stop
├── EffectServiceRunner.cs    StartDelay 지연 실행용 코루틴 호스트 (풀 루트에 부착)
├── PooledEffectHandle.cs     인스턴스 재생 제어(속도/방출 컷/노브 3층: 머티리얼·파티클·MPB) + 풀 반납 (ParticleStopped / Fixed) + StopWindowed
├── ParticleStopRelay.cs      최상위 파티클의 Stop 콜백을 핸들로 릴레이
├── EffectParameterSet.cs     프리팹이 노출할 셰이더 노브 선언 (에디터 메타데이터)
└── EffectParamApplier.cs     노브 3층 적용기 모음 (런타임/프리뷰 공용) — EffectParamApplier(MPB) · ParticleParamApplier+ParticleBaseline(모듈) · EffectMaterialApplier(머티리얼 스왑)

Assets/04.Scripts/Core/
└── EffectOwnership.cs        캐릭터 config에서 이펙트 프리팹을 유도해 풀에 소유권 등록/해제 (state machine이 호출)

Assets/04.Scripts/Combat/                이펙트 연동
├── EffectHitVolume.cs        이전 Effect Payload Hit 데이터 호환용 재생 리스너
└── EffectProgressDriver.cs   파티클 시간 → 셰이더 _Progress 구동 (MPB 격리)

Assets/05.Editor/
├── EffectTool/               전용 편집 도구 (ZZZ/Effect Tool) — partial 분할
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

## Entry 이펙트 모듈

프리팹의 시각 설정을 복제하지 않고 조합마다 다른 움직임을 주기 위해
`CompositeEffectEntry.Modules`가 `[SerializeReference]` 모듈 목록을 보유한다.
Effect Tool은 구체 기능을 직접 알지 않고 `EffectModule` 파생 타입을 검색해 추가·편집한다.

| 모듈 | 책임 |
|---|---|
| `ArcMotionEffectModule` | 캐릭터 중심 기준 원호 위치와 진행 시간 |
| `FaceOutwardEffectModule` | 현재 방사 방향으로 회전과 축 오프셋 적용 |
| `BakeToWorldEffectModule` | 움직임 중 Custom 좌표계를 사용하고 종료 후 World 좌표로 변환 |
| `ParticlePlaybackEffectModule` | Duration, Playback Speed, Start Lifetime |
| `ParticleAppearanceEffectModule` | Size over Lifetime 커브와 Start Color |
| `MaterialOverrideEffectModule` | 렌더러 sharedMaterial 교체 |

런타임에서는 풀 인스턴스의 `EffectModuleRunner`가 Entry 설정으로부터 실행 상태를 새로 만든다.
모듈 설정 객체에는 시간·Transform 같은 인스턴스 상태를 저장하지 않는다. 모듈 실행 순서는
위치 → 회전 → 시뮬레이션 채널 순으로 Runner가 정렬하므로 Tool의 목록 순서와 무관하다.

에디터 프리뷰는 같은 모듈의 `EvaluatePreview`를 사용하고 파티클을 작은 시간 단계로 시뮬레이션해
런타임의 이동 방출 궤적을 재현한다.

파티클 노브와 머티리얼은 모듈이 설정을 소유하지만 실제 적용과 baseline 복원은
`PooledEffectHandle`이 담당한다. 풀 인스턴스가 다른 Entry에 재사용되어도 이전 재생의 값이 남지 않는다.
기존 Entry 필드는 직렬화 호환용 fallback으로 유지하며, Effect Tool에서 에셋을 열면 값이 있는 항목만
대응 모듈로 옮기고 기존 필드는 중립값으로 초기화한다.

### 캐릭터 소켓 동기화와 좌표계 전환

- Effect Notify는 상태 갱신 중 판정하지만 `PlayAfterAnimation`으로 예약해 같은 프레임의
  `LateUpdate`에서 최신 Animator 소켓 포즈를 읽고 생성한다. Start Delay가 있는 Entry도
  지연 시간이 끝난 프레임의 LateUpdate에서 생성한다.
- `EffectPlayContext`가 재생 요청별 `Spawner`와 `CharacterRoot`를 전달한다.
  `EffectService`는 해당 캐릭터의 루트를 Custom Simulation Space와 EffectModule에 연결하므로,
  플레이어와 다수 몬스터가 전역 풀을 공유해도 좌표계는 서로 간섭하지 않는다.
- `ArcMotionEffectModule`은 앞을 0도로 하는 캐릭터 기준 원호를 계산한다. 진행 중 파티클은
  Custom 공간에서 캐릭터를 따라가고, `BakeToWorldEffectModule`이 스윙 종료 후 위치·속도·축을
  World 공간으로 변환해 후속 동작에는 끌려가지 않게 한다.
- 파티클 프리팹은 방출과 렌더링 원본만 보유한다. 기존 `RadialParticleEmitter` 같은 연출용
  컴포넌트는 제거하고, 원호·방향·좌표계·노브는 CompositeEffect Entry 모듈로 조합한다.

### 상태 전환 수명 정책

Effect Notify는 `TransitionMode`로 상태·구간 전환 시 수명을 결정한다. 기본값은 `Keep`이다.

| 모드 | 동작 |
|---|---|
| `Keep` | Notify 시점에 즉시 재생하고 ConfigState의 소유권을 버린다. 프리팹의 자연 종료나 Duration까지 유지된다. |
| `Stop` | Notify 시점에 즉시 재생한다. `NextSection`이 비면 현재 섹션 이탈 시 정지하고, 지정하면 그 목적지 섹션까지 소유권을 전달해 목적지 이탈 시 정지한다. |
| `Next` | Notify 시점에는 생성하지 않고 예약한다. 실제 Link 목적지가 `NextSection`과 일치할 때만 생성하며, 목적지 상태가 소유권을 넘겨받아 그 상태를 나갈 때 정지한다. |

`Next`는 현재 섹션의 Link 목적지 중 하나를 Animation Tool의 `Next Section` 드롭다운에서 고른다.
다른 분기가 선택되면 예약을 폐기하므로 잘못된 분기에서 한 프레임 생성됐다가 사라지는 현상이 없다.
목적지에서 생성할 때도 `PlayAfterAnimation`을 사용해 중간 프레임 진입 후 최신 소켓 포즈를 읽는다.

현재 섹션부터 보여야 하고 다음 홀드 루프까지 이어져야 하는 Effect는 `Stop`의 `Carry Section`에
그 루프 섹션을 지정한다. 실제 목적지가 일치할 때 기존 핸들을 전달하며, 다른 분기로 나가면 현재
섹션 이탈 시 정지한다. 전달받은 루프 섹션의 self-link에서는 핸들을 유지하고 루프를 벗어날 때 정지한다.

같은 섹션을 대상으로 하는 self-link는 애니메이션 타임라인만 재진입하며 전환 관리 Effect의
논리적 수명은 이어진다. 활성 `Stop` 핸들과 `Next` 발동 상태, 아직 목적지가 확정되지 않은 `Next`
예약을 보존하므로 홀드 루프 중에는 중복 생성하거나 정지하지 않는다. 자연 종료하는 `Keep`, 이미
종료 구간에 도달한 interval `Stop`, 일반 이벤트 Notify는 재진입마다 다시 발동할 수 있다. 다른
섹션으로 전환할 때는 기존 수명 정책을 적용한다.

구간 Effect는 전환 정책과 별개로 같은 구간 안에서 `EndNormalizedTime`에 도달하면 정지한다.
`Next`로 전달된 Effect와 `Stop` Effect는 종료 요청에서 `BakeToWorldEffectModule`을 먼저 실행한 뒤
방출을 멈춘다. 오래 유지한 `EffectHandle`이 이미 풀에 반환되어 다른 재생에 쓰인 인스턴스를
잘못 정지하지 않도록 `PooledEffectHandle`의 바인딩 세대가 일치할 때만 종료 요청을 적용한다.

### Bake To World와 소켓 추종

`BakeToWorldEffectModule.Follow Root`는 World 베이크 전 시뮬레이션 기준을 선택한다.

- 켬: 캐릭터 루트 기준 Custom 공간을 사용한다.
- 끔: Effect의 Local 공간을 사용해 `FollowSpawner`가 복사한 소켓 포즈를 그대로 따라간다.
- 종료 요청: 모든 하위 `ParticleSystem`의 위치·속도·회전축을 World 공간으로 변환하고 방출을 멈춘다.

소켓 빔처럼 재생 중 무기를 따라야 하는 Effect는 `FollowSpawner`를 켜고 `Follow Root`를 끈다.
Animation Tool의 모듈 프리뷰는 Clear 후 첫 시뮬레이션 스텝에서 파티클을 다시 시작해 런타임과
같은 이펙트를 표시한다.

---

## Effect와 Hit 생명주기 연동

기본 공격 판정은 별도의 Hit Notify가 데이터를 소유하고 `BindingKey`로 이펙트 원점만 참조한다.
클립을 넘어가는 이펙트와 판정 생명주기를 완전히 묶어야 할 때는 Effect Notify의
`Sync Hit With Effect`를 사용한다. 이 경우 Effect Notify가 `HitData`를 함께 소유하며,
`HitData.EffectKey`와 Entry의 `BindingKey`가 같은 실제 풀 인스턴스 하나에서 판정이 시작된다.
Entry의 `PooledEffectHandle`이 정지되거나 풀에 반납될 때 판정도 종료되므로 `Stop`/`Next` 정책으로
다음 섹션에 이월된 이펙트도 판정을 유지한다. 확장형 판정의 진행도는 `HitData.Duration`을 사용한다.
Entry에 `Duration`이 지정된 경우에도 그 시점에 판정을 종료하고 `BakeToWorldEffectModule`을 실행하므로,
소켓을 따라가던 활성 판정은 사라지고 월드 공간에 구운 파티클 꼬리만 남는다.

프리팹에 기존 `EffectHitVolume`이 붙어 있으면 해당 컴포넌트가 같은 수명 연동을 담당하고,
그렇지 않으면 `PooledEffectHandle`이 판정을 직접 구동한다.

Hit 데이터를 별도 Notify에서 관리하려면 Hit Notify의 `Sync With Effect`를 사용할 수 있다.
이 옵션은 `Origin`을 Effect로 고정하고 `Effect Key`가 가리키는 현재 풀 인스턴스에 판정을 붙인다.
Hit Notify가 Effect보다 먼저 발동한 경우에는 같은 섹션에서 해당 Binding이 등록될 때까지 재시도하며,
연결된 뒤에는 Effect의 정지·반납과 함께 판정을 종료한다. 같은 `NormalizedTime`에 있는 Effect Notify가
`Next`라면 즉시 Binding을 찾지 않고 `BindingKey`가 일치하는 예약 Effect에 Hit를 함께 저장한다.
Notify 목록 순서와 관계없이 목적 섹션에서 Effect가 생성될 때 판정도 함께 시작한다.

### Effect 연동 Hit 작성 체크리스트

- CompositeEffect Entry의 `BindingKey`와 Hit의 `Effect Key`는 대소문자를 포함해 정확히 맞춘다.
- 같은 손·발사체 역할을 여러 섹션에서 재사용하면 동일한 키를 써도 된다. 좌우처럼 동시에 구분해야 하는
  원점은 `Eff_FireBeam_Enhance_L`, `Eff_FireBeam_Enhance_R`처럼 서로 다른 키를 사용한다.
- 현재 섹션에서 바로 보여야 하는 Effect는 `Keep` 또는 `Stop`을 사용한다. `Next`는 지정한 목적 섹션에
  진입한 뒤 생성해야 하는 Effect에만 사용한다.
- 별도 Hit Notify를 `Next` Effect와 묶을 때는 두 Notify의 `NormalizedTime`을 같게 두고
  `Sync With Effect`를 켠다. `Next Section`도 실제 Link 목적지와 일치해야 한다.
- Effect Notify 내부 Hit와 별도 `Sync With Effect` Hit를 같은 Effect에 중복 설정하지 않는다.

첫 실행에서 Effect 또는 Hit가 빠지면 먼저 Transition Mode를 확인한다. `Next`는 현재 섹션에서
Binding을 만들지 않으므로 목적 섹션 전환 전에는 보이지 않는 것이 정상이다. Effect는 보이지만 Hit만
없다면 `BindingKey`/`Effect Key`, `NormalizedTime`, `Sync With Effect` 순서로 확인한다.

---

## 남은 것 / 로드맵

- **구간형(지속) 노티파이** — ✅ 구현됨. `TrackNotify.EndNormalizedTime`로 `[Start, End]` 구간 유지 → [구간 이펙트](#구간interval-이펙트--시점이-아니라-start-end) 참조. 남은 건 플레이 모드 실전 검증(트레일)
- **`SendMessage` → 이벤트 릴레이** — Effect 외 Notify(`EventName`) 디스패치는 아직 SendMessage (리플렉션 할당) → 캐릭터별 이벤트 릴레이(강타입 `event Action<string>`, 인스턴스 스코프)로 교체 예정 ([TODO.md](TODO.md))
- **Addressables 전환** — 모바일 대비, 스킬 VFX를 사용 직전 로드/종료 후 Release ([TODO.md](TODO.md) 모바일 절)
