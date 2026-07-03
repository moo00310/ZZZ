# 이펙트 아키텍처

## 전체 구조도

```
[ AnimationConfig — TrackNotify(Type=Effect) ]     ← "무엇을 언제 터뜨릴지"만 안다
    │   Notify는 CompositeEffect(SO) 하나만 참조 — 프리팹/풀/배치를 모른다
    ▼   ConfigState.DispatchNotify → EffectService.Play(composite, spawner)
[ EffectService ]             ← static 런타임 진입점 (AnimConfig가 아는 유일한 이펙트 API)
    │   · 조합의 Entry들을 순회 — StartDelay > 0 이면 EffectServiceRunner(코루틴 호스트)로 지연 재생
    │   · 소켓 본 검색(FindSocket) → 배치(FollowSpawner / 스폰 위치 분리) → 파티클 재시작
    ▼
[ EffectPool ]                ← 프리팹 단위 풀 (Get / Release + 프리워밍, MaxSize 초과분 파괴)
    │   같은 프리팹을 여러 조합이 써도 풀은 공유된다 (키 = 프리팹 GameObject)
    ▼
[ 이펙트 인스턴스 ]
    ├── PooledEffectHandle        자기 자신의 풀 반납 담당 (ParticleStopped / Fixed)
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

### 왜 원자(개별 이펙트)용 SO를 따로 두지 않았나

초기안은 `EffectDefinition`(원자 1개 = SO 1개) + `CompositeEffect`(조합)의 2단 SO였다.
그러나 **원자마다 에셋을 만들어 관리하는 비용**이 실익보다 컸다 — 원자의 설정(배치/풀링/반납)은
대부분 "그 조합 안에서의" 값이지 프리팹의 고유 속성이 아니었다. 그래서 `EffectDefinition`은 폐기하고,
`CompositeEffectEntry`가 **프리팹을 직접 참조**하며 설정을 자체 보유한다. 단일 이펙트도 Entry 1개짜리
조합으로 표현하므로 Notify 쪽엔 타입 분기가 없다.

> 트레이드오프 — 같은 프리팹을 쓰는 Entry가 여럿일 때 풀 설정(`PrewarmCount`/`MaxSize`)은
> **최초로 풀을 만든 Entry의 값**이 쓰인다(풀은 프리팹당 하나). 원자 공통 설정의 "단일 출처"가
> 없는 대신, 에셋 수가 절반으로 줄고 편집 동선이 조합 하나로 끝난다.

### 시차(딜레이)의 두 층위

| 층위 | 저장 위치 | 편집 |
|------|-----------|------|
| **조합 내 프리팹 간 시차** | `CompositeEffectEntry.StartDelay` (SO) | EffectTool / Effect 탭 타임라인 드래그 |
| **프리팹 내부 서브파티클 시차** | 각 `ParticleSystem.main.startDelay` (프리팹 자체) | 타임라인 드래그가 프리팹에 직접 굽는다 |

내부 시차를 자체 시퀀스 SO나 런타임 재생기(PlayableDirector 등)로 만들지 않은 이유:
저장 데이터가 파티클 Start Delay 그 자체면 **런타임 비용이 0**이고, 풀링 단위(프리팹 = 1유닛)가 자명해진다.
Unity Timeline은 외부 Instantiate·인스턴스 리바인딩이 필요한 오케스트레이션엔 부적합해 쓰지 않았다.

---

## CompositeEffect — 조합 데이터 (SO)

[CompositeEffect.cs](../Assets/04.Scripts/Effects/CompositeEffect.cs) — `List<CompositeEffectEntry>` 하나가 전부다.

| Entry 필드 | 의미 |
|------------|------|
| `Prefab` | 재생할 이펙트 프리팹 (서브파티클 + 내부 Start Delay 번들) |
| `StartDelay` | 이 조합 안에서의 상대 시차(초) |
| `Socket` | 붙일 본/소켓 이름 (빈값 = 스포너 원점). 스포너 계층에서 이름으로 재귀 검색 |
| `PositionOffset` / `EulerOffset` / `Scale` | 소켓 기준 로컬 배치 |
| `FollowSpawner` | true = 소켓에 부모로 붙어 따라감 / false = 스폰 순간 위치에 분리(투사체 잔상 등) |
| `PrewarmCount` | 로드 시 미리 생성(첫 스폰 히칭/GC 방지) — 프리팹당 최초 1회 |
| `MaxSize` | 풀 상한 (0 = 무제한). 초과분은 반납 시점에 파괴 |
| `Despawn` | `ParticleStopped`(파티클 전부 정지 시 자동 반납, 권장) / `Fixed`(Lifetime 초 뒤 강제 반납) |
| `Lifetime` | `Fixed`일 때만 사용(초) — Looping 등 스스로 안 멈추는 이펙트용 |

---

## EffectService — 런타임 진입점

[EffectService.cs](../Assets/04.Scripts/Effects/EffectService.cs) — static 클래스. `Play(composite, spawner)` 하나가 공개 API다.

```
Play(composite, spawner)
    │  Entry 순회
    ├── StartDelay ≤ 0  → 즉시 PlayEntry
    └── StartDelay > 0  → EffectServiceRunner.Delay(코루틴) → PlayEntry
                           (지연 중 spawner가 파괴되면 스킵)
