# 전투 애니메이션 아키텍처

`AnimationConfig`가 전투 흐름을 저장하고 `ConfigState`가 이를 실행하는 과정을 설명한다. 플레이어와 몬스터는 같은 실행 코드를 사용한다.

| 용어 | 코드 타입 | 의미 |
|---|---|---|
| Section | `TrackClip` | Config 안의 애니메이션 구간 |
| Link | `ClipLink` | Section 사이의 전이 |
| Notify | `TrackNotify` | 재생 중 특정 시점이나 구간에 실행되는 이벤트 |
| Module | `SectionModule` | Section의 일정 구간에 적용되는 기능 |

## 전체 구조도

```
[ Input ]
    │  (공용 PlayerInput → PlayerInputRouter → 활성 캐릭터)
    ▼
[ PlayerStateMachine ]        ← MonoBehaviour, 얇은 코디네이터(조립 + facade)
    │   · 협력 객체 조립: InputBuffer / HitTrigger / DodgeTrigger / ParryTrigger /
    │     Attack_Normal_EnhanceTrigger / ConfigRegistry / EnemySensor
    │   · 무적(Invulnerable)·패링(ParryActive)·들어오는 공격 윈도우 보유 + facade 노출
    │   ├── InputBuffer        입력 버퍼 (Normal / Dodge …)
    │   ├── HitTrigger         피격 트리거 (재진입 가드 + L/H 승격 + 패링 시 쳐냄 분기)
    │   ├── DodgeTrigger       회피 트리거 (push, 방향→섹션 선택)
    │   ├── ParryTrigger       패링 트리거 (push, 스탠스 단일 진입)
    │   ├── Attack_Normal_EnhanceTrigger  강화공격 (방향 우선 → 거리 분기 폴백)
    │   └── ConfigRegistry     섹션 이름으로 config 자동 검색 (FindWithSection)
    │   (테스트용 H/J/K 키는 PlayerTestTriggers 컴포넌트로 분리)
    │
    │  단일 ConfigState 보유 → Enter() / Update() 직접 호출
    ▼
[ ConfigState ]               ← 순수 C# 단일 러너 (상태 클래스 1개, 플레이어·몬스터 공용)
    │   · AnimationConfig(Clips + Links)를 파싱·구동
    │   · 걷기 / 콤보 / 강화공격 / 대시 / 회피 / 패링 / 피격을 전부 이 한 클래스로 표현
    │   · Link(전이) · Notify(이벤트) · Module(기능) · 워프/섹션턴 처리
    │
    ├── 클립 재생 ──────────────┐
    │                          ▼
    │              [ AnimatorBridge ]        ← Animator 접근 유일 창구 (파사드, 플레이어·몬스터 공용)
    │                          │  Play(클립명) = CrossFadeInFixedTime / additive 흔들림
    │                          ▼
    │              [ Unity Animator ]        ← 클립 재생만 (파라미터/Transition 미사용)
    │                          ▼
    │              [ Animation Clips ]       ← FBX에서 추출한 클립들
    │
    └── 이동/회전 ─────────────┐
                               ▼
                   [ PlayerController ]      ← 루트모션(위치=Bip001 / 회전=Root yaw) · 워프 · 부스트
```

> **상태 머신 프레임워크가 따로 없다.** `PlayerStateMachine`이 `ConfigState` 인스턴스 **하나**를
> 들고 직접 `Update()`를 돌린다. 상태 전환(콤보·대시·회피·피격 복귀)은 별도 State 클래스로
> 갈아끼우는 게 아니라, `ConfigState`가 **config를 갈아끼우는(SwitchConfig)** 방식으로 처리한다.

