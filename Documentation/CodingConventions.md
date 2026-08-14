# ZZZ — 코딩 컨벤션

## 네임스페이스

> 현재 코드에 실제로 존재하는 네임스페이스만 적는다. (계획만 있고 코드가 없는 네임스페이스는 추가하지 않음 — 생길 때 등록)

| 위치 | 네임스페이스 |
|------|-------------|
| 04.Scripts/Core (AnimationConfig 등 공용) | `ZZZ` |
| 04.Scripts/Movement | `ZZZ` |
| 04.Scripts/Player | `ZZZ.Player` |
| 04.Scripts/Player/StateMachine | `ZZZ.Player.StateMachine` |
| 04.Scripts/Player/StateMachine/States | `ZZZ.Player.StateMachine.States` |
| 04.Scripts/Combat | `ZZZ.Combat` |
| 04.Scripts/Monster | `ZZZ.Monster` |
| 05.Editor/AnimationTool | `ZZZ.Editor.AnimationTool` |

> `Core/`의 공유 타입(`AnimationConfig` / `ConfigDriving` / `LinkCondition`)과 `Movement/`는 폴더와 무관하게 루트 `ZZZ`를 쓴다 — 플레이어·몬스터 공용이라 최상위 네임스페이스에 둔다.
> `StateMachine` 하위(States/Triggers/Modules)는 전부 `ZZZ.Player.StateMachine`을 쓴다(폴더별로 더 쪼개지 않음).

---

## 네이밍 컨벤션

### 클래스 / 구조체 / 열거형
```csharp
// PascalCase
public class PlayerMotor { }
public struct HitData { }
public enum AttackType { Light, Heavy, Special }
```

### 메서드
```csharp
// PascalCase
public void TakeDamage(float amount) { }
private void UpdateAnimationState() { }
```

### 필드
```csharp
// private → _camelCase
private float _currentHp;
private bool _isAttacking;

// SerializeField → [SerializeField] private _camelCase
[SerializeField] private float _moveSpeed = 5f;
[SerializeField] private Animator _animator;

// public 상수 → UPPER_SNAKE_CASE
public const int MAX_COMBO_COUNT = 5;

// static readonly → PascalCase
private static readonly int AnimHashAttack = Animator.StringToHash("Attack");
```

### 프로퍼티
```csharp
// PascalCase
public float CurrentHp { get; private set; }
public bool IsAlive => _currentHp > 0f;
```

### 로컬 변수 / 파라미터
```csharp
// camelCase
float deltaTime = Time.deltaTime;
void SetTarget(Transform targetTransform) { }
```

### 이벤트 / 델리게이트
```csharp
// Action / UnityEvent → On + PascalCase
public event Action<float> OnDamaged;
public UnityEvent OnDeath;
```

---

## 파일 구조 규칙

### 런타임 스크립트 (`04.Scripts/`)
```csharp
using System;
using UnityEngine;

namespace ZZZ.Combat
{
    public class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private float _attackDamage = 10f;

        private Animator _animator;
        private static readonly int AnimHashAttack = Animator.StringToHash("Attack");

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        public void PerformAttack()
        {
            _animator.SetTrigger(AnimHashAttack);
        }
    }
}
```

### 에디터 스크립트 (`05.Editor/`)
```csharp
using UnityEngine;
using UnityEditor;

namespace ZZZ.Editor.AnimationTool
{
    public class AnimationToolWindow : EditorWindow
    {
        [MenuItem("ZZZ/Animation Tool")]
        public static void OpenWindow()
        {
            GetWindow<AnimationToolWindow>("Animation Tool");
        }

        private void OnGUI()
        {
            // GUI 코드
        }
    }
}
```

---

## 규칙 요약

| 항목 | 규칙 |
|------|------|
| 클래스 / 메서드 / 프로퍼티 | `PascalCase` |
| private 필드 | `_camelCase` |
| 로컬 변수 / 파라미터 | `camelCase` |
| 상수 | `UPPER_SNAKE_CASE` |
| Animator 해시 | `private static readonly int AnimHash + 이름` |
| 인스펙터 노출 | `[SerializeField] private` — `public` 필드 사용 금지 |
| 네임스페이스 | `ZZZ.<모듈>` 필수 |
| 에디터 코드 격리 | `05.Editor/` 에만 위치, 런타임 의존 금지 |
| 주석 | WHY가 명확할 때만 작성, WHAT 주석 금지 |

---

## Animator 해시 관리
```csharp
// 매 프레임 StringToHash 호출 방지 — 반드시 static readonly 로 캐싱
private static readonly int AnimHashIdle    = Animator.StringToHash("Idle");
private static readonly int AnimHashRun     = Animator.StringToHash("Run");
private static readonly int AnimHashAttack  = Animator.StringToHash("Attack");
private static readonly int AnimHashHit     = Animator.StringToHash("Hit");
```

## null 체크
```csharp
// Unity Object는 == null 사용 (C# ?. 연산자 사용 금지)
if (_target == null) return;

// 일반 C# 클래스는 ?. 사용 가능
_callback?.Invoke();
```
