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
[ ConfigState ]               ← 순수 C# 단일 러너 (상태 클래스 1개)
    │   · AnimationConfig(Clips + Links)를 파싱·구동
    │   · 걷기 / 콤보 / 강화공격 / 대시 / 회피 / 패링 / 피격을 전부 이 한 클래스로 표현
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
│     ├── EnableTracking / TrackWindow / StopDistance       적 방향으로 이동 워프
│     ├── FaceTarget / FaceWindow / FaceTurnSpeed            타겟 조준 회전(스냅/락온 통합) — 워프와 독립
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
| `RequireHeld` | Attack 키가 '지금 눌려있을(held) 때' 충족 — 누름 버퍼 대신 홀드 상태 (차지 루프용) |
| `Timing` | 언제 평가할지 — `WhenMatched` / `OnRelease` / `OnEnd` / `OnEndIfMatched` |
| `WindowStart` ~ `WindowEnd` | 평가 구간 (normalizedTime) |
| `BlendDuration` | 전이 시 CrossFade 시간(초) |
| `EntryOffset` | 전이 후 대상 섹션을 이 normalizedTime부터 재생 (중간 진입 — 윈드업 스킵 등) |

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
> `ConfigState`는 섹션 진입 시 `Controller.UseCodeMovement = (MoveMode != RootMotion)`을 토글하고,
> `AllowRotation`은 매 프레임 `LockRotation`+`LockWindow`(normalizedTime 구간)로 제어한다
> (`UpdateRotationWindows`). 잠금 해제 후 회전은 `_rotationEaseTime` 동안 이즈인된다.

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

### 타겟 워프 & 조준 — 이동(EnableTracking)과 회전(FaceTarget)은 독립
RootMotion 섹션 진입 시 전방 적(`EnemySensor.FindTarget()`)을 찾아, **둘 중 켜진 기능만** 적용한다(두 토글은 별개).
적이 없으면 둘 다 무동작 → 원본 모션 그대로 (적 유무 분기 불필요).

**이동 워프 (`EnableTracking`)** — 루트모션 수평 이동을 적 방향으로 재조준 (회전과 무관, 이동만).
- `StopDistance`로 타겟 앞에서 멈춤(관통 방지)
- `TrackWindow`(Start~End) 구간에서만 워프 작동 → 타격 이후엔 끊어 적을 따라 휙 도는 것 방지
- 콤보 단마다 재탐색 → 적이 옆으로 빠져도 다음 타가 따라간다

**타겟 조준 (`FaceTarget`)** — `FaceWindow` 동안 타겟을 향해 회전. 스냅(1회)·락온(지속)을 하나로 통합.
- `FaceWindow [0,0]` = 진입 1회 스냅 / 넓히면 그 구간 내내 락온(적이 움직여도 따라붙음)
- `FaceTurnSpeed` 0 = 즉시(스냅처럼), >0 = 각속도 제한 회전
- **이동 워프와 독립** — 트래킹 없이 회전만 켤 수 있다. `FaceInputOnEnter`(내 입력 방향 조준)가 있으면 진입 스냅보다 우선.

### 섹션 턴 (SectionTurn) — 루트 회전 추출

턴 애니(예: 180° 뒤돌기, TurnBack)에서 캐릭터를 실제로 회전시킨다. 회전을 `transform`에 적용해야
턴 이후 이동/다음 섹션(run_loop)이 새 방향으로 이어진다. `SectionTurn`을 켠 섹션에서만 작동.

**리그 전제 (이 기능의 모든 난이도의 근원)**
- **회전이 `Bip001`(골반)에 구워져** 있다. 별도 `Root` 본은 깨끗한 yaw 커브를 갖지만
  자식이 없는 형제 leaf라 메시를 구동하지 않는다.
- 그래서 **회전 측정은 `Root` 본, 메시 보정은 `Bip001`** 로 분리한다.

**메커니즘** ([PlayerController.cs](../Assets/04.Scripts/Player/PlayerController.cs) `LateUpdate`)

