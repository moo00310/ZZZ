# ZZZ — 데이터 기반 전투 애니메이션 데모 (Unity URP)

> 플레이어(Burnice)가 허수아비/몬스터(Durahan)를 공격하는 URP 전투 연출 데모.
> 핵심은 **"Animator에 로직을 쌓지 않고, 전투 흐름을 데이터(ScriptableObject)로 정의하고, 그 데이터를 커스텀 에디터 툴로 시각 편집한다"** 는 파이프라인을 직접 설계·구현한 것.

---

## 🎯 한 줄 요약

전투 연출(콤보 · 강화공격 · 피격 · 회피 · 패링 · 루트모션 이동)을
**C# 상태 러너(ConfigState) + 데이터 트랙(ScriptableObject) + 커스텀 에디터 툴** 의 3층 구조로 만들고,
"무엇을 언제 재생할지"를 코드가 아니라 **에셋 편집만으로** 구성할 수 있게 했다.


> 📖 구현 내부 동작·문제해결의 상세는 **[AnimationArchitecture.md](Documentation/AnimationArchitecture.md)** ·
> **[EffectArchitecture.md](Documentation/EffectArchitecture.md)**,
> 자료구조 선택 철학은 **[자료구조_선택.md](Documentation/자료구조_선택.md)** 참고.
> 코딩·커밋 컨벤션은 아래 [컨벤션](#-컨벤션) 절, 진행 중 작업·로드맵은 **[TODO.md](Documentation/TODO.md)**.

---

## 🎬 애니메이션 시스템 — 현재 핵심 결과물

### 핵심 아이디어 — 분기는 코드, 흐름은 데이터, 재생은 Animator

```
┌─────────────────────────────────────────────────────────────┐
│  분기 로직        →  C# 러너  (ConfigState 1개)              │
│  무엇을 언제 재생  →  AnimationConfig  (ScriptableObject 트랙) │
│  클립 재생만      →  Unity Animator  (파라미터/Transition 無) │
└─────────────────────────────────────────────────────────────┘
```

Animator Controller에 Trigger / Bool / Transition 화살표를 쌓는 전통 방식은 콤보·피격이 복잡해질수록
그래프가 거미줄이 되고 디버깅·확장이 지옥이 된다. 그래서 **Animator는 `CrossFade(클립명)`으로 클립만 틀고**,
분기는 코드로, "전이 흐름(언제 무엇으로 넘어가는가)"은 데이터로 분리했다. 결과적으로 Animator Controller에는
파라미터도 Transition 화살표도 없다.

| 관점 | 기존 — Animator 그래프 방식 | 이 프로젝트 — 코드+데이터 분리 |
|------|------|------|
| 분기·전이 | Animator에 Trigger/Bool/Transition 화살표로 구성 | 분기는 C# 러너가, "언제 무엇으로" 흐름은 데이터(Config)가 |
| 콤보/연출 추가 | 그래프에 상태·화살표 추가 (복잡할수록 거미줄) | 에셋 편집만 — 코드 수정·재컴파일 없이, 디자이너 협업 가능 |
| Animator 역할 | 파라미터·전이·블렌딩까지 담당 | 클립 재생(CrossFade)만 |
| 디버깅·확장 | 그래프가 커지면 추적·확장이 지옥 | 흐름이 데이터 한 곳에 모여 읽힘 |
| 이 방식의 비용 | — | 스키마를 직접 설계·유지, 블렌딩·시간계산 직접 구현, **추적용 전용 에디터 툴 필요(그래서 만들었다)** |

### 대표 결과물 (왜 → 어떻게 → 장단점)

**① `AnimationConfig` — 데이터로 정의·구동하는 전투 런타임**
코드 수정 없이 ScriptableObject 에셋만 편집해 전투 흐름을 구성하는 데이터 시스템(언리얼 "애님 몽타주"와 비슷한 트랙).
한 config는 섹션(`TrackClip`) 목록을 갖고, 각 섹션에 아래 3종을 데이터로 매단다.

| 구성요소 | 하는 일 |
|------|------|
| [**Links (전이)**](Documentation/AnimationArchitecture.md#cliplink-전이-정의) | "이 섹션에서 → 어떤 입력/타이밍이면 → 어느 config·섹션으로"를 분기. `Timing`(`WhenMatched`/`OnRelease`/`OnEnd`/`OnEndIfMatched`) + `Window`/`EntryOffset`로 콤보·차지·중간진입 제어. 조건은 다형성 `LinkCondition` |
| [**Notifies**](Documentation/AnimationArchitecture.md#피격-외부-이벤트-진입) | 재생 중 특정 시점에 이벤트/이펙트 발동 (피격 흔들림 신호 등) |
| [**Modules (플러그인)**](Documentation/AnimationArchitecture.md#섹션-모듈-sectionmodule--기능을-끼워-넣는-플러그인) | `[SerializeReference]` 다형성 — **새 판정/연출 = 클래스 1개 상속**. 무적(`IFrameModule`)·패링(`ParryModule`) 등 |

→ 전투 콘텐츠 확장이 "에셋 추가 + 섹션 이름 규약"으로 끝난다. 단, 데이터 스키마를 직접 설계·유지해야 하고 다형성 직렬화는 타입 이동/리네임에 취약하다.

**② `ConfigState` — config를 파싱해 모든 흐름을 굴리는 공유 러너**
걷기·콤보·강화공격·대시·피격·회피·패링을 **이 한 클래스**가 config를 읽어 구동한다. 별도 State 클래스를
갈아끼우는 상태머신이 아니라 **config를 갈아끼우는(`SwitchConfig`/`InterruptWith`)** 방식이다.

> **공유 엔진** — `ConfigState`는 플레이어 구상 타입이 아니라 인터페이스(`ConfigDriving.cs`)에만 의존해, **몬스터(Durahan)도 같은 엔진으로 Idle+Hit를 구동**한다. 전이 조건도 다형성 `LinkCondition`(`InputCondition`/`AlwaysCondition` … 몬스터 거리·체력 등 확장)으로 빠져, 새 조건 = 클래스 1개 추가. 상세 [AnimationArchitecture.md](Documentation/AnimationArchitecture.md#몬스터-공유-엔진-재사용)

→ 상태가 늘어도 클래스가 안 늘어나고(데이터만 늘어남) 캐릭터 종류가 늘어도 엔진은 하나. 단, 러너 하나가 모든 흐름을 책임져 클래스 자체는 커진다.

**③ `AnimationConfigTool` — config를 시각 편집하는 에디터 툴 ★ 핵심 결과물**
`AnimationConfig`를 코드 없이 편집하는 커스텀 EditorWindow(IMGUI). "데이터로 분리해 흐름이 안 보인다"는 단점을 정면으로 메운다.

| 기능 | 설명 |
|------|------|
| [**타임라인 편집**](Documentation/AnimationArchitecture.md#에디터-툴-연동) | 섹션 배치 · Link(베지어 연결선) · Notify · Module을 타임라인 위에서 시각 편집 |
| [**Combo 프리뷰**](Documentation/AnimationArchitecture.md#에디터-툴-연동) | 입력을 눌러두면 Link 흐름을 그대로 재생 — CrossFade 블렌딩과 루트모션을 에디터에서 시뮬레이션 |
| [**라이브 모니터**](Documentation/AnimationArchitecture.md#에디터-툴-연동) | 플레이 중 현재 config / 섹션 / 입력 버퍼 / 진행도를 실시간 표시 (플레이어·몬스터 선택 추적) |

### 대표 문제해결 — "왜 튀는가"를 파고든 기록

각 시스템에서 부딪힌 **문제와 해결**은 `AnimationArchitecture.md`의 해당 "문제와 해결"에 정리.

- [루트모션 — Unity 기본 대신 본에서 직접 추출 & "튐" 버그 6종](Documentation/AnimationArchitecture.md#루트모션-직접-구현)
- [타겟 워프 & 섹션 턴 — 애니 원본으로 적을 못 맞추는 문제 보정](Documentation/AnimationArchitecture.md#타겟-워프--섹션-턴-공격-보정)
- [피격 — additive 레이어로 반응 포즈 위에 흔들림 얹기 & 반응 escalation](Documentation/AnimationArchitecture.md#피격-외부-이벤트-진입)
- [회피 / 패링 — i-frame은 '무시', 패링은 '반격으로 응수' (대칭 설계)](Documentation/AnimationArchitecture.md#회피-dodge--evade)

---

## ✨ 이펙트 시스템 — 조합 실행 + 프리팹 풀링

전투 타격 연출 담당. 애니메이션의 Notify(발동 시점)에서 출발해 **풀링 런타임 + 전용 에디터 툴**까지 구현했다.

### 핵심 아이디어 — 실행은 조합 단위, 풀링은 프리팹 단위

```
┌────────────────────────────────────────────────────────────────┐
│  무엇을 언제      →  AnimationConfig의 Notify  (발동 시점만 안다) │
│  어떤 연출 묶음   →  CompositeEffect  (SO — 프리팹들 + 시차/배치) │
│  꺼내고 돌려놓기  →  EffectService + EffectPool  (프리팹 단위 풀)  │
└────────────────────────────────────────────────────────────────┘
```

하나의 연출(폭발 = 화염+연기+파편)은 여러 프리팹이 시차를 두고 터지지만, 같은 서브 이펙트(hit_spark)는
여러 연출이 재사용한다. 그래서 **실행(Play)은 조합(SO) 단위, 풀(Get/Release)은 프리팹 단위**로 분리했다 —
같은 프리팹을 다른 조합이 다른 시차/배치로 써도 풀은 공유된다.

### 대표 결과물 (왜 → 어떻게 → 장단점)

**① `CompositeEffect` — 프리팹 직접 참조 조합 데이터 (SO)**
원자(개별 이펙트)마다 SO를 만드는 초기안(`EffectDefinition`)은 에셋 관리 비용이 실익보다 커서 폐기하고,
각 Entry가 **프리팹을 직접 참조**하며 시차(`StartDelay`)·배치(소켓/오프셋/추종)·풀링(프리워밍/상한)·반납 설정을 자체 보유한다.
단일 이펙트도 Entry 1개짜리 조합이라 Notify 쪽 타입 분기가 없다.
→ 에셋 수·편집 동선 최소화. 단, 프리팹 공유 시 풀 설정은 최초 생성 Entry 값을 따른다.

**② `EffectService` + `EffectPool` — 풀링 런타임 (GC 절감, 모바일 대비)**
`ConfigState.DispatchNotify`의 `Instantiate`를 [EffectService.Play](Documentation/EffectArchitecture.md#effectservice--런타임-진입점)로
교체 — 프리팹별 풀에서 꺼내 소켓 본에 배치하고, 재생이 끝나면 **자동 반납**한다
([PooledEffectHandle](Documentation/EffectArchitecture.md#자동-반납--pooledeffecthandle--particlestoprelay):
최상위 파티클 전부 정지 감지 / Lifetime 강제 반납). 프리워밍으로 첫 스폰 히칭을 막고 `MaxSize`로 피크만 흡수한다.

**③ `EffectTool` + AnimationConfigTool "Effect" 탭 — 전용 에디터 툴**

| 기능 | 설명 |
|------|------|
| [**EffectTool**](Documentation/EffectArchitecture.md#effecttool-zzzeffect-tool--조합-전용-편집-창) (`ZZZ/Effect Tool`) | 조합 브라우징 · Entry 편집 · **StartDelay 타임라인 드래그** · 씬 Simulate 프리뷰 · 풀 개요 모니터 |
| [**Effect 탭**](Documentation/EffectArchitecture.md#animationconfigtool-effect-탭--애니와-같은-시간축에서-편집) (AnimationConfigTool) | 이펙트 타이밍의 기준은 애니 프레임 — **애니 프리뷰를 보면서** 발동 시점·소켓·오프셋·시차를 조정, 캐릭터 소켓 본에 이펙트를 Simulate로 겹쳐 확인 |

### 함께 구현된 것

- **타격 판정** — 공격 이펙트 프리팹의 `EffectHitVolume`가 스폰 순간 자기 범위(SphereCollider)를 때린다: **이펙트 비주얼 범위 = 타격 범위** (스폰이 Notify라 타격 타이밍도 애니 데이터를 따름)
- **화염 이펙트(FX_FlameBurst)** — 파티클 시간에 맞춰 셰이더 `_Progress`를 구동하는 `EffectProgressDriver` (`MaterialPropertyBlock`으로 인스턴스 격리 — 풀 재사용과 맞물림)

### 대표 문제해결

- [파티클 정지 콜백이 부모로 전파 안 됨 → 최상위 파티클마다 릴레이 부착](Documentation/EffectArchitecture.md#자동-반납--pooledeffecthandle--particlestoprelay)
- [static 서비스의 지연 재생 / Enter Play Mode static 잔존 / 풀 무한 성장 등](Documentation/EffectArchitecture.md#문제와-해결)

> → 구조·경계·반납 메커니즘 상세: **[EffectArchitecture.md](Documentation/EffectArchitecture.md)** · 남은 항목(구간형 노티파이 등)은 **[TODO.md](Documentation/TODO.md)**

---

## 🎨 셰이더 시스템 — 로드맵 (미구현)

캐릭터 룩·전투 연출용 셰이더. 아직 코드는 없다(`03.Shaders` / `06.RenderPipeline` 는 자리만 잡아둔 placeholder).

**방향** — 셀 셰이딩/림라이트 **툰 셰이더** + `ShaderGUI`(키워드 자동 관리), 아웃라인/포스트용
`ScriptableRendererFeature`. 전투 중 셰이더 연출은 `MaterialPropertyBlock`으로 적용해 머티리얼 오염을 막는다.
모바일 타겟이라 half precision·연산 절감을 함께 고려.

> → 상세 계획: **[TODO.md](Documentation/TODO.md)**

---

## 🧭 컨벤션

- **코딩** — [CodingConventions.md](Documentation/CodingConventions.md)
- **커밋·브랜치** — [Git_커밋_컨벤션.md](Documentation/Git_커밋_컨벤션.md)

---

## 🧱 기술 스택 (현재 실제로 쓰는 것만)

`Unity` · `URP` · `C#` · `Input System(신형)` · `Custom Editor (IMGUI EditorWindow)` · `ScriptableObject 데이터 설계` · `직접 구현한 루트모션 / TPS 카메라` · `이펙트 오브젝트 풀링`

> 툰 셰이더 / RenderFeature · 모바일 빌드 · Addressables 는 **로드맵(미구현)** 이라 위 스택에서 제외했다.
