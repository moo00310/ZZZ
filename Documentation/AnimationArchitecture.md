# 전투 애니메이션 구현 요약

`AnimationConfig`가 전투 흐름을 저장하고 `CharacterActionRunner`가 이를 실행한다. 플레이어와 몬스터는 같은 실행기를 사용하며, Animator는 전이 판단이 아니라 클립 재생만 담당한다.

## 해결하려는 문제

- Animator Controller에 콤보와 전투 조건이 분산되면 흐름을 추적하고 재사용하기 어렵다.
- 공격 타이밍, 이동 보정, 판정과 연출을 코드에 직접 넣으면 새 공격마다 코드 수정이 필요하다.
- 플레이어와 몬스터가 별도 상태 실행기를 가지면 같은 기능을 중복 구현하게 된다.

## 전체 흐름

    입력과 게임 상태
           │
           ▼
    AgentActionController
           │
           ▼
    AnimationConfig ── Section · Link · Notify · Module
           │
           ▼
      CharacterActionRunner ──── Section · Link · Module 실행
           │
           ├────────── CharacterAnimatorBridge: CrossFade 재생
           ├────────── AgentMotor / MonsterMotor: 이동과 회전
           └────────── CharacterNotifyRunner: Notify 시간과 생명주기
                              │
                              ├── EffectService / AudioService: 연출
                              ├── CameraFeedbackService: 카메라 피드백
                              └── HitService: 판정과 피격 전달

| 구성 요소 | 역할 |
|---|---|
| Section (`TrackClip`) | 재생할 클립, 속도와 루트 모션 설정 |
| Link (`ClipLink`) | 전이 대상, 조건, 평가 시점과 입력 윈도우 |
| Notify (`TrackNotify`) | 특정 시점 또는 구간에 타격·이펙트·사운드·카메라 요청 |
| Module (`SectionModule`) | 구간별 이동, 회전, 타깃 보정, 무적과 패링 기능 |
| `CharacterNotifyRunner` | Notify 발동 시점과 실행 중 Handle의 시작·갱신·종료 관리 |

## 핵심 설계

### 데이터가 전투 흐름을 소유한다

`AnimationConfig`는 ScriptableObject 에셋이다. 콤보, 회피, 패링과 피격은 서로 다른 상태 클래스가 아니라 Section과 Link의 조합으로 표현한다. 새로운 행동은 우선 데이터로 만들고, 새로운 종류의 조건이나 기능이 필요할 때만 `LinkCondition` 또는 `SectionModule` 구현을 추가한다.

### 하나의 실행기를 공유한다

`CharacterActionRunner`는 MonoBehaviour가 아닌 공용 C# 실행기다. 플레이어와 몬스터는 입력 방식과 이동 구현만 각자의 인터페이스로 제공한다. 전이 평가와 Module 생명주기는 같은 코드 경로를 사용하며, Notify 실행은 내부의 `CharacterNotifyRunner`에 위임한다.

`CharacterNotifyRunner`는 Notify의 발동 시점과 실행 중 Handle만 관리한다. 실제 이펙트 생성, 사운드 재생, 카메라 피드백과 타격 판정은 각각 `EffectService`, `AudioService`, `CameraFeedbackService`, `HitService`가 담당하므로 Notify 타임라인과 기능 구현의 경계가 유지된다.

### Animator는 재생에 집중한다

Animator 파라미터와 전이 그래프를 사용하지 않는다. `CharacterAnimatorBridge`가 `CrossFadeInFixedTime`으로 클립을 재생하고, 전이 조건과 타이밍은 `CharacterActionRunner`가 관리한다. 전투 규칙이 Animator와 코드 양쪽에 나뉘지 않아 실행 흐름을 한곳에서 확인할 수 있다.

### 저장 단위와 편집 단위를 분리한다

