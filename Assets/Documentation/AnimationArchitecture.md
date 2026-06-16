# 플레이어 애니메이션 아키텍처

## 핵심 철학

> **분기는 C# State Machine이, "무엇을 언제 재생할지"는 데이터(Config)가, Animator는 "클립 재생"만 담당**

Animator Controller에 복잡한 Transition/파라미터를 쌓지 않는다.
- 분기 로직은 C# State Machine에서 처리하고,
- 콤보/피격 같은 "전이 흐름"은 `AnimationConfig`(ScriptableObject) **데이터**로 정의하며,
- Animator는 `CrossFade(클립명)`으로 클립을 재생하는 역할만 한다.

그 결과 Animator Controller에는 Trigger/Bool/Transition 화살표가 거의 없다.

---

## 전체 구조도

```
[ Input ]
    │  (PlayerInput SendMessages → 입력 버퍼링)
    ▼
[ PlayerStateMachine ]        ← MonoBehaviour, 루트 진입점 + 입력 버퍼 + 피격 트리거
    │
    │  ChangeState<T>()
    ▼
[ StateMachine ]              ← 순수 C# 상태 관리자 (Core/)
    │
    │  Enter / Update / Exit
    ▼
[ StateBase ]                 ← 각 상태의 추상 베이스
    │
    ├── ConfigState           → AnimationConfig를 파싱·구동하는 "범용 상태"
    │     · 걷기 / 콤보 / 대시 / 피격 등 대부분을 이 한 클래스로 표현
    │     · Link(전이)·Notify(이벤트)·Section 처리
    │     · Animator.CrossFade(클립명) 호출
    │
    └── (레거시 하드코딩 State — config로 미이관)
          ├── EnhanceComboState   → PlayEnhanceAttack(1~3) 콤보
          ├── RushState           → PlayRush()
          └── SpecialState        → PlaySpecial()
           │
           ▼
    [ PlayerAnimatorBridge ]  ← Animator 접근 유일한 창구 (파사드)
           │
           │  Animator.CrossFade(stateName) / 레이어/가중치 제어
           ▼
    [ Unity Animator ]        ← 클립 재생만 담당 (파라미터/Transition 미사용)
           │
           ▼
    [ Animation Clips ]       ← FBX에서 추출한 클립들
```

> 현재 흐름은 `PlayerStateMachine`이 `ConfigState`로 시작하고, 콤보·대시·특수기·피격은
> 대부분 **config의 Link(`TargetConfig`/`TargetSection`)** 로 처리된다.
> `EnhanceComboState`/`RushState`/`SpecialState`는 이전 하드코딩 방식의 잔존물로,
> 점진적으로 config 기반으로 흡수되는 중이다.

---

## AnimationConfig — 데이터로 정의하는 전이 트랙

`AnimationConfig`는 "몽타주 스타일"의 ScriptableObject 트랙이다. 코드 수정 없이
에셋만 편집해서 콤보/피격 연출을 구성한다.

```
AnimationConfig
├── EntrySection            진입 시 재생할 섹션 (빈 값 = 첫 클립)
├── LoopTrack / DoneThreshold / ComboResetTime
├── Clips : List<TrackClip>     ← 섹션(클립) 목록
│     ├── SectionName            섹션 식별자
│     ├── Clip / Speed
│     ├── MoveMode               None / Planar / RootMotion
│     ├── LockRotation / StartBoost
│     ├── Links   : List<ClipLink>     ← 이 섹션에서 분기 가능한 전이
│     └── Notifies: List<TrackNotify>  ← 재생 중 발동할 이벤트/이펙트
└── GlobalLinks : List<ClipLink>   ← 모든 섹션에 적용 (Any State 전이)
```

### ClipLink (전이 정의)

