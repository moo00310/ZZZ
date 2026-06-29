# ZZZ — 데이터 기반 전투 애니메이션 데모 (Unity URP)

> 플레이어(Burnice)가 허수아비를 공격하는 URP 전투 연출 데모.
> 핵심은 **"Animator에 로직을 쌓지 않고, 전투 흐름을 데이터(ScriptableObject)로 정의하고, 그 데이터를 커스텀 에디터 툴로 시각 편집한다"** 는 파이프라인을 직접 설계·구현한 것.

---

## 🎯 한 줄 요약

전투 연출(콤보 · 강화공격 · 피격 · 회피 · 패링 · 루트모션 이동)을
**C# 상태 러너 + 데이터 트랙(ScriptableObject) + 커스텀 에디터 툴** 의 3층 구조로 만들고,
"무엇을 언제 재생할지"를 코드가 아니라 **에셋 편집만으로** 구성할 수 있게 했다.

> 📖 구현 내부 동작·문제해결의 상세 설명은 **[AnimationArchitecture.md](Documentation/AnimationArchitecture.md)**,
> 프로젝트 구성·코딩 규칙·로드맵은 **[프로젝트_개요.md](Documentation/프로젝트_개요.md)** 참고.

---

## 🏛️ 아키텍처 — 책임을 셋으로 쪼갰다

```
┌─────────────────────────────────────────────────────────────┐
│  분기 로직        →  C# 러너  (ConfigState 1개)              │
│  무엇을 언제 재생  →  AnimationConfig  (ScriptableObject 트랙) │
│  클립 재생만      →  Unity Animator  (파라미터/Transition 無) │
└─────────────────────────────────────────────────────────────┘
```

**왜 이렇게 만들었나 (이 프로젝트가 풀려는 문제)**
Animator Controller에 Trigger / Bool / Transition 화살표를 쌓는 전통 방식은
콤보·피격이 복잡해질수록 그래프가 거미줄이 되고, 디버깅과 확장이 지옥이 된다.
그래서 **Animator는 `CrossFade(클립명)`으로 클립만 틀고**, 분기는 코드로,
"전이 흐름(언제 무엇으로 넘어가는가)"은 데이터로 분리했다.
결과적으로 Animator Controller에는 파라미터도 Transition 화살표도 없다.

| | 장점 | 단점 (트레이드오프) |
|---|------|------|
| 데이터 분리 | 새 콤보/연출을 코드 수정·재컴파일 없이 에셋 편집만으로 추가. 디자이너 협업 가능 | 데이터 스키마를 직접 설계·유지해야 하고, 전이 흐름이 코드에 안 보여 **전용 에디터 툴이 없으면 추적이 어렵다** (그래서 툴을 만들었다) |
| Animator 파라미터리스 | 거미줄 Transition 그래프 제거, 전이 타이밍을 코드/데이터가 일원 관리 | Animator 기본 기능(블렌드 트리·상태머신 전이)을 포기 → 블렌딩·시간계산을 직접 구현해야 함 |

---

## 🧩 핵심 시스템 (왜 → 어떻게 → 장단점)

### 1. `AnimationConfig` — 데이터로 정의·구동하는 전투 런타임

**기능** — 코드 수정 없이 ScriptableObject 에셋만 편집해 전투 흐름(콤보·강화공격·피격·회피·패링·이동)을 구성하는 데이터 시스템. 언리얼의 "애님 몽타주"와 비슷한 트랙 개념.

**동작** — 한 `AnimationConfig`는 섹션(`TrackClip`) 목록을 갖고, 각 섹션은 클립·재생속도·이동방식(`MoveMode`)·회전잠금에 더해 아래 3종을 데이터로 매단다.