PlayEntry(entry, spawner)
    ├── GetOrCreatePool(prefab)      풀이 없으면 생성 (Prewarm 수행) — lazy
    ├── pool.Get()                   재사용 or 신규 인스턴스
    ├── FindSocket → PlaceInstance   소켓 본 검색 + FollowSpawner에 따라 부착/분리 배치
    ├── PooledEffectHandle.Bind      반납 방식 바인딩 (매 재생마다)
    └── SetActive + RestartParticles 루트 파티클만 Play(true) → 자식은 내부 Start Delay로 순차 재생
```

- **풀 보관 루트** — 비활성 인스턴스는 `DontDestroyOnLoad`된 `EffectPool` 오브젝트 아래에 정리된다.
- **코루틴 호스트** — static 클래스는 코루틴을 못 돌리므로, 지연 재생은 풀 루트에 붙인
  [EffectServiceRunner](../Assets/04.Scripts/Effects/EffectServiceRunner.cs)가 대신 돌린다.
- **Enter Play Mode 대응** — 도메인 리로드를 끈 설정에서도 이전 플레이의 static 풀/러너가 새지 않도록
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`에서 상태를 리셋한다.

---

## 자동 반납 — PooledEffectHandle + ParticleStopRelay

풀링에서 제일 까다로운 건 "언제 돌려놓느냐"다. 파티클의 **Stop Action = Destroy**는 풀에서 못 쓰므로
(인스턴스가 진짜 파괴됨), **Stop Action = Callback** + `OnParticleSystemStopped` 콜백으로 반납한다.

[PooledEffectHandle.cs](../Assets/04.Scripts/Effects/PooledEffectHandle.cs)가 인스턴스 루트에 붙어 반납을 담당한다.

| DespawnMode | 동작 |
|-------------|------|
| `ParticleStopped` | 인스턴스 안의 **최상위 ParticleSystem 전부**(다른 파티클의 자식이 아닌 것)에 [ParticleStopRelay](../Assets/04.Scripts/Effects/ParticleStopRelay.cs)를 붙여 각자의 Stop 콜백을 받고, **전부 멈추면** 카운트다운이 끝나 반납 |
| `Fixed` | `Lifetime`초 뒤 강제 반납 (Looping 등 자동 정지가 없는 이펙트용) |

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

---

## 에디터 툴

### EffectTool (`ZZZ/Effect Tool`) — 조합 전용 편집 창

[Assets/05.Editor/EffectTool/](../Assets/05.Editor/EffectTool/) — partial class로 영역 분할.

| 영역 | 기능 |
|------|------|
| 목록 (`List`) | 프로젝트의 모든 `CompositeEffect` 브라우징 + New Composite 생성 |
| 타임라인 (`Timeline`) | Entry `StartDelay`를 시간축 막대로 표시 — **드래그로 시차를 데이터에 굽는다** |
| 인스펙터 (`Inspector`) | Entry별 프리팹/배치/풀링/반납 편집 |
| 씬 프리뷰 (`Preview`) | `ParticleSystem.Simulate` 스크럽으로 플레이 진입 없이 조합 연출 확인 |
| 풀 개요 (`Pool`) | 플레이 중 프리팹별 풀 상태(Free/Live/Created/Max) 모니터 |

### AnimationConfigTool "Effect" 탭 — 애니와 같은 시간축에서 편집

이펙트 타이밍의 기준은 결국 **애니메이션 프레임**이다. 그래서 조합 편집 기능을
[AnimationConfigTool.EffectPreview.cs](../Assets/05.Editor/AnimationTool/AnimationConfigTool.EffectPreview.cs)
(Effect 탭)로 흡수해, 캐릭터 애니 프리뷰를 보면서 한 자리에서 조정한다.

- **발동 시점 편집** — 선택한 Effect Notify의 `NormalizedTime`을 프레임 표시와 함께 슬라이더/마커 드래그로 조정
- **소켓 프리뷰** — 각 Entry의 발동 시점(`섹션 시작 + NormalizedTime×클립길이 + StartDelay`)에 맞춰
  조합 원자들을 **캐릭터 소켓 본에 붙여 `Simulate`** — 트랙 스크럽/편집 중에도 현재 플레이헤드에 즉시 반영
- **인라인 조합 편집** — Entry별 소켓/오프셋/시차/풀링 + StartDelay 타임라인 + 풀 개요를 탭 인스펙터에 내장
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
├── CompositeEffect.cs        조합 SO + Entry(프리팹 직접 참조 + 배치/풀링/반납 설정)
├── EffectService.cs          ★ 진입점 — Play(조합) / 프리팹별 풀 관리 / 소켓 검색·배치
├── EffectPool.cs             프리팹 단위 인스턴스 풀 (Get/Release + 프리워밍 + MaxSize)
├── EffectServiceRunner.cs    StartDelay 지연 실행용 코루틴 호스트 (풀 루트에 부착)
├── PooledEffectHandle.cs     인스턴스 자신의 풀 반납 (ParticleStopped 카운트다운 / Fixed)
└── ParticleStopRelay.cs      최상위 파티클의 Stop 콜백을 핸들로 릴레이

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

- **구간형(지속) 노티파이** — 현재 Notify는 시점 발동. `[Start, End]` 구간 동안 유지되는 이펙트는 미지원 (Looping + `Fixed` 반납으로 우회 가능)
- **`SendMessage` 대체** — Effect 외 Notify(`EventName`) 디스패치는 아직 SendMessage (리플렉션 할당) → 이벤트/델리게이트로 교체 검토
- **Addressables 전환** — 모바일 대비, 스킬 VFX를 사용 직전 로드/종료 후 Release ([TODO.md](TODO.md) 모바일 절)