| 필드 | 의미 |
|------|------|
| `TargetConfig` | 비면 현재 config 내 전이, 지정 시 그 config로 갈아끼움 |
| `TargetSection` | 대상 섹션 (비면 복귀 / EntrySection) |
| `Attack` + `Direction` | 발동 조건 (공격 입력 + 방향 입력, **AND**) |
| `Timing` | 언제 평가할지 — `WhenMatched` / `OnWindowMiss` / `OnEnd` |
| `WindowStart` ~ `WindowEnd` | 평가 구간 (normalizedTime) |
| `BlendDuration` | 전이 시 CrossFade 시간 |

- **WhenMatched** : 윈도우 구간 안에서 조건 충족 즉시 (콤보 입력 / 방향 이동 / 복귀)
- **OnWindowMiss** : 윈도우 끝까지 조건이 안 맞고 지나가면 (콤보 캔슬 / 타임아웃)
- **OnEnd** : 클립이 끝나면 (조건은 가드로 작동, 루프 클립엔 무효)

---

## Animator Controller 구조 (파라미터리스)

```
Layer 0 (Base)
│   모든 클립을 State로 두고 ConfigState가 CrossFade(클립명)으로 직접 재생
│   → Blend Tree / Trigger / Bool / Transition 파라미터 미사용
│
Layer 1 (Additive)
    └── Hit_Shake — 피격 흔들림을 additive로 덧씌움
        ConfigState의 Notify(EventName="OnHitShake")가 SendMessage로 호출
```

- 이동(Idle/Walk/Run)도 Blend Tree가 아니라 각 클립을 직접 `CrossFade`로 전환한다.
- 따라서 Animator 파라미터는 **사용하지 않는다.** (재생 상태는 코드/데이터가 관리)

---

## 이동 처리 (MoveMode)

클립마다 `MoveMode`로 이동 방식을 지정한다 ([AnimationConfig.cs](../04.Scripts/AnimationConfig.cs)).

| MoveMode | 설명 | 예시 |
|----------|------|------|
| `None` | 제자리 (이동 없음) | Idle, 경직 |
| `Planar` | 입력 방향으로 코드 이동 (`MoveSpeed`) | 걷기/달리기 루프 |
| `RootMotion` | 루트본 이동량을 추출해 적용 | 공격/대시 |

### 루트모션 (직접 구현)

Unity 기본 "Apply Root Motion" 체크박스가 아니라, [PlayerController.cs](../04.Scripts/Player/PlayerController.cs)
`LateUpdate`에서 **루트본 `localPosition` 델타를 직접 추출 → `CharacterController.Move`로 변환**한다.

- 매 프레임 루트본을 로컬 0으로 리셋해 누적 드리프트 방지
- 루프 클립이 끝→처음으로 되감길 때(normalizedTime wrap) 그 프레임 델타는 버려 튐 방지
- 전이(CrossFade) 구간에서는 델타를 flush 해 점프 방지
- `Bip001` 본의 XZ를 리셋해 메시 드리프트 방지 (Y는 유지)
- 시작 부스트(`StartBoost`)로 클립 초반 루트모션 워밍업 보완

`ConfigState`는 섹션 진입 시 `MoveMode`에 따라
`Controller.UseCodeMovement`(Planar=코드 이동, RootMotion=본 델타)와
`AllowRotation`(LockRotation)을 토글한다.

---

## State 전환 흐름 (config 기반)

```
ConfigState (home = 걷기 config)
    │
    │  매 프레임 현재 섹션의 Links + config.GlobalLinks 평가
    │  (클립 고유 Link 먼저 → GlobalLink 순)
    │
    ├── 공격 입력 버퍼됨 + 조건/타이밍 충족
    │       → TargetConfig(콤보 config)로 전이 → 콤보 섹션 진입
    │           └ 다음 콤보도 Link로 이어짐 (WhenMatched 윈도우)
    │
    ├── 이동 입력 (GlobalLink, Direction 조건)
    │       → Walk/Run 섹션으로 전이
    │
    └── OnEnd / OnWindowMiss
            → home(걷기) config로 복귀
```