전이 윈도우, Notify와 Module 구간은 `normalizedTime`으로 저장한다. 클립 길이와 FPS가 바뀌어도 상대 위치를 유지하기 위해서다. 제작 도구에서는 이를 프레임으로 변환해 표시해 애니메이션 타이밍을 직관적으로 편집한다.

### 확장 지점을 명시한다

- `LinkCondition`: 입력, 거리, 방향과 상태처럼 전이 가능 여부를 판단한다.
- `SectionModule`: 일정 구간 동안 이동·회전·무적·패링 같은 동작을 적용한다.
- `NotifyPayload`: 타격, 이펙트, 사운드와 카메라처럼 특정 시점의 외부 요청을 전달한다.

이 타입들은 `[SerializeReference]`로 직렬화된다. 공유 실행기에서 구체 타입을 나열하는 분기문 대신 각 확장 타입이 자신의 책임을 수행한다.

## 이동과 전투 판정

- `MoveMode.RootMotion`은 Animator 델타를 Motor가 받아 `CharacterController`에 적용한다.
- `TargetWarpModule`과 `FaceTargetModule`이 공격 거리와 방향을 독립적으로 보정한다.
- `IFrameModule`은 회피 구간의 피격 무시를, `ParryModule`은 공격을 쳐내는 구간을 표현한다.
- Hit Notify는 근접, 구, 부채꼴, 박스, 캡슐과 Sweep 판정을 같은 `HitService`에 요청한다.
- 실행 중인 이펙트의 `BindingKey`를 Hit 원점으로 사용해 이동하는 빔과 파티클 판정을 동기화할 수 있다.

## 입력과 방어 흐름

- 입력 버퍼는 전이 윈도우가 열릴 때 조건에 맞는 입력을 소비한다.
- 회피와 패링처럼 즉시 반응해야 하는 행동은 현재 Config를 인터럽트하는 push 방식으로 진입한다.
- 회피 성공은 입력 순간이 아니라 경고를 보낸 공격자의 실제 공격 시점에 확정한다.
- 패링 성공은 `ParryModule` 활성 중 실제 Hit가 쳐냄으로 분기됐을 때만 발행한다.

## 에디터 툴 연동

`AnimationConfigTool`은 런타임 데이터 모델을 그대로 편집한다.

- Section, Link, Notify와 Module을 한 타임라인에 배치한다.
- 콤보, 루트 모션과 이펙트를 미리 재생한다.
- Scene View에서 Hit 모양, 크기와 원점을 편집한다.
- Play Mode에서 현재 Config, Section, 입력 상태와 판정 범위를 확인한다.

`EffectTool`과 공용 프리뷰 코드를 사용하므로 애니메이션 위에서 이펙트 시점과 위치를 함께 조정할 수 있다.

## 트레이드오프

| 장점 | 비용 |
|---|---|
| 공격을 데이터 조합으로 추가할 수 있다 | `CharacterActionRunner`와 `CharacterNotifyRunner` 사이의 실행 순서를 유지해야 한다 |
| 플레이어와 몬스터가 실행 코드를 공유한다 | 각 소비자가 공용 인터페이스 경계를 지켜야 한다 |
| 타이밍과 전이를 한 도구에서 확인한다 | 잘못된 Section, Window와 참조를 에디터에서 검증해야 한다 |
| 다형성 확장이 쉽다 | `[SerializeReference]` 타입 이름 변경 시 마이그레이션이 필요하다 |

중앙 실행기가 비대해지지 않도록 입력 판단은 Condition과 Trigger, 구간 동작은 Module, 외부 연출의 시간과 생명주기는 `CharacterNotifyRunner`에 유지한다.

## 관련 문서

- [이펙트 구현 요약](EffectArchitecture.md)
- [전투 피드백 구현 요약](CombatFeedbackArchitecture.md)
- [플레이어 런타임 및 스쿼드 구조](PlayerRuntimeArchitecture.md)
- [주요 설계 결정](자료구조_선택.md)