| 단계 | 처리 |
|------|------|
| 트리거 | `MoveDir.Reverse`(진행 반대키, `dot(forward,입력)<-0.707`) → 턴 섹션 전이 (`ConfigState.IsReverseInput`) |
| 회전 추출 | `Root` 본 yaw(up축 twist 분해로 측정) 델타를 `transform`에 누적 (`_rootYawComp`) |
| 메시 카운터 | `_rootYawComp`만큼 `Bip001`을 월드에서 되돌림 → 메시 이중 회전(≈360) 방지 (메시는 애니 원본) |
| 위치 보정 | 이동 변환에서 `-_rootYawComp` → 애니 위치곡선 회전 + transform 회전 이중 적용 방지(거꾸로/사선) |
| 전이 가드 | 진입/CrossFade 프레임은 baseline만 갱신(블렌드 오염 방지), 재진입은 `FlushRootRotation`으로 누적 리셋 |

> 측정에 `Root`를 쓰는 이유: `Bip001`(골반)은 기울고 sway가 심해 forward 투영·twist 둘 다 노이즈가
> 커서 transform이 흔들리고 각도도 안 맞는다. `Root`는 깨끗해 정확·무흔들림.

**겪은 문제와 해결 (디버깅 순서)**

| 증상 | 원인 | 해결 |
|------|------|------|
| 메시 ≈360° 과회전 | transform·Bip001 둘 다 돌아 이중 | `_rootYawComp`만큼 Bip001 카운터 |
| 턴 후 거꾸로/사선 | 위치곡선에 회전 들었는데 transform도 돌아 이중 | 위치 변환에 `-_rootYawComp` |
| 회전 wobble | 골반(Bip001) yaw 측정 노이즈 | 측정원을 깨끗한 Root로 |
| 재진입 이동 틀어짐 | 진입 블렌드 동안 Root 값 오염 누적 | 전이 프레임 추출 스킵, baseline만 |
| 턴→턴 재진입 어긋남 | `_rootYawComp` 리셋 안 됨 | 진입마다 `FlushRootRotation` |
| **턴→run 전이 휘청** | 턴 힙(+180)·run 힙(+0)이 ~180 달라 CrossFade가 slerp로 비선형 휘청 | **하드컷 + 카운터 즉시 해제** (아래) |

**현재 구현 (결론) — 턴→run은 하드컷**

> 회전은 **Root에서 추출 → transform**, 메시 이중회전은 **Bip001 카운터**로 지우고, 턴→run 전이는
> **하드컷(턴→run 링크 `BlendDuration = 0`) + 카운터 즉시 해제**로 처리한다.
> **결과: facing이 전 구간 연속**, 캐릭터는 의도대로 180 돌아 새 방향으로 달린다.

**왜 블렌드가 아니라 하드컷인가** — 턴 클립의 힙은 +180 돌아간 포즈, run 클립의 힙은 정면(+0)으로
~180° 다르다. 둘을 CrossFade로 섞으면 골반 yaw가 **slerp로 비선형 휘청**인다(로그 실측: bipWorldY
187→319→113). 이걸 코드로 펴려면 골반 yaw를 매 프레임 정확히 빼야 하는데, 기울어진 골반은 측정
노이즈가 커서 불가능(transform·메시 흔들림). **→ 블렌드를 버리는 것(하드컷)이 코드로 낼 수 있는
가장 깨끗한 결과**라 이걸 택했다. 블렌드가 없으니 골반이 1프레임에 +180→+0으로 전환되고, 양쪽
프레임 모두 `mesh = transform facing`이라 연속이 된다.

**감수한 비용** — 전이 1프레임에 **사지 stride 로컬 포즈만** 컷된다. facing·이동은 멀쩡하고, 턴이
달리는 포즈로 끝나면 이 컷은 거의 안 보인다. (구간 회전잠금 `LockWindow`, 잠금 해제 후
`_rotationEaseTime` 이즈인도 함께 동작.)

