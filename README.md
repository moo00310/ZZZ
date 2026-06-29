# ZZZ — 데이터 기반 전투 애니메이션 데모 (Unity URP)

> 플레이어(Burnice)가 허수아비/몬스터(Durahan)를 공격하는 URP 전투 연출 데모.
> 핵심은 **"Animator에 로직을 쌓지 않고, 전투 흐름을 데이터(ScriptableObject)로 정의하고, 그 데이터를 커스텀 에디터 툴로 시각 편집한다"** 는 파이프라인을 직접 설계·구현한 것.

---

## 🎯 한 줄 요약

전투 연출(콤보 · 강화공격 · 피격 · 회피 · 패링 · 루트모션 이동)을
**C# 상태 러너(ConfigState) + 데이터 트랙(ScriptableObject) + 커스텀 에디터 툴** 의 3층 구조로 만들고,
"무엇을 언제 재생할지"를 코드가 아니라 **에셋 편집만으로** 구성할 수 있게 했다.


> 📖 구현 내부 동작·문제해결의 상세는 **[AnimationArchitecture.md](Documentation/AnimationArchitecture.md)**,
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

## ✨ 이펙트 시스템 — 로드맵 (진행 예정)

전투 타격 연출 담당. 현재는 **타격 판정 스캐폴드**까지 와 있다: 공격 이펙트 프리팹에 붙인 `EffectHitVolume`가
스폰 순간 자기 범위(SphereCollider) 안의 대상을 때린다 — **이펙트 비주얼 범위 = 타격 범위**.

**방향** — 별도 이펙트 툴을 새로 만들지 않고 **`AnimationConfig`의 Notify 시스템을 확장**해 처리한다:
본 소켓 바인딩 · 구간형(지속) 노티파이 · 이펙트 오브젝트 풀링(반복 스폰 GC 절감, 모바일 대비).

> → 상세 계획·체크리스트: **[TODO.md](Documentation/TODO.md)**

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

`Unity` · `URP` · `C#` · `Input System(신형)` · `Custom Editor (IMGUI EditorWindow)` · `ScriptableObject 데이터 설계` · `직접 구현한 루트모션 / TPS 카메라`

> 이펙트(Notify 확장) · 툰 셰이더 / RenderFeature · 모바일 빌드 · Addressables 는 **로드맵(미구현)** 이라 위 스택에서 제외했다.