| 구성요소 | 하는 일 |
|------|------|
| [**Links (전이)**](Documentation/AnimationArchitecture.md#cliplink-전이-정의) | "이 섹션에서 → 어떤 입력/타이밍이면 → 어느 config·섹션으로"를 분기. `Timing`(`WhenMatched`/`OnRelease`/`OnEnd`/`OnEndIfMatched`) + `Window`/`EntryOffset`/`RequireHeld`로 콤보·차지·중간진입 제어 |
| [**Notifies**](Documentation/AnimationArchitecture.md#피격-외부-이벤트-진입) | 재생 중 특정 시점에 이벤트/이펙트 발동 (피격 흔들림 신호 등) |
| [**Modules (플러그인)**](Documentation/AnimationArchitecture.md#섹션-모듈-sectionmodule--기능을-끼워-넣는-플러그인) | `[SerializeReference]` 폴리모픽 — **새 판정/연출 = 클래스 1개 상속**. 무적(`IFrameModule`)·패링(`ParryModule`) 등 |

- **장점** — 전투 콘텐츠 확장이 "에셋 추가 + 섹션 이름 규약"으로 끝난다. 분기·판정·이벤트가 한 트랙에 모여 데이터만 봐도 흐름이 읽힌다.
- **단점** — 직렬화 호환(enum 값 보존 등)을 신경 써야 하고, 폴리모픽 `SerializeReference`는 타입 이동/리네임에 취약하다.

### 2. `ConfigState` — config를 파싱해 모든 흐름을 굴리는 단일 러너

**기능** — 걷기·콤보·강화공격·대시·피격·회피·패링을 **이 한 클래스**가 config를 읽어 구동한다.

**동작** — 별도 State 클래스를 갈아끼우는 상태머신이 아니라, **config를 갈아끼우는(`SwitchConfig`/`InterruptWith`)** 방식. 섹션 진입 후 경과시간으로 normalizedTime을 직접 계산해 Link/Notify/Module 윈도우를 평가한다. (과거의 `IState`/`StateBase`/`StateMachine` 프레임워크와 하드코딩 State는 모두 제거·통합)

> **공유 엔진** — `ConfigState`는 플레이어 구상 타입이 아니라 인터페이스(`ConfigDriving.cs`)에만 의존해, **몬스터(Durahan)도 같은 엔진으로 Idle+Hit를 구동**한다(현재 스캐폴드). 전이 조건도 폴리모픽 `LinkCondition`(`InputCondition`/`AlwaysCondition` … 몬스터 거리·체력 등 확장)으로 빠져, 새 조건 = 클래스 1개 추가다. 자세한 내용은 [AnimationArchitecture.md](Documentation/AnimationArchitecture.md#몬스터-공유-엔진-재사용) 참고.

- **장점** — 상태가 늘어도 클래스가 안 늘어난다(데이터만 늘어남). 전이 로직이 한 곳에 모여 디버깅이 단순. 캐릭터 종류가 늘어도 엔진은 하나.
- **단점** — 러너 하나가 모든 흐름을 책임지므로 이 클래스 자체는 커지고, 특수 케이스가 늘면 분기 비용이 생긴다.

### 3. `AnimationConfigTool` — config를 시각 편집하는 에디터 툴 ★ 핵심 결과물

**기능** — `AnimationConfig`를 코드 없이 편집하는 커스텀 EditorWindow(IMGUI).

| 기능 | 설명 |
|------|------|
| [**타임라인 편집**](Documentation/AnimationArchitecture.md#에디터-툴-연동) | 섹션 배치 · Link(베지어 연결선) · Notify · Module을 타임라인 위에서 시각 편집 |
| [**Combo 프리뷰**](Documentation/AnimationArchitecture.md#에디터-툴-연동) | 입력을 눌러두면 Link 흐름을 그대로 재생 — CrossFade 블렌딩과 루트모션을 에디터에서 시뮬레이션 |
| [**라이브 모니터**](Documentation/AnimationArchitecture.md#에디터-툴-연동) | 플레이 중 현재 config / 섹션 / 입력 버퍼 / 진행도를 실시간 표시 |

- **장점** — "데이터로 분리해서 흐름이 안 보인다"는 단점을 정면으로 메운다. 빌드/플레이 없이 콤보를 시각 검증.
- **단점** — IMGUI라 레이아웃 코드를 손으로 짜야 하고, 툴 자체의 유지보수 비용이 든다.

---

## 🥊 문제와 해결 (단순 구현이 아니라 "왜 튀는가"를 파고든 기록)

각 시스템에서 부딪힌 **문제와 그 해결**은 `AnimationArchitecture.md`의 해당 섹션 "문제와 해결"에 정리.

- [루트모션 — Unity 기본 대신 본에서 직접 추출 & "튐" 버그 6종](Documentation/AnimationArchitecture.md#루트모션-직접-구현)
- [타겟 워프 & 섹션 턴 — 애니 원본으로 적을 못 맞추는 문제 보정](Documentation/AnimationArchitecture.md#타겟-워프--섹션-턴-공격-보정)
- [피격 — additive 레이어로 반응 포즈 위에 흔들림 얹기 & 반응 escalation](Documentation/AnimationArchitecture.md#피격-외부-이벤트-진입)
- [회피 / 패링 — i-frame은 '무시', 패링은 '반격으로 응수' (대칭 설계)](Documentation/AnimationArchitecture.md#회피-dodge--evade)

---

## 🗺️ 앞으로 할 일 (로드맵 — 아직 미구현)

진행 중 작업·로드맵은 [TODO.md](Documentation/TODO.md) 참고. 아래는 **목표이며 현재 코드에는 없다.**

**주요 목표 — 모바일 빌드 & 최적화**
PC 데모에 그치지 않고 **Android 실기 빌드 + 안정적 프레임**을 목표로,
*측정(Profiler) → 병목 확인 → 최적화* 순으로 진행 예정. (Addressables 비동기 로딩, 툰 셰이더 모바일 대응, 텍스처 압축 등)

---

## 🧱 기술 스택 (현재 실제로 쓰는 것만)

`Unity` · `URP` · `C#` · `Input System(신형)` · `Custom Editor (IMGUI EditorWindow)` · `ScriptableObject 데이터 설계` · `직접 구현한 루트모션 / TPS 카메라`

> 모바일 빌드 · Addressables · 툰 셰이더 / RenderFeature 는 **로드맵(미구현)** 이라 위 스택에서 제외했다.
