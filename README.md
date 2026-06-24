# ZZZ — 데이터 기반 전투 애니메이션 데모 (Unity URP)

> 플레이어(Burnice)가 허수아비를 공격하는 URP 전투 연출 데모.
> 핵심은 **"Animator에 로직을 쌓지 않고, 전투 흐름을 데이터(ScriptableObject)로 정의하고, 그 데이터를 에디터 툴로 시각 편집한다"** 는 파이프라인을 직접 설계·구현한 것.

---

## 🎯 한 줄 요약

전투 연출(콤보 · 피격 · 회피 · 루트모션 이동)을 **C# 상태 머신 + 데이터 트랙 + 커스텀 에디터 툴**의 3층 구조로 만들고,
"무엇을 언제 재생할지"를 코드가 아니라 **에셋 편집만으로** 구성할 수 있게 했다.

> 💡 **왜 Unity인가** — **모바일 환경에 최적화된 전투 게임**을 목표로 Unity를 선택했다.
> 가벼운 빌드와 성숙한 모바일 최적화 파이프라인이 핵심 이유.

> 📖 프로젝트 구성(폴더 구조 · 렌더 파이프라인 · 코딩 규칙 · 로드맵)에 대한 자세한 설명은
> **[프로젝트_개요.md](Documentation/프로젝트_개요.md)** 참고.

---

## 🏛️ 아키텍처: 책임을 셋으로 쪼갰다

```
┌─────────────────────────────────────────────────────────────┐
│  분기 로직        →  C# State Machine  (ConfigState 등)       │
│  무엇을 언제 재생  →  AnimationConfig  (ScriptableObject 트랙) │
│  클립 재생만      →  Unity Animator  (파라미터/Transition 無) │
└─────────────────────────────────────────────────────────────┘
```

**왜 이렇게 했나 (같이 고민한 지점):**
Animator Controller에 Trigger / Bool / Transition 화살표를 쌓는 전통 방식은
콤보·피격이 복잡해질수록 그래프가 거미줄이 되고, 디버깅이 지옥이 된다.
그래서 **Animator는 `CrossFade(클립명)` 으로 클립만 틀고**, 분기는 코드로,
"전이 흐름"은 데이터로 분리했다. 결과적으로 Animator Controller에는
파라미터도 Transition 화살표도 없다.

---

## 🧩 핵심 시스템

### 1. `AnimationConfig` — 데이터로 정의·구동하는 전투 런타임

코드 수정 없이 ScriptableObject 에셋만 편집해 전투 흐름(콤보·피격·회피·이동)을 구성하는 데이터 시스템.

> 각 기능명을 누르면 [AnimationArchitecture.md](Documentation/AnimationArchitecture.md)의 상세 설명으로 이동합니다.

| 기능 | 설명 |
|------|------|
| [**데이터 트랙 (Clips/섹션)**](Documentation/AnimationArchitecture.md#animationconfig--데이터로-정의하는-전이-트랙) | 클립 + 재생 속도 + 이동 방식(`MoveMode`) + 회전 잠금 등을 섹션 단위로 정의 |
| [**Links (전이)**](Documentation/AnimationArchitecture.md#cliplink-전이-정의) | "이 섹션에서 → 어떤 입력/타이밍이면 → 어느 config·섹션으로"를 데이터로 분기. `Timing`(`WhenMatched`/`OnWindowMiss`/`OnEnd`) + `Window`로 콤보 선입력 윈도우 제어 |
| [**Notifies**](Documentation/AnimationArchitecture.md#animationconfig--데이터로-정의하는-전이-트랙) | 재생 중 특정 시점에 이벤트/이펙트 발동 (피격 흔들림 등) |
| [**SectionModule (플러그인)**](Documentation/AnimationArchitecture.md#섹션-모듈-sectionmodule--기능을-끼워-넣는-플러그인) | `[SerializeReference]` 폴리모픽 — **새 연출/판정 = 클래스 1개 상속**. 예: `IFrameModule`(무적 프레임) |
| [**ConfigState (단일 러너)**](Documentation/AnimationArchitecture.md#전체-구조도) | 걷기·콤보·대시·피격·회피를 **이 한 클래스**가 config를 파싱해 구동. 과거 하드코딩 State와 별도 StateMachine 프레임워크는 모두 통합·제거 |
| [**직접 구현한 루트모션**](Documentation/AnimationArchitecture.md#루트모션-직접-구현) | Unity "Apply Root Motion"을 안 쓰고 애니메이션의 이동 본을 직접 추출해 캐릭터를 이동 |

### 2. `AnimationConfigTool` — config를 시각 편집하는 에디터 툴

`AnimationConfig`를 코드 없이 편집하는 커스텀 EditorWindow. **이 프로젝트의 핵심 결과물.**

| 기능 | 설명 |
|------|------|
| [**타임라인 편집**](Documentation/AnimationArchitecture.md#에디터-툴-연동) | 섹션 배치 · Link(베지어 연결선) · Notify · Module을 타임라인 위에서 시각 편집 |
| [**Combo 프리뷰**](Documentation/AnimationArchitecture.md#에디터-툴-연동) | 입력을 눌러두면 Link 흐름을 그대로 재생 — CrossFade 블렌딩과 루트모션을 에디터에서 시뮬레이션 |
| [**라이브 모니터**](Documentation/AnimationArchitecture.md#에디터-툴-연동) | 플레이 중 `PlayerStateMachine`을 추적해 현재 config / 섹션 / 입력 버퍼 / 진행도를 실시간 표시 |

---

## 🥊 문제와 해결

단순히 기능을 추가한 게 아니라, 매 단계 "왜 튀는가 / 왜 끊기는가"를 파고들었다.
각 시스템에서 부딪힌 **문제와 그 해결**은 `AnimationArchitecture.md`의 해당 섹션 안 "문제와 해결" 항목에 정리해 두었다.

- [루트모션 — 직접 구현 & "튐" 버그](Documentation/AnimationArchitecture.md#루트모션-직접-구현)
- [피격 — additive 레이어로 흔들림 연출 & 반응 escalation](Documentation/AnimationArchitecture.md#피격-외부-이벤트-진입)

---

## 🗺️ 앞으로 할 일

진행 중인 작업과 로드맵은 [TODO.md](Documentation/TODO.md) 참고.

**주요 목표 — 모바일 빌드 & 최적화**
PC 데모에 그치지 않고 **Android 실기 빌드 + 안정적 프레임(목표 30/60fps)** 을 목표로 한다.
모바일이라는 명확한 성능 기준을 두고 — *측정(Profiler) → 병목 확인 → 최적화* 순으로:

- **메모리 관리** — Addressables로 무거운 스킬 VFX·캐릭터 프리팹을 필요할 때 비동기 로드, 안 쓰면 해제
- **렌더 최적화** — 툰 셰이더 모바일 대응(half precision), SRP Batcher / GPU Instancing, 텍스처 압축(ASTC)

---

## 🧱 기술 스택

`Unity` · `URP` · `C#` · `Custom Editor (IMGUI)` · `ScriptableObject 데이터 설계` · `직접 구현한 루트모션` · `Android 빌드` · `Addressables`