> **`ConfigState`는 플레이어 전용이 아니다 — 몬스터와 공유하는 엔진이다.** 구상 타입(`PlayerController`/
> `AnimatorBridge`/`PlayerStateMachine`)에 직접 의존하지 않고, [`ConfigDriving.cs`](../Assets/04.Scripts/Core/ConfigDriving.cs)의
> 인터페이스(`IConfigMover`/`IAnimatorBridge`/`IConfigSignals`)와 조건 컨텍스트(`ILinkConditionContext`)에만 의존한다.
> 이 둘(Mover·Animator)을 묶어 넘기는 `ConfigContext`는 **구상 클래스**(다형성은 그 필드 타입에만 있음).
> 플레이어·몬스터가 각자 `Awake`에서 `ConfigContext`를 채우고 조건 컨텍스트(`PlayerConditionContext`/`MonsterConditionContext`)를 주입해 같은 엔진을 구동한다.
> 생성자: `new ConfigState(ctx, signals, condCtx, homeConfig)`. 자세한 몬스터 쪽 사용은 [몬스터(공유 엔진 재사용)](#몬스터-공유-엔진-재사용) 참고.

---

## 전투 흐름 데이터 에셋 (AnimationConfig)

`AnimationConfig`는 "언리얼 엔진의 몽타주 스타일"의 ScriptableObject 트랙이다. 코드 수정 없이
에셋만 편집해서 콤보/피격/회피 연출을 구성한다.

> **용어** — 한 config는 **클립 구간** 목록으로 이루어진다(코드 타입 `TrackClip`).
> 이 문서에서 **"섹션"** 은 항상 이 클립 구간 하나를 가리키는 내부 용어다.
> **"Link"** 는 구간 사이의 **전이 정의**(`ClipLink`), **"Notify"** 는 재생 중 특정 시점에 발동하는
> **애니메이션 이벤트**(UE AnimNotify에서 딴 이름)다.

```
AnimationConfig
├── EntrySection            진입 시 재생할 섹션 (빈 값 = 첫 클립)
├── LoopTrack / DoneThreshold
├── Clips : List<TrackClip>     ← 클립 구간("섹션") 목록
│     ├── SectionName            섹션 식별자
│     ├── Clip / Speed
│     ├── MoveMode               None / RootMotion
│     ├── Links    : List<ClipLink>     ← 이 섹션에서 분기 가능한 전이
│     ├── Notifies : List<TrackNotify>  ← 재생 중 발동할 애니메이션 이벤트(이펙트/신호). 시점 또는 [NormalizedTime, EndNormalizedTime] 구간
│     └── Modules  : List<SectionModule>  ← 이동·회전·판정 등 섹션 기능(다형성)
└── GlobalLinks : List<ClipLink>   ← 모든 섹션에 적용 (Any State 전이)
```

### 전이 정의 (ClipLink)

| 필드 | 의미 |
|------|------|
| `TargetConfig` | 비면 현재 config 내 전이, 지정 시 그 config로 갈아끼움 |
| `TargetSection` | 대상 섹션 (비면 복귀 / EntrySection) |
| `Condition` | **무엇이 충족인지** — 다형성 `LinkCondition`(`[SerializeReference]`). null이면 항상 true(Always) 취급 |
| `Timing` | 언제 평가할지 — `WhenMatched` / `OnRelease` / `OnEnd` / `OnEndIfMatched` |
| `WindowStart` ~ `WindowEnd` | 평가 구간 (normalizedTime) |
| `BlendDuration` | 전이 시 CrossFade 시간(초) |
| `EntryOffset` | 전이 후 대상 섹션을 이 normalizedTime부터 재생 (중간 진입 — 윈드업 스킵 등) |

> 과거에는 조건을 `ClipLink`의 인라인 `Attack`/`Direction`/`RequireHeld` 필드로 들고 있었으나, 다형성 `Condition`(`InputCondition`)으로
> **이전·제거 완료**했다. 인라인 방식은 조건이 늘 때마다 `ClipLink`에 안 쓰는 필드가 쌓이는데,
> 향후 몬스터 AI 조건(거리·체력·BT 결정 등)까지 받으려면 다형성이 필요했다. 신규 링크는 `Condition`을 직접 설정한다.

### 전이 조건 (LinkCondition) — 다형성

`ClipLink`는 조건을 추상 타입 `LinkCondition`으로 들고, `ConfigState`는 타입을 가리지 않고 `Condition.Matches()`로 평가한다(다형성).
"언제 평가할지"(Timing·Window)는 링크가, "무엇이 충족인지"만 조건이 담당한다. 새 조건은 `LinkCondition` 상속 1개로 추가한다.

> `[SerializeReference]`를 쓰는 이유: 일반 직렬화는 선언 타입 기준이라 어떤 자식 조건을 넣었는지가 저장되지 않는다.
> `[SerializeReference]`가 실제 자식 타입과 값을 에셋에 그대로 저장/복원해 준다(`SectionModule`도 같은 이유로 사용).

평가에 필요한 질의는 `ILinkConditionContext`로 주입받는다 (플레이어=`PlayerConditionContext`, 몬스터=`MonsterConditionContext`).

| 구현 | 의미 |
|------|------|
| `InputCondition` | 기존 `Attack`(공격 입력) + `Direction`(방향, `Reverse` 포함) + `RequireHeld`를 흡수. `ReleaseTriggered`(OnRelease 발사)·`Consume`(입력 버퍼 소비)·`AcceptsInput`(폴백 게이트)를 구현 |
| `AlwaysCondition` | 무조건 true — 몬스터의 "끝나면 idle 복귀"(OnEnd) 같은 가드 없는 전이용 (플레이어 `Attack=None`='입력 없을 때'와는 의미가 다름) |
| `DistanceCondition` *(예정)* | 타깃과의 거리 조건 (근접→공격 / 원거리→접근). `IMonsterConditionContext.DistanceToTarget` 질의 |
| `HealthCondition` *(예정)* | 자기/타깃 체력 비율 조건 (저체력→광폭화 패턴 등) |
| `AIDecisionCondition` *(예정)* | BT/블랙보드가 내린 결정을 받는 조건 (예: `Decision == Attack`). BT는 결정만 쓰고, 전이 타이밍은 링크가 유지 |

> 위 셋은 아직 미구현 — `LinkCondition` 다형성과 `ILinkConditionContext` 주입 구조가 열어둔 확장 자리다.
> 몬스터 AI 도입 시 서브클래스 1개 + 컨텍스트 질의 추가로 붙고, `ConfigState` 평가 로직은 안 건드린다.

- **WhenMatched** : 윈도우 구간 안에서 조건 충족 즉시 (콤보 입력 / 방향 이동 / 복귀)
- **OnRelease** : 윈도우 안에서 Attack 키를 **뗀 순간** (홀드 차지 → 릴리스). 누름 버퍼가 아니라 실제 홀드 상태로 판정
- **OnEnd** : 클립이 끝나면 (조건은 가드로 작동, 루프 클립엔 무효)
- **OnEndIfMatched** : 윈도우 안에서 조건이 한 번이라도 충족되면 래치 → 섹션 끝에 발동 (카운터 예약 등)

> 평가 순서: 클립 고유 `Links` 먼저 → 그다음 `config.GlobalLinks`. 첫 발동 링크에서 전이하고 멈춘다.
> 실제 공격 입력(`Attack != None`)을 요구한 링크만 입력 버퍼를 소비한다.

---

## Animator Controller 구조 (파라미터리스)

```
Layer 0 (Base)
│   모든 클립을 State로 두고 ConfigState가 Play(클립명)으로 직접 재생
│   → Blend Tree / Trigger / Bool / Transition 파라미터 미사용
│
Layer 1 (Additive)
    └── Hit_Shake — 피격 흔들림을 additive로 덧씌움
        ConfigState의 Notify(EventName="OnHitShake")가 SendMessage로 호출
```

- 이동(Idle/Walk/Run)도 Blend Tree가 아니라 각 클립을 직접 `Play`로 전환한다.
- 따라서 Animator 파라미터는 **사용하지 않는다.** (재생 상태는 코드/데이터가 관리)
- `AnimatorBridge.Play()`는 `CrossFadeInFixedTime`(초 단위 고정 시간)을 쓴다 →
  config의 `BlendDuration`(초)과 단위가 일치. 함께 `ApplyAnimatorSpeed(Speed)`로
  애니 재생 속도를 로직 타임라인과 맞춘다(안 맞추면 전환 딜레이 발생).

### 왜 Blend Tree를 안 쓰고 클립을 직접 재생하나

이동(Idle/Walk/Run)조차 Blend Tree로 섞지 않고 각 클립을 `Play`(CrossFade)로 전환한다. 이유:

| 이유 | 설명 |
|------|------|
| **루트 이동량이 암묵적으로 섞임** | Animator는 Blend Tree의 루트 델타도 블렌딩하지만, 어떤 클립의 이동량이 최종값에 얼마나 반영되는지가 파라미터 가중치에 숨어 전투 거리 튜닝과 워프 결과를 예측하기 어려워진다 |
| **핵심 철학과 충돌** | 블렌딩 로직 + 파라미터(speed/direction)를 다시 AnimatorController로 밀어넣는다 → "분기/타이밍은 코드·데이터, Animator는 클립 재생만"이라는 전제를 깬다 |
| **섹션 단위 제어 불가** | MoveMode·SectionModule·Link·Notify는 전부 "특정 클립=섹션"에 붙는다. Blend Tree는 "어느 클립인지"를 추상화해 이 per-section 데이터를 붙일 곳이 없어진다 |

> 정리: 이 프로젝트의 이동은 **개별 섹션 클립의 루트모션**으로 성립하므로, Blend Tree의
> 암묵적 가중치보다 데이터에서 명시한 짧은 `CrossFade`를 사용한다.

---

## 타이밍 값은 normalizedTime으로 저장 — 에디터만 프레임으로 표시

모든 타이밍/구간 값(`WindowStart`~`WindowEnd`, `DoneThreshold`, `EntryOffset`,
`Notify.NormalizedTime`, `Notify.EndNormalizedTime`(구간 이펙트), 모듈 `Start`~`End`)은
`.asset`에 **normalizedTime(0~1)** 으로 저장된다. 반면 에디터 툴(`AnimationConfigTool`)은 이걸 **정수 프레임**으로 변환해 표시·편집한다.

```
저장(.asset)        normalizedTime (0~1)   ← 견고
에디터 입력/표시     정수 프레임 (예: 21/68f) ← 직관
런타임 평가          normalizedTime         ← 싸다
```

### 왜 데이터를 프레임이 아니라 normalizedTime으로 저장하나

> 프레임 숫자는 그 클립의 `frameRate`·`length`에 묶인 **절대값**이라, 클립이 조금만 바뀌면 가리키는 지점이
> 어긋난다. `0~1`은 "클립의 몇 % 지점"이라 클립이 바뀌어도 **상대 위치가 보존**된다.

| 장점 | 설명 |
|------|------|
| **클립 재임포트/교체에 강함** (가장 큼) | 클립을 다시 뽑아 프레임 수가 68→72로 바뀌거나 30→60fps로 바뀌어도, `0.3`은 그대로 같은 포즈 근처를 가리켜 윈도우 재작업이 거의 없다. 프레임 저장이면 "21프레임"이 다른 순간을 가리켜 전부 다시 잡아야 함 |
| **frameRate 독립** | 프레임 숫자는 fps를 알아야 의미가 생기지만, `0~1`은 그 자체로 완결 |
| **재생 속도(Speed) 무관** | 런타임이 `nt = _clipTime * Speed / length`로 평가 → Speed가 바뀌어도 `0~1` 윈도우는 그대로 동작 |
| **런타임 계산이 싸다** | 평가가 `p >= WindowStart && p <= WindowEnd` 한 줄. 매 프레임 `clip.frameRate`/`length`를 안 읽어도 됨 |
| **길이 다른 여러 클립에 공용** | `GlobalLinks`처럼 여러 클립에 걸치는 값은 클립마다 길이가 달라 프레임으론 불가. `0~1`이면 길이 무관 균일 적용 |

### 왜 에디터는 프레임으로 보여주나

`normalized`의 유일한 약점은 **사람이 손으로 잡기 비직관적**(`0.3088…`보다 "21프레임"이 명확)이라는 점.
그래서 **저장은 normalized(견고) + 입력은 프레임(직관)** 하이브리드로 둘 다 챙긴다.

- 변환 헬퍼 `ClipFrames` / `FrameWindowField` / `FrameField`
  ([AnimationConfigTool.Inspector.cs](../Assets/05.Editor/AnimationTool/AnimationConfigTool.Inspector.cs))가
  `normalized ↔ frame` 양방향 변환(분모 = `clip.length * frameRate`). 입력란은 정수 프레임이고 옆에 `/ 68f` 총 프레임 표시.
- 소속 클립이 없으면(예: `GlobalLinks`) 길이를 몰라 **normalized 슬라이더로 폴백**한다.
- 툴바도 현재 재생 위치를 `21/68f`로 표시
  ([AnimationConfigTool.Toolbar.cs](../Assets/05.Editor/AnimationTool/AnimationConfigTool.Toolbar.cs)).

### 트레이드오프

`normalized`는 "상대 위치"를 보존하지 정확한 "절대 프레임 수"는 보존하지 않는다. 예: **"클립 길이와 무관하게
정확히 8프레임 i-frame"** 같은 절대 프레임 시맨틱이 필요하면, 클립 길이가 바뀔 때 실제 프레임 수가 달라진다.
다만 여기서는 윈도우가 "특정 포즈 구간(캔슬·조준·무적)"에 묶이는 거라 **상대 위치 보존이 오히려 맞는** 선택이다.

---

## 이동 처리 (MoveMode)

클립마다 `MoveMode`로 이동 방식을 지정한다 ([AnimationConfig.cs](../Assets/04.Scripts/Core/AnimationConfig.cs)).

| MoveMode | 설명 |
|----------|------|
| `None` | 제자리 (루트모션 안 씀, 중력만 코드 적용) — Idle, 경직 |
| `RootMotion` | `Animator.deltaPosition/deltaRotation`을 적용 — 걷기/달리기/공격/대시/회피 |

> 걷기·달리기도 코드 이동이 아니라 **RootMotion**으로 처리한다.
> `ConfigState`는 섹션 진입 시 `Controller.UseCodeMovement = (MoveMode != RootMotion)`을 토글한다.
> 추가 이동, 회전 잠금, 타깃 워프·조준, 시작 부스트, 루트 회전 제어와 루트모션 보정은
> 모두 `SectionModule`이 소유한다.

### 루트모션 (`OnAnimatorMove`)

프리팹의 `Apply Root Motion`을 켜고, [PlayerController.cs](../Assets/04.Scripts/Player/PlayerController.cs)의
`OnAnimatorMove`가 `Animator.deltaPosition/deltaRotation`을 받아 직접 적용한다. 콜백이 존재하므로
Animator가 Transform을 자동 이동하지 않고, 컨트롤러가 최종 적용을 소유한다.

| 단계 | 처리 |
|------|------|
| 위치 입력 | `deltaPosition`의 수평 X·Z만 사용하고 Y는 버린다 |
| 위치 보정 | 후진 배율 → 타깃 워프 → 추가 이동/시작 부스트를 합친다 |
| 실제 이동 | 중력을 더한 뒤 `CharacterController.Move`를 프레임당 한 번 호출한다 |
| 골반 중복 이동 제거 | 이동 델타를 읽은 뒤 `Bip001` 로컬 X·Z를 0으로 덮고 Y·회전은 유지한다 |
| 회전 입력 | 일반 루트 회전은 `deltaRotation`, 섹션 턴은 `Root` 또는 `Bip001`의 프레임 회전 변화량을 사용한다 |
| 회전 소유권 | 루트 회전 제거 → 섹션 턴 → 타깃 조준 → 일반 루트 회전/입력 회전 순으로 결정한다 |

`MoveMode.None`에서는 Animator 루트 델타를 버리므로 제자리 클립이 실수로 이동하지 않는다.
`SmoothLoopSpeedModule`과 Bip001 기반 이동 추출/회전 카운터는 제거했고, 루프와 CrossFade 델타는 Animator 평가값을 신뢰한다.

---

## 공격 보정 — 타겟 워프 & 턴 회전 추출

애니 원본만으로는 적을 정확히 못 때리므로, 루트모션 위에 두 가지 보정을 얹는다.

### 타겟 워프 & 조준 — `TargetWarpModule`과 `FaceTargetModule`은 독립
RootMotion 섹션 진입 시 각 모듈이 전방 적(`EnemySensor.FindTarget()`)을 찾아 적용한다.
적이 없으면 둘 다 무동작 → 원본 모션 그대로 (적 유무 분기 불필요).

**이동 워프 (`TargetWarpModule`)** — 루트모션 수평 이동을 적 방향으로 재조준 (회전과 무관, 이동만).
- `StopDistance`로 타겟 앞에서 멈춤(관통 방지)
- 모듈 `Start~End` 구간에서만 워프 작동 → 타격 이후엔 끊어 적을 따라 휙 도는 것 방지
- 콤보 단마다 재탐색 → 적이 옆으로 빠져도 다음 타가 따라간다

**타겟 조준 (`FaceTargetModule`)** — 모듈 윈도우 동안 타겟을 향해 회전한다.
- `Start=End=0`이면 진입 1회 스냅 / 넓히면 그 구간 내내 락온
- `TurnSpeed` 0 = 즉시, >0 = 각속도 제한 회전
- `FaceInputModule`이 함께 있으면 입력 방향 조준이 진입 스냅보다 우선

### 턴 회전 (`SectionTurnModule`) — 턴 애니로 캐릭터를 실제 회전

턴 애니(예: 180° 뒤돌기, TurnBack)에서 캐릭터를 실제로 회전시킨다. 회전을 `transform`에 적용해야
턴 이후 이동/다음 섹션(run_loop)이 새 방향으로 이어진다. 모듈이 있는 섹션에서만 작동한다.

회전은 Animator가 최종 본 포즈를 계산한 뒤 `Root`와 `Bip001`의
`현재 로컬 회전 * inverse(이전 로컬 회전)`에서 선택 축 twist를 추출한다. 먼저 유효한 델타가
나오는 본을 그 섹션의 회전 소스로 고정하므로, 클립에 따라 회전 곡선이 `Root` 또는 `Bip001`에
들어 있어도 같은 경로로 처리한다.

**메커니즘** ([PlayerController.cs](../Assets/04.Scripts/Player/PlayerController.cs) `LateUpdate`)

| 단계 | 처리 |
|------|------|
| 트리거 | `MoveDir.Reverse`(진행 반대키, `dot(forward,입력)<-0.707`) → 턴 섹션 전이 (`ConfigState.IsReverseInput`) |
| 회전 입력 | 첫 유효 델타가 검출된 `Root` 또는 `Bip001`의 선택 축 프레임 델타 |
| 원본 축 | 상대 회전의 X/Y/Z 회전량을 월드 yaw로 매핑하며 `Auto`는 프레임별 주축을 자동 선택한다 |
| 구간 | `Start~End` 안에서만 적용하고 밖에서는 버린다 |
| 배율 | `RotationScale`을 yaw 델타에 적용한다 |
| 상한/종료 | 적용 중에는 `TargetAngle`로 누적량을 제한하고, 윈도우 종료 시 검출된 방향의 정확한 목표 각도로 마무리한다 |
| 월드 회전 | 섹션 진입 때 저장한 최상위 캐릭터 회전을 기준으로 누적 yaw를 적용한다 |
| 외형 역보정 | 최상위에 넘긴 누적 yaw만큼 `Bip001`을 월드 축에서 반대로 돌려 모델의 이중 회전을 막는다 |
| 재진입 | `FlushRootRotation`으로 기준 회전, 이전 본 회전, 소스 선택과 누적 각도를 초기화한다 |

Burnice TurnBack은 `SourceAxis=Y`, `RotationScale=1`, `TargetAngle=180`을 사용한다. 이 클립은
`Animator.deltaRotation`이나 `Root`에서 유효한 회전이 나오지 않을 수 있어 런타임이 `Bip001`을
회전 소스로 선택한다. TurnBack에서 `Run_Loop`으로 갈 때는 추가 CrossFade 회전이 섞이지 않도록
링크의 `BlendDuration`을 0으로 둔다.
`RotationLockModule`은 입력 회전만 막으므로 섹션 턴과 함께
사용해도 섹션 턴 회전은 정상 적용된다.

---

## 구간별 로직 모듈 (SectionModule) — 기능을 끼워 넣는 플러그인

**기능** — 클립 구간(섹션) 하나에 붙는 기능 단위. `TrackClip.Modules`에 `[SerializeReference]`로 **다형성 직렬화**된다.
새 연출/판정 기능 = 베이스 상속 1개 추가 (ConfigState 본체는 안 건드림).

**동작** — `ConfigState`가 섹션 진입 시 `OnEnter`, 매 프레임 `Tick`을 호출한다.
구간(Start~End) 동안 무언가를 켜는 패턴이 반복돼서, 구간 판정을 `WindowModule` 베이스로 한 번만 짜고
"무엇을 켜느냐"만 자식이 정의한다.

```
SectionModule (추상)           OnEnter(1회) / Tick(매 프레임)
├── FaceInputModule / StartBoostModule / BackMotionScaleModule
└── WindowModule (추상)        [Start, End] normalizedTime 구간 판정(InWindow) 공유 — 루프 wrap 처리 포함
    ├── AdditionalMovementModule / RotationLockModule
    ├── TargetWarpModule / FaceTargetModule / SectionTurnModule
    ├── IFrameModule           구간 동안 Machine.Invulnerable = true   → 피격 '무시'
    └── ParryModule            구간 동안 Machine.ParryActive  = true   → 피격을 '쳐냄'으로 분기
```

`ConfigState`는 섹션 진입 시 이동기와 전투 플래그를 기본값으로 리셋하고, 모듈이 필요한 기능만 다시 켠다.
`HitTrigger`는 `Invulnerable`이면 피격을 무시(회피 i-frame), `ParryActive`면 쳐냄으로 분기(패링).

- **장점** — 무적·패링처럼 "구간 동안 플래그를 켜는" 판정을 **클래스 1개 + 구간 두 값**으로 추가한다. 툴의 추가 메뉴/구간 편집 UI가 베이스 덕에 자동으로 잡힌다. i-frame('무시')과 parry('응수')가 대칭이라 읽기 쉽다.
- **단점** — `[SerializeReference]` 다형성은 타입 리네임/이동 시 직렬화가 깨질 수 있고, 모듈 간 실행 순서·상호작용이 늘면 암묵적 의존이 생긴다(현재는 플래그 토글뿐이라 단순).

---

## 회피 (Dodge / Evade)

회피는 "어떤 config에서든" 입력 즉시 강제 진입하는 push 방식이다.
([DodgeTrigger](../Assets/04.Scripts/Player/StateMachine/Triggers/DodgeTrigger.cs))

```
Dodge 입력 버퍼됨
    │  (링크 평가 전에 검사 → 콤보보다 우선, 공격 중 캔슬 가능)
    ▼
DodgeTrigger.Suffix()  — 입력 상태로 섹션 방향 결정
    ├── 무입력/아래            → Evade_Back   (회전 없이 백스텝)
    ├── 방향(W/A/D) 일반        → Evade_Front  (FaceInputModule로 입력 방향 회전)
    └── 방향 + 퍼펙트 윈도우    → Evade_Left / Evade_Right  (좌우 회피)
    │
    ▼
ConfigRegistry.FindWithSection("Evade_" + suffix)  — 섹션 이름 규약으로 config 검색
    │  · 재진입 가드: 이미 회피 중 + 진행도 < _dodgeReinterrupt 면 무시 (연타 프리즈 방지)
    ▼
ConfigState.InterruptWith(cfg, section, _dodgeBlend)
    └── IFrameModule이 회피 시작~중반 무적 부여 → 이 사이 피격은 무시(회피 성공)
```

### 퍼펙트 회피 윈도우
적이 "공격 적중 직전" `OpenIncomingAttack(window)`로 창을 열어두면, 그 사이 회피 = 퍼펙트(좌/우 모션).
현재는 `PlayerTestTriggers`의 `K` 키로 적 공격을 시뮬레이션한다(윈도우를 열고 끝에 `TriggerHit` 적중 — i-frame 중이면 자동 무시).
실제 적 공격 시스템이 생기면 공격 액티브 직전에 `OpenIncomingAttack`를 호출하면 된다.

### 문제와 해결

- **회피 도중엔 맞지 않아야 함** → `IFrameModule`로 회피 시작~중반 구간에 무적(i-frame) 부여, 이 사이 피격은 무시
- **연타로 회피 재입력 시 프리즈** → 이미 회피 중 + 진행도 < `_dodgeReinterrupt`면 새 회피 무시 (재진입 가드)
- **회피 전이 중 캐릭터가 튀는 문제(전방 회피→달리기 튐 / 후방 회피 연타 워프)** → 전이 구간 루트모션 델타는 두 클립 블렌딩 아티팩트라 신뢰 불가 → 전이 중 수평 이동을 통째로 버려 해결 ([루트모션](#루트모션-직접-구현) "스냅" 항목 참조)

---

## 패링 (Parry) — i-frame과 대칭인 '응수' 방어

**왜 만들었나** — 회피(i-frame)는 공격을 *무시*만 한다. 패링은 같은 타이밍 방어지만 적 공격을
*쳐내고 반격으로 응수*하는, 더 공격적인 선택지를 주려고 만들었다. 둘을 **대칭 구조**로 설계해
판정 코드를 거의 공유한다(무적=`Invulnerable`, 패링=`ParryActive`).

```
패링 입력 (push, 어느 config에 있든)
    │
    ▼
ParryTrigger.Trigger()  — 스탠스 섹션(Attack_ParryAid_Start)으로 강제 진입 (방향 분기 없음)
    │  · 재진입 가드: 같은 스탠스 중 + 진행도 < _reinterrupt 면 무시
    ▼
ParryModule (윈도우)  — 활성 구간 동안 Machine.ParryActive = true
    │
    ▼  (이 사이 적 공격이 닿으면)
HitTrigger.Trigger()
    ├── Invulnerable 이면 → 피격 무시 (회피 i-frame)
    ├── ParryActive 이고 TryDeflect() 성공 → 쳐냄(ParryAid_L/H) 진입  ← 패링 성공
    └── 둘 다 아니면 → 일반 피격 (Hit_L/H_Front/Back)
            │
            ▼
        쳐냄 ParryAid_L/H config의 Link(Attack=Normal → Counter)가 카운터 follow-up 처리
```

- **동작 요점** — 적 공격 강도(`IncomingStrength`)로 쳐냄 섹션 L/H를 고른다. 쳐냄 섹션 config가 없으면 `TryDeflect()`가 false를 반환해 **일반 피격으로 안전하게 폴백**한다(패링 모션 미제작 상태에서도 안 깨짐).
- **장점** — 회피/피격과 판정 경로(`HitTrigger`)·진입 방식(push)·구간 모듈(`WindowModule`)을 **공유**해 코드 추가가 작다. 데이터(섹션 이름 규약 `Attack_ParryAid_*`)만으로 쳐냄·카운터를 잇는다.
- **단점** — 현재 들어오는 공격은 테스트키(`K`)로 시뮬레이션이라, 실제 적 AI가 `OpenIncomingAttack(window, strength)`를 공격 액티브 직전에 호출하도록 배선해야 완성된다. 접두어 문자열 규약(`Attack_ParryAid_`)을 `ParryTrigger`/`HitTrigger`가 공유하므로 한 곳(`ParryTrigger.Prefix`)에서만 정의해 주입한다.

---

## 피격 (외부 이벤트 진입)

```
충돌 검출 → PlayerStateMachine.TriggerHitFrom → HitTrigger.TriggerFrom(attackerPos)
    │  공격자 위치로 Front/Back 판정 (또는 PlayerTestTriggers의 H=Back / J=Front)
    ▼
HitTrigger.Trigger(direction)
    ├── Invulnerable(회피 i-frame)이면 무시
    ├── ConfigRegistry.FindWithSection("Hit_L_{dir}" ?? "Hit_H_{dir}") 로 hit config 검색
    ├── 재진입 가드 : 진행도 < _hitReinterruptThreshold 면 새 피격 무시 (연타 stunlock 방지)
    └── escalation  : 연속타 카운트로 강도 교대 (홀수타=L / 짝수타=H)
    ▼
ConfigState.InterruptWith(hitConfig, "Hit_{L|H}_{Front|Back}", _hitEntryBlend)
    ├── layer 0     : Hit 반응 클립 재생
    └── layer 1     : Notify(OnHitShake) → additive 흔들림 + weight 페이드
            │
            ▼
        Hit config의 OnEnd Link → home(걷기)로 복귀
```

### 문제와 해결

- **피격 흔들림을 반응 모션 "위에" 겹치기 (additive 레이어)** → 흔들림 클립으로 반응을 *덮어쓰면* 방향별 피격 포즈(Front/Back·L/H)가 사라진다. 그래서 별도 **additive 레이어(layer 1)** 에 `Hit_Shake`를 올려 base 반응(layer 0) **위에 더해** 재생 → 반응 포즈는 그대로 둔 채 흔들림만 얹힌다. 흔들림은 클립 길이만큼 유지한 뒤 레이어 weight를 0으로 페이드해 깔끔히 사라진다 (`AnimatorBridge.ShakeRoutine`).
  - **데이터 ↔ 코드 분리** : config는 `Notify(EventName="OnHitShake")`로 "여기서 흔들림" 신호만 보내고, 실제 레이어/weight 제어는 `AnimatorBridge`가 담당 (SendMessage로 느슨하게 결합) → 흔들림 타이밍을 코드 수정 없이 에셋에서 조절 *(→ SendMessage는 캐릭터별 강타입 이벤트 릴레이로 교체 예정, [TODO.md](TODO.md))*
- **같은 약한 움찔거림만 반복돼 단조로움** → escalation: 연속타 카운트로 약(L) ↔ 강(H) 반응을 교대 재생
- **연타 stunlock(보조 가드)** → 반응이 충분히 진행되기 전(`_hitReinterruptThreshold`)엔 새 피격을 무시

---

## 입력 버퍼 & 콤보

```
PlayerInputRouter
    ├── Attack → 활성 캐릭터.BufferInput(ComboInput.Normal/Strong)
    └── Dodge  → 활성 캐릭터.BufferInput(ComboInput.Dodge)
            │  버퍼에만 저장 (_inputBufferWindow = 0.25s)
            ▼
        Update()
            ├── 버퍼가 Dodge면 → TriggerDodge() (콤보보다 우선)
            └── ConfigState.Update()
                    ├── 현재 섹션 Link의 Condition(InputCondition: Attack/Direction) + Timing 평가
                    ├── HasBufferedInput && BufferedInput == Condition.Attack 이면 조건 충족
                    └── 발동 시 ConsumeInput() 후 TargetConfig/Section으로 전이
```

> `ComboInput` = `None / Any / Normal / Strong / Enhance / Dodge / Parry` (`None`·`Any`는 특수 토큰, `Normal`=좌클릭 탭 / `Strong`=좌클릭 홀드 강공 / `Enhance`=E 강화공격).
> 입력은 누름 버퍼(0.25s)와 별개로 **홀드 상태(`IsHeld`)** 도 추적한다 — `OnRelease`/`RequireHeld`가 이 홀드 상태를 본다 (홀드 차지 → 릴리스). 강화공격(`Enhance`) 입력 액션은 떼는 콜백을 받으려고 **PassThrough** 타입이다.

### 문제와 해결

- **선입력은 받되 아무 때나 콤보가 나가면 안 됨** → 입력을 0.25초(`_inputBufferWindow`) 버퍼에 저장해두고, Link의 `Timing`/`Window`로 "콤보를 이어갈 수 있는 구간"에서만 다음 동작이 발동하도록 데이터로 제어
- **회피와 콤보 입력이 충돌** → 회피(Dodge)는 링크 평가보다 먼저 검사해 콤보보다 우선 (공격 중에도 회피로 캔슬 가능)

---

## 강화공격 (Attack_Normal_Enhance) — 상황으로 진입 모션을 고른다

**기능** — 같은 강화공격 입력(E)이라도 **상황에 맞는 다른 진입 모션**으로 들어간다
(앞으로 누르면 돌진, 멀면 거리 좁히는 대시 등).

**동작** — 두 경로가 있다.
1. **콤보 중** — 각 공격 섹션의 `Attack_Normal_Enhance` Link가 윈도우 안에서 입력을 먼저 소비(우선).
2. **걷기/Idle 등 링크가 없는 상태** — `PlayerStateMachine`이 `ConfigState.Update`(콤보 링크 평가) 후
   입력이 남아 있으면 `Attack_Normal_EnhanceTrigger`(전역 폴백)를 호출.

폴백 트리거의 섹션 선택 — **방향 우선 → 중립이면 거리** ([Attack_Normal_EnhanceTrigger.cs](../Assets/04.Scripts/Player/StateMachine/Triggers/Attack_Normal_EnhanceTrigger.cs)):

| 입력 상황 | 진입 섹션 |
|-----------|-----------|
| 이동 Forward(W) | `..._Front_01` (전진 돌진) |
| 이동 Back(S) | `..._Back_01` (백스텝 카운터) |
| 그 외(중립·좌·우) | `EnemySensor`로 잰 전방 적 거리 → 근(`_01`) / 중(`_02`) / 원(`_03`) |

- **폴백 안전망** — 방향/거리 전용 섹션이 아직 config에 없으면 한 단계 가까운 쪽으로 자동 폴백(원→중→근). 모션을 다 안 만든 상태에서도 안 깨진다.
- **장점** — "어떤 상황에 어떤 모션"을 코드가 아니라 섹션 이름 규약 + 거리 임계값(인스펙터)으로 조절. 적과의 거리는 워프에 쓰는 `EnemySensor.FindTarget(out dist)`를 **재사용**(탐색 1회).
- **단점** — 진입 분기 규칙이 트리거 코드에 들어가 있어, 완전히 데이터만으로 정의되진 않는다(섹션 이름 컨벤션 의존).

---

## 트리거·config 인스펙터 연동

이벤트 진입 config(Hit, Evade 등)는 `PlayerStateMachine._configs`에 드롭만 하면 **섹션 이름으로 자동 검색**(`FindConfigWithSection`)해 진입한다 — 코드 수정 불필요(링크로 도달하는 콤보 config는 `TargetConfig` 참조라 리스트에 넣을 필요 없음).

> **트리거 설정도 인스펙터에서** — `Hit/Dodge/Parry/Attack_Normal_Enhance` 트리거는 `[Serializable]` 객체라
> 각자 설정(섹션 이름·blend·거리 임계 등)을 직접 들고, `PlayerStateMachine` 인스펙터에 폴드로 노출된다.
> 런타임 의존(상태/레지스트리/입력)만 `Init()`으로 주입한다. (평평하던 머신 필드 정리)

---

## 몬스터 (공유 엔진 재사용)

**왜** — 피격 반응·복귀 같은 흐름은 플레이어와 똑같이 "config를 읽어 클립을 틀고 OnEnd로 복귀"하는 구조다.
그래서 몬스터용 상태머신을 새로 짜지 않고 **같은 `ConfigState` 엔진을 재사용**한다. 그러려면 `ConfigState`가
플레이어 구상 타입에 묶이면 안 되므로, 의존을 [`ConfigDriving.cs`](../Assets/04.Scripts/Core/ConfigDriving.cs)의
인터페이스로 추출했다(`IConfigMover`/`IAnimatorBridge`/`IConfigSignals` + 조건 `ILinkConditionContext`).
이 표면들을 묶어 넘기는 `ConfigContext`는 구상 클래스다(번들 자체는 다형성이 필요 없어 인터페이스로 두지 않음).

**구성** ([Assets/04.Scripts/Monster/](../Assets/04.Scripts/Monster/))

| 부품 | 구현 인터페이스 | 역할 |
|------|----------------|------|
| `MonsterStateMachine` | `IConfigSignals` · `ILiveMonitor` | 입력 없는 코디네이터. `Awake`에서 `new ConfigState(...)` 조립, `Start`에서 `Enter()`, 매 프레임 `Update()`. `HitTarget.OnDamaged` 구독 → 앞/뒤 판정 후 Hit config로 `InterruptWith` |
| `MonsterController` | `IConfigMover` | v1(Idle+Hit, 제자리)은 `ConfigState`가 세팅하는 값들을 **보관만**(no-op). `FaceToward`만 즉시 회전으로 구현. 루트모션/넉백/추격은 후속 |
| `ConfigContext` (공용) | — (구상 클래스) | 몬스터 컴포넌트(Mover/Animator/Transform/GO)를 묶어 `ConfigState`에 주입. `Awake`에서 직접 채움 — 전용 컨텍스트 클래스 없음 |
| `MonsterConditionContext` | `ILinkConditionContext` | 입력 개념이 없어 질의가 전부 빈 값. Idle+Hit는 입력 조건이 없어 `Always`/`None`이 자동 통과하고 OnEnd로 복귀 |

- **AnimatorBridge·HitTarget 그대로 재사용** — `MonsterStateMachine`은 `IAnimatorBridge`로 같은 `AnimatorBridge`를 받는다(흔들림 State 이름만 인스펙터에서 Durahan용으로 설정). 입력이 없으므로 `IInputMonitor`는 구현하지 않아 라이브 모니터가 입력 행을 자동 생략한다.
- **경직(poise) A안 — 히트 쿨다운** : 인터럽트 직후 `_hitStunCooldown`(기본 0.3s) 동안은 Hit 모션을 재시작하지 않는다(무한 경직 락 방지). 데미지(HP)는 매 히트 적용되고 '모션 리셋'만 throttle. 후속으로 C안(Hit config 구간별 슈퍼아머 윈도우) 확장 가능.
- **한계(현 스캐폴드)** — 실제 이동/AI가 없다(제자리 Idle+Hit). 거리/체력 기반 AI 조건이 생기면 `ILinkConditionContext`를 확장하고 몬스터 컨텍스트가 채우면 된다(플레이어 쪽은 새 멤버를 기본값으로). `ConfigState`는 현재 `ZZZ.Player.StateMachine.States`에 있으나 공유 엔진이라 추후 중립 네임스페이스로 이전 가능.

---

## 파일 구조

```
Assets/04.Scripts/
├── Core/                            공유 코어 (플레이어·몬스터 엔진)
│   ├── AnimationConfig.cs           ScriptableObject + TrackClip/ClipLink/Notify/enum 정의
│   ├── ConfigDriving.cs             ConfigState가 의존하는 공유 인터페이스 (ConfigContext/Mover/AnimatorBridge/Signals + ILiveMonitor/IInputMonitor)
│   └── LinkCondition.cs             다형성 전이 조건 베이스 + InputCondition/AlwaysCondition + ILinkConditionContext
│
├── Combat/
│   ├── EnemySensor.cs               전방 부채꼴 적 탐지 (워프 타겟 / 거리 분기)
│   ├── HitTarget.cs                 피격 대상(허수아비/몬스터) — HP 보유, OnDamaged 이벤트
│   ├── IHittable.cs                 피격 가능 인터페이스
│   ├── MeleeHitter.cs               OnAttackHit Notify → EnemySensor 범위 안 타격 (센서 기반)
│   └── EffectHitVolume.cs           공격 이펙트 프리팹에 부착 → 스폰 시 자기 범위(SphereCollider) 타격 (이펙트 범위 기반)
│
├── Movement/
│   └── RootMotionTracker.cs         에디터 RootT 프리뷰용 프레임 델타 헬퍼
│
├── Monster/                         몬스터 — 같은 ConfigState 사용 (Idle+Hit)
│   ├── MonsterStateMachine.cs       입력 없는 코디네이터 (IConfigSignals/ILiveMonitor) — 피격 시 Hit config 인터럽트
│   ├── MonsterController.cs         IConfigMover 구현 — v1은 제자리 재생(보관만), FaceToward만 실제 회전
│   └── MonsterConditionContext.cs   ILinkConditionContext 구현 — 입력 없음(전부 빈 값)
│
└── Player/
    ├── PlayerController.cs           OnAnimatorMove 루트모션·워프·회전 소유권·CharacterController 이동
    ├── PlayerInputRouter.cs          공용 PlayerInput 콜백 → 활성 캐릭터 입력 인터페이스
    ├── SquadController.cs            캐릭터 생성·교체 및 입력/카메라 타깃 전환
    ├── PlayableCharacter.cs          캐릭터 프리팹의 상태 머신·CameraPoint 파사드
    ├── PlayerResources.cs            플레이어 자원(스태미나 등)
    ├── PlayerStateHUD.cs             현재 config/섹션/입력 디버그 HUD
    ├── TPSCameraController.cs        커스텀 TPS 카메라
    │
    └── StateMachine/
        ├── PlayerStateMachine.cs     코디네이터 — 입력버퍼·트리거·config 검색 조립 + facade
        ├── PlayerStateContext.cs     상태 공유 데이터 (Controller/Animator/CC/Transform)
        ├── PlayerConditionContext.cs 플레이어 입력/방향을 LinkCondition에 공급 (ILinkConditionContext)
        ├── AnimatorBridge.cs         Animator 파사드 (Play + additive Hit_Shake) — 플레이어·몬스터 공용
        ├── PlayerTestTriggers.cs     테스트 입력(H/J/K) 분리 컴포넌트
        ├── ConfigRegistry.cs         섹션 이름으로 config 검색 (FindWithSection)
        ├── InputBuffer.cs            선입력 버퍼
        │
        ├── States/
        │   └── ConfigState.cs        config 실행기 (플레이어·몬스터 공용)
        │
        ├── Triggers/                 외부/전역 진입 (push)
        │   ├── HitTrigger.cs             피격 + 패링 쳐냄 분기
        │   ├── DodgeTrigger.cs           회피 (방향→섹션)
        │   ├── ParryTrigger.cs           패링 스탠스 진입
        │   └── Attack_Normal_EnhanceTrigger.cs  강화공격 (방향/거리 분기)
        │
        └── Modules/                  섹션 플러그인 (다형성)
            ├── SectionModule.cs          추상 베이스 (OnEnter/Tick)
            ├── WindowModule.cs           구간(Start~End) 판정 베이스
            ├── SectionContext.cs         모듈이 받는 런타임 핸들 묶음
            ├── AdditionalMovementModule.cs 추가 이동 거리/방향
            ├── RotationLockModule.cs     회전 잠금
            ├── FaceInputModule.cs        진입 시 입력 방향 조준
            ├── FaceTargetModule.cs       타깃 조준
            ├── TargetWarpModule.cs       타깃 방향 이동 워프
            ├── StartBoostModule.cs       섹션 시작 이동 보강
            ├── SectionTurnModule.cs      Root/Bip001 회전 구간·배율·목표 각도
            ├── RootMotionTuningModules.cs 후진 루트모션 배율
            ├── IFrameModule.cs           무적 구간(i-frame)
            └── ParryModule.cs            패링 활성 구간
```

### 플레이어 런타임과 캐릭터 교체

`PlayerRuntime`은 `PlayerInput`, `PlayerInputRouter`, `SquadController`를 한 번만 소유한다.
`SquadController`는 등록된 `PlayableCharacter` 프리팹을 미리 생성하고, 활성 캐릭터 하나에만
입력을 전달한다. 교체 시 이전 캐릭터의 월드 위치만 다음 캐릭터에 넘긴 뒤
`PlayerStateMachine`과 `TPSCameraController`의 타깃을 함께 변경한다.

비활성 캐릭터는 `ConfigState.Exit()`로 실행 중인 이펙트와 상태 플래그를 정리한 다음 꺼진다.
일반 교체는 한 캐릭터만 활성화하며, 두 캐릭터가 겹쳐 재생되는 Assist 연출은 별도 교체 모드로
확장한다.

구성 방법과 교체 생명주기는 [PlayerRuntimeArchitecture.md](PlayerRuntimeArchitecture.md)를 참고한다.

### Burnice 애니메이션 리소스 교체

Burnice의 루트모션 보정 클립은 기존 경로와 파일명을 유지한 채 교체했다. 현재 기준 리소스는
`Assets/01.Characters/Burnice/Animations/Anim`의 146개 `.anim`, 같은 폴더 상위의 Animator
Controller, `Prefabs/Avatar_Female_Size02_Burnice.prefab`이다. 리소스 자체가 바뀌어 `.meta` GUID도
함께 바뀌었으므로 `AnimationConfig`, Animator Controller와 `SampleScene` 참조를 한 변경 단위로
관리해야 한다.

교체 범위, GUID 확인 항목과 재검증 절차는
[BurniceAnimationResourceMigration.md](BurniceAnimationResourceMigration.md)에 기록한다.


---

## 에디터 툴 연동

`Assets/05.Editor/AnimationTool/AnimationConfigTool.cs`가 `AnimationConfig`를 시각 편집한다.

- 타임라인에서 클립 배치·Link(베지어 연결선)·Notify·Module 편집
- **Module 추가** : 등록된 `SectionModule` 타입을 드롭다운으로 자동 나열 — 새 모듈 = 클래스 1개 추가 → 메뉴 자동 등장
- **Module Lane** : 클립 행의 `M n` 버튼으로 접기/펼치기. 접으면 컬러 구간 요약,
  펼치면 모듈별 행에서 `WindowModule` 구간과 진입·전체 섹션 모듈을 분리해 표시.
  `WindowModule`의 양 끝 핸들은 타임라인에서 직접 드래그할 수 있으며 클립 프레임에 스냅된다.
- **Combo 프리뷰** : 공격 입력은 단일 드롭다운으로 '눌러둠(held)' 선택 → Link 흐름을 그대로 재생 (CrossFade 블렌딩·루트모션 시뮬레이션)
- **라이브 모니터** : 플레이 중 `PlayerStateMachine`을 추적해 현재 config/섹션/입력 버퍼/**Held**(눌린 키)를 실시간 표시
  (`CurrentConfig`/`CurrentSection`/`CurrentNormalizedTime`/`CurrentMoveDir`/`IsInputHeld` 등을 노출)

---