**더 매끄럽게 하려면 (선택 — 애니/리그)** — 근본 원인이 "턴이 골반에 구워진 것"이라, 아래 중 하나면
**일반 블렌드가 그냥 부드러워지고 위 카운터/하드컷 우회 자체가 거의 불필요**해진다:
- ① 턴 회전을 `Root` 노드(루트모션)에 굽기 → 골반 항상 정면 → 턴끝 골반 = run 골반 → 블렌드 무휘청 (정석)
- ② 턴 클립 끝 포즈 = Run_Loop 시작 포즈로 맞추기 → slerp 폭 축소 → 블렌드 가능

---

## 섹션 모듈 (SectionModule) — 기능을 끼워 넣는 플러그인

**기능** — 한 섹션에 붙는 기능 단위. `TrackClip.Modules`에 `[SerializeReference]`로 **폴리모픽 직렬화**된다.
새 연출/판정 기능 = 베이스 상속 1개 추가 (ConfigState 본체는 안 건드림).

**동작** — `ConfigState`가 섹션 진입 시 `OnEnter`, 매 프레임 `Tick`을 호출한다.
구간(Start~End) 동안 무언가를 켜는 패턴이 반복돼서, 구간 판정을 `WindowModule` 베이스로 한 번만 짜고
"무엇을 켜느냐"만 자식이 정의한다.

```
SectionModule (추상)           OnEnter(1회) / Tick(매 프레임)
└── WindowModule (추상)        [Start, End] normalizedTime 구간 판정(InWindow) 공유 — 루프 wrap 처리 포함
    ├── IFrameModule           구간 동안 Machine.Invulnerable = true   → 피격 '무시'
    └── ParryModule            구간 동안 Machine.ParryActive  = true   → 피격을 '쳐냄'으로 분기
```

`ConfigState`는 섹션 진입 시 `Invulnerable`/`ParryActive`를 **false로 리셋**하고, 모듈이 윈도우 동안만 다시 켠다.
`HitTrigger`는 `Invulnerable`이면 피격을 무시(회피 i-frame), `ParryActive`면 쳐냄으로 분기(패링).

- **장점** — 무적·패링처럼 "구간 동안 플래그를 켜는" 판정을 **클래스 1개 + 구간 두 값**으로 추가한다. 툴의 추가 메뉴/구간 편집 UI가 베이스 덕에 자동으로 잡힌다. i-frame('무시')과 parry('응수')가 대칭이라 읽기 쉽다.
- **단점** — `[SerializeReference]` 폴리모픽은 타입 리네임/이동 시 직렬화가 깨질 수 있고, 모듈 간 실행 순서·상호작용이 늘면 암묵적 의존이 생긴다(현재는 플래그 토글뿐이라 단순).

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

> `ComboInput` = `Normal / Enhanced / Attack_Normal_Enhance / Dodge / Any / None / Parry`.
> 입력은 누름 버퍼(0.25s)와 별개로 **홀드 상태(`IsHeld`)** 도 추적한다 — `OnRelease`/`RequireHeld`가 이 홀드 상태를 본다 (홀드 차지 → 릴리스). `Attack_Normal_Enhance` 입력 액션은 떼는 콜백을 받으려고 **PassThrough** 타입이다.

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

## config 자동 검색 (FindConfigWithSection)

이벤트로 진입하는 config(Hit, Evade 등)는 개별 필드를 두지 않는다.
`PlayerStateMachine`의 `_configs` 리스트에 **드롭만** 하면, 재생할 **섹션 이름으로 자동 검색**해 진입한다.

- 새 회피/피격 모션을 추가해도 코드 수정 불필요 — config를 만들어 리스트에 넣고 섹션 이름 규약만 지키면 됨
- 링크로 도달 가능한 config(콤보 등)는 리스트에 넣을 필요 없음 — `TargetConfig` 참조로 연결

