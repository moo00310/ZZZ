# 플레이어 애니메이션 아키텍처

## 핵심 철학

> **코드 State Machine이 "무엇을 할지" 결정 → Animator는 "클립 재생"만 담당**

Animator Controller에 복잡한 Transition/파라미터를 쌓지 않는다.
모든 분기 로직은 C# State Machine 안에서 처리한다.

---

## 전체 구조도

```
[ Input ]
    │  (PlayerInput SendMessages)
    ▼
[ PlayerStateMachine ]        ← MonoBehaviour, 루트 진입점
    │
    │  ChangeState<T>()
    ▼
[ StateMachine ]              ← 순수 C# 상태 관리자
    │
    │  Enter / Update / Exit
    ▼
[ StateBase ]                 ← 각 상태의 추상 베이스
    │
    ├── LocomotionState       → AnimatorBridge.SetLocomotion(speed)
    ├── NormalComboState      → AnimatorBridge.Play("Attack_Normal_01~05")
    ├── EnhanceComboState     → AnimatorBridge.Play("Attack_Normal_Enhance_01~03")
    ├── RushState             → AnimatorBridge.Play("Attack_Rush")
    ├── SpecialState          → AnimatorBridge.Play("Attack_Special_01")
    ├── ExSpecialState        → AnimatorBridge.Play("Attack_ExSpecial_01/02")
    ├── CounterState          → AnimatorBridge.Play("Attack_Counter_01")
    └── ParryState            → AnimatorBridge.Play("Attack_ParryAid_Start")
           │
           ▼
    [ PlayerAnimatorBridge ]  ← Animator 접근 유일한 창구
           │
           │  Animator.Play() / SetFloat() / SetBool()
           ▼
    [ Unity Animator ]        ← 클립 재생만 담당
           │
           ▼
    [ Animation Clips ]       ← FBX에서 추출한 클립들
```

---

## Animator Controller 구조 (최소화)

```
Base Layer
│
├── Locomotion (Blend Tree)
│     0.0 ─── Idle
│     0.5 ─── Walk  
│     1.0 ─── Run
│
└── Action (Single State)
      └── 모든 공격 클립을 Animator.Play()로 직접 호출
          → Transition/Trigger 파라미터 불필요
```

**파라미터는 3개만 유지:**

| 이름 | 타입 | 용도 |
|------|------|------|
| `Speed` | Float | Blend Tree 이동속도 |
| `IsSprinting` | Bool | Walk/Run 전환 |
| `IsGrounded` | Bool | 착지 판단 |

---

## State 전환 흐름

```
대기/이동 중
    │
    ├── 공격 입력 ──────────────────────────→ NormalComboState
    │                                              │
    │                                    Attack_Normal_01
    │                                         입력 있음?
    │                                    ┌──── Yes ────┐
    │                               normalizedTime     │
    │                               >= 0.7f?           │
    │                               Yes → 다음 콤보    No → 큐 대기
    │                                    Attack_Normal_02~05
    │                                         콤보 완료
    │                                              │
    │◀─────────────────────────── LocomotionState ◀┘
    │
    ├── 대시 + 공격 ──────────────────────→ RushState
    │                                    클립 끝 → Locomotion
    │
    ├── 스킬 버튼 ──────────────────────→ SpecialState
    │                                    클립 끝 → Locomotion
    │
    └── 궁극기 버튼 ─────────────────────→ ExSpecialState
                                         클립 끝 → Locomotion
```

---

## 콤보 입력 버퍼 로직

```
NormalComboState.Update()
│
├── _comboTimer += deltaTime
├── normalizedTime = Animator.GetCurrentNormalizedTime()
│
├── [ 입력 큐 있음 && normalizedTime >= 0.7f ]
│     → 다음 콤보 인덱스 실행
│     → AnimatorBridge.Play("Attack_Normal_0X")
│
└── [ _comboTimer >= 1.2f ]
      → ChangeState<LocomotionState>()
```

---

## 파일 구조

```
04.Scripts/Player/
├── PlayerController.cs           이동, 입력 수신
├── TPSCameraController.cs        카메라 오빗
├── PlayerAnimatorController.cs   (구버전 — StateMachine으로 대체)
│
└── StateMachine/
    ├── Core/
    │   ├── IState.cs
    │   ├── StateBase.cs
    │   └── StateMachine.cs
    ├── PlayerStateContext.cs     상태 간 공유 데이터
    ├── PlayerAnimatorBridge.cs   Animator 파사드
    ├── PlayerStateMachine.cs     루트 MonoBehaviour
    │
    └── States/
        ├── LocomotionState.cs
        ├── NormalComboState.cs
        ├── EnhanceComboState.cs
        ├── RushState.cs
        ├── SpecialState.cs
        ├── ExSpecialState.cs     (미구현)
        ├── CounterState.cs       (미구현)
        └── ParryState.cs         (미구현)
```

---

## 추가 예정 시스템

| 시스템 | 설명 |
|--------|------|
| **강화 상태 판단** | 일정 콤보 히트 후 EnhanceComboState 자동 전환 |
| **카운터 윈도우** | 적 공격 직전 타이밍 감지 → CounterState 진입 |
| **패리 판정** | 입력 타이밍 기반 ParryState 진입 |
| **피격 반응** | 어느 State에서든 HitState로 즉시 전환 |