### 피격 (외부 이벤트 진입)

```
충돌 검출 → PlayerStateMachine.TriggerHitFrom(attackerPos)
    │  공격자 위치로 Front/Back 판정
    ▼
ConfigState.InterruptWith(hitConfig, "Hit_{L|H}_{Front|Back}", blend)
    │
    ├── 재진입 가드 : 진행도 < 임계값이면 새 피격 무시 (연타 stunlock 방지)
    ├── escalation  : 연속타 카운트로 강도 승격 (홀수타=L / 짝수타=H 교대)
    ├── layer 0     : Hit 반응 클립 재생
    └── layer 1     : Notify(OnHitShake) → additive 흔들림 + weight 페이드
            │
            ▼
        Hit config의 OnEnd Link → home(걷기)로 복귀
```

---

## 입력 버퍼 & 콤보

콤보 진입 판정은 두 경로가 공존한다.

**1. config 기반 (주 경로 — ConfigState)**
```
PlayerStateMachine.BufferInput()      ← 입력을 버퍼에만 저장 (_inputBufferWindow=0.25s)
    │
ConfigState.Update()
    ├── 현재 섹션의 Link 조건(Attack/Direction) + Timing 평가
    ├── HasBufferedInput && BufferedInput == link.Attack 이면 조건 충족
    └── 발동 시 ConsumeInput() 후 TargetConfig/Section으로 전이
```

**2. 하드코딩 콤보 (레거시 — EnhanceComboState)**
```
EnhanceComboState.Update()
├── _nextQueued && normalizedTime >= 0.7f  → 다음 콤보 인덱스 재생
└── _comboTimer >= 1.2f                     → ConfigState로 복귀
```

---

## 파일 구조

```
04.Scripts/
├── AnimationConfig.cs               ScriptableObject + TrackClip/ClipLink/Notify 정의
│
└── Player/
    ├── PlayerController.cs           이동, 입력 수신, 루트모션(본 델타 추출)
    │
    └── StateMachine/
        ├── Core/
        │   ├── IState.cs
        │   ├── StateBase.cs
        │   └── StateMachine.cs
        ├── PlayerStateContext.cs     상태 간 공유 데이터 (Controller/Animator/CC/Transform)
        ├── PlayerAnimatorBridge.cs   Animator 파사드 (CrossFade + additive 레이어)
        ├── PlayerStateMachine.cs     루트 MonoBehaviour, 입력 버퍼, 피격 트리거
        │
        └── States/
            ├── ConfigState.cs        ★ 범용 상태 — config로 대부분의 흐름 구동
            ├── EnhanceComboState.cs  (레거시 하드코딩 콤보)
            ├── RushState.cs          (레거시)
            └── SpecialState.cs       (레거시)
```

---

## 에디터 툴 연동

`05.Editor/AnimationTool/AnimationConfigTool.cs`가 `AnimationConfig`를 시각 편집한다.

- 타임라인에서 클립 배치·Link(베지어 연결선)·Notify 편집
- **Combo 프리뷰** : 입력을 눌러두고 Link 흐름을 그대로 재생 (CrossFade 블렌딩·루트모션 시뮬레이션)
- **라이브 모니터** : 플레이 중 `PlayerStateMachine`을 추적해 현재 config/섹션/입력 버퍼를 실시간 표시
  (`PlayerStateMachine`이 `CurrentConfig`/`CurrentSection`/`CurrentNormalizedTime` 등을 노출)

---

## 추가 예정 시스템

| 시스템 | 설명 |
|--------|------|
| **강화 상태 판단** | 일정 콤보 히트 후 강화 콤보 config로 자동 전환 |
| **카운터 윈도우** | 적 공격 직전 타이밍 감지 → 카운터 섹션 진입 |
| **패리 판정** | 입력 타이밍 기반 패리 섹션 진입 |
| **레거시 State 흡수** | EnhanceCombo/Rush/Special을 config 기반으로 이관 |