> **트리거 설정도 인스펙터에서** — `Hit/Dodge/Parry/Attack_Normal_Enhance` 트리거는 `[Serializable]` 객체라
> 각자 설정(섹션 이름·blend·거리 임계 등)을 직접 들고, `PlayerStateMachine` 인스펙터에 폴드로 노출된다.
> 런타임 의존(상태/레지스트리/입력)만 `Init()`으로 주입한다. (평평하던 머신 필드 정리)

---

## 파일 구조

```
Assets/04.Scripts/
├── AnimationConfig.cs               ScriptableObject + TrackClip/ClipLink/Notify/enum 정의
│
├── Combat/
│   ├── EnemySensor.cs               전방 부채꼴 적 탐지 (워프 타겟 / 거리 분기)
│   ├── HitTarget.cs                 피격 대상(허수아비) — HP 보유
│   └── IHittable.cs                 피격 가능 인터페이스
│
├── Movement/
│   └── RootMotionTracker.cs         본 위치 프레임 델타 추출 헬퍼
│
└── Player/
    ├── PlayerController.cs           이동/루트모션(위치=Bip001 / 회전=Root yaw 추출)·워프·섹션턴
    ├── PlayerResources.cs            플레이어 자원(스태미나 등)
    ├── PlayerStateHUD.cs             현재 config/섹션/입력 디버그 HUD
    ├── TPSCameraController.cs        커스텀 TPS 카메라
    │
    ├── Debug/                        AdditiveCompareTool / AnimatorLayerHUD
    │
    └── StateMachine/
        ├── PlayerStateMachine.cs     코디네이터 — 입력버퍼·트리거·config 검색 조립 + facade
        ├── PlayerStateContext.cs     상태 공유 데이터 (Controller/Animator/CC/Transform)
        ├── PlayerAnimatorBridge.cs   Animator 파사드 (Play + additive Hit_Shake)
        ├── PlayerTestTriggers.cs     테스트 입력(H/J/K) 분리 컴포넌트
        ├── ConfigRegistry.cs         섹션 이름으로 config 검색 (FindWithSection)
        ├── InputBuffer.cs            선입력 버퍼
        │
        ├── States/
        │   └── ConfigState.cs        ★ 단일 러너 — config로 모든 흐름 구동
        │
        ├── Triggers/                 외부/전역 진입 (push)
        │   ├── HitTrigger.cs             피격 + 패링 쳐냄 분기
        │   ├── DodgeTrigger.cs           회피 (방향→섹션)
        │   ├── ParryTrigger.cs           패링 스탠스 진입
        │   └── Attack_Normal_EnhanceTrigger.cs  강화공격 (방향/거리 분기)
        │
        └── Modules/                  섹션 플러그인 (폴리모픽)
            ├── SectionModule.cs          추상 베이스 (OnEnter/Tick)
            ├── WindowModule.cs           구간(Start~End) 판정 베이스
            ├── SectionContext.cs         모듈이 받는 런타임 핸들 묶음
            ├── IFrameModule.cs           무적 구간(i-frame)
            └── ParryModule.cs            패링 활성 구간
```


---

## 에디터 툴 연동

`Assets/05.Editor/AnimationTool/AnimationConfigTool.cs`가 `AnimationConfig`를 시각 편집한다.

- 타임라인에서 클립 배치·Link(베지어 연결선)·Notify·Module 편집
  - **Module 추가** : 등록된 `SectionModule` 타입을 드롭다운으로 자동 나열 — 새 모듈 = 클래스 1개 추가 → 메뉴 자동 등장
- **Combo 프리뷰** : 공격 입력은 단일 드롭다운으로 '눌러둠(held)' 선택 → Link 흐름을 그대로 재생 (CrossFade 블렌딩·루트모션 시뮬레이션)
- **라이브 모니터** : 플레이 중 `PlayerStateMachine`을 추적해 현재 config/섹션/입력 버퍼/**Held**(눌린 키)를 실시간 표시
  (`CurrentConfig`/`CurrentSection`/`CurrentNormalizedTime`/`CurrentMoveDir`/`IsInputHeld` 등을 노출)

---
