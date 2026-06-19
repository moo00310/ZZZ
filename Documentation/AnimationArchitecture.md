# 플레이어 애니메이션 아키텍처

## 핵심 철학

> **분기는 C# 코드가, "무엇을 언제 재생할지"는 데이터(Config)가, Animator는 "클립 재생"만 담당**

Animator Controller에 복잡한 Transition/파라미터를 쌓지 않는다.
- 분기/전이 로직은 `ConfigState`(C#)가 처리하고,
- 콤보/피격/회피 같은 "전이 흐름"은 `AnimationConfig`(ScriptableObject) **데이터**로 정의하며,
- Animator는 `CrossFadeInFixedTime(클립명)`으로 클립을 재생하는 역할만 한다.

그 결과 Animator Controller에는 Trigger/Bool/Transition 화살표가 거의 없다.

---

## 전체 구조도

```
[ Input ]
    │  (PlayerInput SendMessages → 입력 버퍼링)
    ▼
[ PlayerStateMachine ]        ← MonoBehaviour, 얇은 코디네이터(조립 + facade)
    │   · 협력 객체 조립: InputBuffer / HitTrigger / DodgeTrigger / ConfigRegistry
    │   · 무적(Invulnerable)·퍼펙트 회피 윈도우 보유 + facade 노출
    │   ├── InputBuffer      입력 버퍼 (Normal / Dodge)
    │   ├── HitTrigger       피격 트리거 (재진입 가드 + L/H 승격)
    │   ├── DodgeTrigger     회피 트리거 (push, 방향→섹션 선택)
    │   └── ConfigRegistry   섹션 이름으로 config 자동 검색 (FindWithSection)
    │   (테스트용 H/J/K 키는 PlayerTestTriggers 컴포넌트로 분리)
    │
    │  단일 ConfigState 보유 → Enter() / Update() 직접 호출
    ▼
[ ConfigState ]               ← 순수 C# 단일 러너 (상태 클래스 1개)
    │   · AnimationConfig(Clips + Links)를 파싱·구동
    │   · 걷기 / 콤보 / 대시 / 회피 / 피격을 전부 이 한 클래스로 표현
    │   · Link(전이) · Notify(이벤트) · Module(기능) · 워프/섹션턴 처리
    │
    ├── 클립 재생 ──────────────┐
    │                          ▼
    │              [ PlayerAnimatorBridge ]  ← Animator 접근 유일 창구 (파사드)
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
> (과거의 `Core/`(IState·StateBase·StateMachine)와 하드코딩 State들은 모두 제거됨)

---

## AnimationConfig — 데이터로 정의하는 전이 트랙

`AnimationConfig`는 "언리얼 엔진의 몽타주 스타일"의 ScriptableObject 트랙이다. 코드 수정 없이
에셋만 편집해서 콤보/피격/회피 연출을 구성한다.

```
AnimationConfig
├── EntrySection            진입 시 재생할 섹션 (빈 값 = 첫 클립)
├── LoopTrack / DoneThreshold
├── Clips : List<TrackClip>     ← 섹션(클립) 목록
│     ├── SectionName            섹션 식별자
│     ├── Clip / Speed
│     ├── MoveMode               None / RootMotion
│     ├── LockRotation / LockWindow / FaceInputOnEnter   입력 회전 잠금(구간 지정 가능) / 진입 시 입력 방향 스냅
│     ├── SmoothLoopSpeed          루프 전진 평속화(틱 제거) — RootMotion 루프 전용, 기본 꺼짐
│     ├── StartBoostSpeed / StartBoostTime     루트모션 워밍업 보완
│     ├── EnableTracking / TrackWindow / StopDistance / SnapRotation   적 워프
│     ├── SectionTurn / TurnWindow              루트 회전 추출(턴) — Root 본 yaw를 transform에
│     ├── Links    : List<ClipLink>     ← 이 섹션에서 분기 가능한 전이
│     ├── Notifies : List<TrackNotify>  ← 재생 중 발동할 이벤트/이펙트
│     └── Modules  : List<SectionModule>  ← 섹션 기능 (i-frame 등, 폴리모픽)
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
| `BlendDuration` | 전이 시 CrossFade 시간(초) |

- **WhenMatched** : 윈도우 구간 안에서 조건 충족 즉시 (콤보 입력 / 방향 이동 / 복귀)
- **OnWindowMiss** : 윈도우 끝까지 조건이 안 맞고 지나가면 (콤보 캔슬 / 타임아웃)
- **OnEnd** : 클립이 끝나면 (조건은 가드로 작동, 루프 클립엔 무효)

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
- `PlayerAnimatorBridge.Play()`는 `CrossFadeInFixedTime`(초 단위 고정 시간)을 쓴다 →
  config의 `BlendDuration`(초)과 단위가 일치. 함께 `ApplyAnimatorSpeed(Speed)`로
  애니 재생 속도를 로직 타임라인과 맞춘다(안 맞추면 전환 딜레이 발생).

---

## 시간 계산 — 애니메이터 시간을 안 믿는다

`ConfigState`는 normalizedTime을 Animator에서 읽지 않고 **섹션 진입 후 경과 시간(`_clipTime`)** 으로
직접 계산한다(`SectionNormalizedTime`).

```
nt = _clipTime * Speed / clip.length
```

- CrossFade 중 Animator가 "이전 클립의 시간"을 반환해 윈도우/Notify가 어긋나는 문제를 피한다.
- 전환마다 `_clipTime`을 0으로 리셋 → 섹션 타임라인이 항상 0부터 시작.
- 루프 클립은 `Mathf.Repeat(nt, 1f)`로 사이클 내 진행도를 본다.

---

## 이동 처리 (MoveMode)

클립마다 `MoveMode`로 이동 방식을 지정한다 ([AnimationConfig.cs](../Assets/04.Scripts/AnimationConfig.cs)).

| MoveMode | 설명 |
|----------|------|
| `None` | 제자리 (루트모션 안 씀, 중력만 코드 적용) — Idle, 경직 |
| `RootMotion` | `Bip001` 본의 수평 이동량을 추출해 적용 — 걷기/달리기/공격/대시/회피 |

> 값 1은 과거 `Planar`(코드 이동) 자리였으나 폐기. `RootMotion=2` 직렬화 호환을 위해 enum에서 1은 비워 둔다.
> 걷기·달리기도 코드 이동이 아니라 **RootMotion**으로 처리한다.
> `ConfigState`는 섹션 진입 시 `Controller.UseCodeMovement = (MoveMode != RootMotion)`,
> `AllowRotation = !LockRotation`을 토글한다.

### 루트모션 (직접 구현)

Unity 기본 "Apply Root Motion" 체크박스는 제어 폭이 좁아 쓰지 않고,
[PlayerController.cs](../Assets/04.Scripts/Player/PlayerController.cs) `LateUpdate`에서 직접 이동을 추출해 적용한다.

**구현 방식 — `Bip001` 본에서 수평 이동 추출**

이 캐릭터의 클립은 전진·좌우 스웨이·상하 바운스가 전부 `Bip001` 본에 구워져 있다
(`Root` 노드엔 위치 커브가 없어 이동 추출에 쓰지 않는다).

| 축 | 처리 |
|----|------|
| X·Z (수평) | 프레임 간 델타를 뽑아 `CharacterController.Move`로 실제 이동에 사용 |
| Y (수직 바운스) | 메시에 그대로 남겨 달리기 상하 리듬 유지 (캐릭터 본체 Y는 중력이 담당) |

1. **추출** — `Bip001`의 로컬 위치 프레임 델타에서 수평(X·Z)만 뽑아 월드로 변환(`transform.TransformDirection`) 후 `_rootMotionScale` 적용.
2. **상쇄** — 같은 프레임에 `Bip001`의 X·Z를 로컬 0으로 리셋(메시 드리프트/발 미끄러짐 방지). Y는 유지.
3. **적용** — 순수 수평 이동량만 `CharacterController.Move`로 먹이고, 수직은 중력(`_verticalVelocity`)으로 따로 적용.

**문제와 해결** — 본에서 직접 이동을 뽑다 보니 여러 "튐"이 생겼다. 각각 어떻게 막았는지.

> 용어: **되감기**=루프가 끝나 처음으로 돌아가는 순간 · **기준점**=이동량을 재는 직전 프레임 위치 · **블렌드(전환)**=동작이 바뀔 때 두 클립이 잠깐 섞이는 구간.

| 증상 | 왜 생기나 | 해결 |
|------|-----------|------|
| 가만히 있어도 캐릭터가 스르륵 밀림 / 몸(메시)만 따로 떠서 실제 위치와 어긋남 | 애니에 담긴 이동값이 매 프레임 쌓임 | 매 프레임 `Bip001`의 수평(X·Z)을 0으로 되돌려 누적을 끊음. 높이(Y)는 남겨 상하 바운스 유지 |
| 루프가 처음으로 돌아가는 순간 순간이동 | 되감기 프레임엔 위치가 확 뒤로 점프함 | 그 한 프레임의 이동은 버림(0 처리) |
| 루프마다 본체가 잠깐 멈칫("틱틱") | 전진 커브가 끝에서 1프레임 멈췄다 되감김 → 다리는 도는데 본체만 안 나감 (포즈·회전은 시작=끝이라 매끄러움) | 루프 클립은 한 바퀴 **평균 전진속도**(`_loopVelLocal`)를 재서 다음 바퀴부터 일정하게 전진. 멈칫 없고, 평균이라 안 빨라지고, 회전·다리는 원본이라 보폭 유지. 공격 등 비루프는 원본 그대로. **섹션(TrackClip)별 `SmoothLoopSpeed` 토글** — 애니 섹션 툴에서 RootMotion 섹션에 체크박스로 on/off (끄면 원본 보폭감 유지, 틱 보임). `ConfigState`가 섹션 진입 시 `Controller.SmoothLoopSpeed`로 전달 |
| 동작이 바뀔 때 캐릭터가 점프 | 새 동작에 들어가도 직전 기준점이 남아있음 | 진입 순간 기준점을 새로 잡음 (`FlushRootPos`) |
| 동작 전환(블렌드) 중 앞/뒤로 튐 | 블렌드 구간엔 두 클립 포즈가 섞여 "가짜 이동"이 나옴 | 블렌드 구간(+직후 1프레임)의 수평 이동은 통째로 버림 (≤0.05s라 짧고, 끝나면 기준점 다시 잡아 재개) |
| 동작 초반이 굼떠 보임 | 루트모션이 시작 순간엔 이동량이 약함 | 시작 부스트로 초반 가속 보강 (`StartBoostSpeed`/`StartBoostTime`) |

> 평균 전진속도 방식의 트레이드오프: 보폭 안의 미세한 가감속(디딜 때 느리고 찰 때 빠른 것)이 평탄해져 발이 살짝 미끄러질 수 있으나, 달리기 속도에선 대개 안 보인다.

---

## 타겟 워프 & 섹션 턴 (공격 보정)

애니 원본만으로는 적을 정확히 못 때리므로, 루트모션 위에 두 가지 보정을 얹는다.

### 타겟 워프 (EnableTracking)
RootMotion 섹션 진입 시 전방 적(`EnemySensor.FindTarget()`)을 찾아 **루트모션 수평 이동을 적 방향으로 재조준**한다.

- 적이 없으면 보정량 0 → 원본 루트모션 그대로 (적 유무 분기 불필요)
- `StopDistance`로 타겟 앞에서 멈춤(관통 방지)
- `SnapRotation`이면 진입 시 타겟 방향으로 즉시 회전 (단, `FaceInputOnEnter`로 이미 입력 방향을 봤으면 생략)
- `TrackWindow`(Start~End) 구간에서만 워프 작동 → 타격 이후엔 끊어 적을 따라 휙 도는 것 방지
- 콤보 단마다 재탐색 → 적이 옆으로 빠져도 다음 타가 따라간다

### 섹션 턴 (SectionTurn) — 루트 회전 추출

턴 애니(예: 180° 뒤돌기)에서 캐릭터를 실제로 회전시킨다. 회전을 `transform`에 적용해야 턴 이후
이동/다음 섹션이 새 방향으로 이어진다. `SectionTurn`을 켠 섹션에서만 작동.

**리그 전제**
- 3ds Max Biped. 위치·회전이 `Bip001`(골반)에 구워져 있고, 별도 `Root` 본은 깨끗한 yaw를 가졌지만
  메시를 구동하지 않는 형제 leaf 노드다.
- 그래서 **회전 측정은 `Root` 본, 메시 보정은 `Bip001`** 로 분리한다.
  인스펙터의 PlayerController → Root Motion → **Root Bone** 슬롯에 Root를 할당해야 한다.

**메커니즘** ([PlayerController.cs](../Assets/04.Scripts/Player/PlayerController.cs) `LateUpdate`)

| 단계 | 처리 |
|------|------|
| 회전 추출 | `Root` 본 yaw 델타를 `transform`(월드 up)에 누적 적용 (`_rootYawComp`) |
| 메시 카운터 | `_rootYawComp`만큼 `Bip001`을 월드 공간에서 되돌림 → 메시 이중 회전(≈360) 방지 |
| 위치 보정 | 이동 변환에서 `-_rootYawComp` 적용 → 애니 위치곡선의 회전과 transform 회전 이중 적용 방지 |

> 핵심 개념은 **Unity의 root motion과 동일** — 메시는 애니 원본 그대로 재생(자연 sway 유지),
> `transform`만 루트(Root yaw)를 추적한다. `Bip001` 자체를 측정하지 않으므로 골반 노이즈 wobble이 없다.

**문제와 해결**

| 증상 | 왜 생기나 | 해결 |
|------|-----------|------|
| 메시가 ≈360°로 과회전 | transform과 Bip001이 둘 다 돌아 이중 적용 | transform에 넣은 누적 yaw(`_rootYawComp`)만큼 Bip001을 월드에서 되돌림 |
| 턴 후 달리기가 거꾸로/사선 | 애니 위치곡선에 이미 회전이 들었는데 transform도 돌아 이중 | 위치 변환에서 `-_rootYawComp`를 빼 섹션 시작 회전 기준으로 |
| 회전이 떨림(wobble) | Bip001(골반) yaw를 측정해 카운터 → 기울어진 본+sway가 노이즈 | Bip001 측정 폐기, transform에 넣은 Root yaw만큼만 되돌림 |
| 재진입 시 이동 틀어짐 | 진입 CrossFade 동안 Root가 두 클립 블렌드라 값 오염 | 전이(`transitioning`) 프레임은 추출 건너뛰고 baseline만 갱신 |
| 턴→턴 재진입 시 어긋남 | `_rootYawComp`가 리셋 안 돼 카운터 과다 | 섹션 진입마다 `FlushRootRotation`으로 baseline+누적 리셋 |

**트리거** — 진행 반대 방향키(`MoveDir.Reverse`)로 턴 섹션에 전이.
`dot(forward, 입력) < -0.707`(>135°)로 판정 (`ConfigState.IsReverseInput`).
입력 회전 잠금(`LockRotation`)도 `LockWindow`(normalizedTime 구간)로 부분 적용 가능 — 잠금 해제 후
회전은 `_rotationEaseTime` 동안 이즈인되어 "툭" 튀지 않는다.

> **미구현** — 턴→run 나갈 때 카운터를 전이 진행도에 맞춰 페이드아웃(전환 팝 방지),
> `TurnWindow` 구간 한정 추출, 누적 ±180 클램프는 아직 안 넣음.

---

## 섹션 모듈 (SectionModule) — 기능을 끼워 넣는 플러그인

한 섹션에 붙는 기능 단위. `TrackClip.Modules`에 `[SerializeReference]`로 **폴리모픽 직렬화**된다.
새 연출/판정 기능 = `SectionModule` 상속 1개 추가 (ConfigState 본체는 안 건드림).

```
SectionModule (추상)
├── OnEnter(tc, ctx)        섹션 진입 시 1회
└── Tick(tc, nt, ctx)       매 프레임
    └── IFrameModule        무적 구간 — 윈도우(Start~End) 동안 Machine.Invulnerable = true
```

`ConfigState`는 섹션 진입 시 `Machine.Invulnerable = false`로 리셋하고, `IFrameModule`이
윈도우 동안만 다시 켠다. `TriggerHit`는 `Invulnerable`이면 피격을 무시 → **회피 i-frame**.

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
    ├── 방향(W/A/D) 일반        → Evade_Front  (FaceInputOnEnter로 입력 방향 회전)
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

- **피격 흔들림을 반응 모션 "위에" 겹치기 (additive 레이어)** → 흔들림 클립으로 반응을 *덮어쓰면* 방향별 피격 포즈(Front/Back·L/H)가 사라진다. 그래서 별도 **additive 레이어(layer 1)** 에 `Hit_Shake`를 올려 base 반응(layer 0) **위에 더해** 재생 → 반응 포즈는 그대로 둔 채 흔들림만 얹힌다. 흔들림은 클립 길이만큼 유지한 뒤 레이어 weight를 0으로 페이드해 깔끔히 사라진다 (`PlayerAnimatorBridge.ShakeRoutine`).
  - **데이터 ↔ 코드 분리** : config는 `Notify(EventName="OnHitShake")`로 "여기서 흔들림" 신호만 보내고, 실제 레이어/weight 제어는 `PlayerAnimatorBridge`가 담당 (SendMessage로 느슨하게 결합) → 흔들림 타이밍을 코드 수정 없이 에셋에서 조절
- **같은 약한 움찔거림만 반복돼 단조로움** → escalation: 연속타 카운트로 약(L) ↔ 강(H) 반응을 교대 재생
- **연타 stunlock(보조 가드)** → 반응이 충분히 진행되기 전(`_hitReinterruptThreshold`)엔 새 피격을 무시

---

## 입력 버퍼 & 콤보

```
PlayerStateMachine
    ├── OnAttack → BufferInput(ComboInput.Normal)
    └── OnDodge  → BufferInput(ComboInput.Dodge)
            │  버퍼에만 저장 (_inputBufferWindow = 0.25s)
            ▼
        Update()
            ├── 버퍼가 Dodge면 → TriggerDodge() (콤보보다 우선)
            └── ConfigState.Update()
                    ├── 현재 섹션 Link의 조건(Attack/Direction) + Timing 평가
                    ├── HasBufferedInput && BufferedInput == link.Attack 이면 조건 충족
                    └── 발동 시 ConsumeInput() 후 TargetConfig/Section으로 전이
```

> 콤보 진입 경로는 이제 **config 기반 단일 경로**다. (과거 하드코딩 콤보 State는 제거됨)
> `ComboInput` = `Normal / Enhanced / Special / Dodge / Any / None`.

### 문제와 해결

- **선입력은 받되 아무 때나 콤보가 나가면 안 됨** → 입력을 0.25초(`_inputBufferWindow`) 버퍼에 저장해두고, Link의 `Timing`/`Window`로 "콤보를 이어갈 수 있는 구간"에서만 다음 동작이 발동하도록 데이터로 제어
- **회피와 콤보 입력이 충돌** → 회피(Dodge)는 링크 평가보다 먼저 검사해 콤보보다 우선 (공격 중에도 회피로 캔슬 가능)

---

## config 자동 검색 (FindConfigWithSection)

이벤트로 진입하는 config(Hit, Evade 등)는 개별 필드를 두지 않는다.
`PlayerStateMachine`의 `_configs` 리스트에 **드롭만** 하면, 재생할 **섹션 이름으로 자동 검색**해 진입한다.

- 새 회피/피격 모션을 추가해도 코드 수정 불필요 — config를 만들어 리스트에 넣고 섹션 이름 규약만 지키면 됨
- 링크로 도달 가능한 config(콤보 등)는 리스트에 넣을 필요 없음 — `TargetConfig` 참조로 연결

---

## 파일 구조

```
Assets/04.Scripts/
├── AnimationConfig.cs               ScriptableObject + TrackClip/ClipLink/Notify/enum 정의
│
└── Player/
    ├── PlayerController.cs           이동, 입력 수신, 루트모션(위치=Bip001 / 회전=Root yaw 추출), 워프
    ├── PlayerStateHUD.cs             현재 config/섹션/입력 디버그 HUD
    ├── TPSCameraController.cs        TPS 카메라
    │
    ├── Debug/
    │   ├── AdditiveCompareTool.cs
    │   └── AnimatorLayerHUD.cs
    │
    └── StateMachine/
        ├── PlayerStateMachine.cs     루트 MonoBehaviour — 입력 버퍼, 피격/회피 트리거, config 검색
        ├── PlayerStateContext.cs     상태 공유 데이터 (Controller/Animator/CC/Transform)
        ├── PlayerAnimatorBridge.cs   Animator 파사드 (Play + additive Hit_Shake)
        │
        ├── States/
        │   └── ConfigState.cs        ★ 단일 러너 — config로 모든 흐름 구동
        │
        └── Modules/
            ├── SectionModule.cs      섹션 기능 추상 베이스 (폴리모픽)
            ├── SectionContext.cs     모듈이 받는 런타임 핸들 묶음
            └── IFrameModule.cs       무적 구간(i-frame)
```

> 과거의 `StateMachine/Core/`(IState·StateBase·StateMachine)와
> `States/`의 하드코딩 State(EnhanceCombo·Rush·Special)는 모두 제거되었다.

---

## 에디터 툴 연동

`Assets/05.Editor/AnimationTool/AnimationConfigTool.cs`가 `AnimationConfig`를 시각 편집한다.

- 타임라인에서 클립 배치·Link(베지어 연결선)·Notify·Module 편집
- **Combo 프리뷰** : 입력을 눌러두고 Link 흐름을 그대로 재생 (CrossFade 블렌딩·루트모션 시뮬레이션)
- **라이브 모니터** : 플레이 중 `PlayerStateMachine`을 추적해 현재 config/섹션/입력 버퍼를 실시간 표시
  (`CurrentConfig`/`CurrentSection`/`CurrentNormalizedTime`/`CurrentMoveDir` 등을 노출)

---
