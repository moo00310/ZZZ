# ZZZ 전투 애니메이션 데모

Unity 6와 URP로 제작 중인 3인칭 액션 전투 프로젝트입니다. Burnice 캐릭터의 공격, 콤보, 회피, 패링, 피격과 전투 피드백을 데이터 기반으로 구성합니다.

Animator Controller에 전투 전이 그래프를 만들지 않고, `AnimationConfig`가 전투 흐름을 저장하며 `CharacterActionRunner`가 이를 실행합니다. Animator는 `CharacterAnimatorBridge`의 `CrossFade`를 통한 클립 재생에 집중합니다.

## 핵심 구현 기능

### 데이터 기반 전투 애니메이션

<!-- GIF 추가 예정: ![데이터 기반 전투 애니메이션](Documentation/Images/combat-animation.gif) -->

- `AnimationConfig`에 애니메이션 구간, 전이, Notify와 구간별 기능을 저장합니다.
- `CharacterActionRunner`가 Section 전환, Link 조건 평가와 Module 실행을 담당하며 플레이어와 몬스터가 같은 실행 흐름을 공유합니다.
- `CharacterNotifyRunner`가 Notify 발동 시점과 실행 중인 이펙트·사운드·타격 Handle의 생명주기를 관리합니다.
- `LinkCondition`과 `SectionModule`을 조합해 콤보, 강화 공격, 회피, 패링, 루트 모션과 타깃 보정을 확장합니다.
- 실제 연출과 판정은 `EffectService`, `AudioService`, `CameraFeedbackService`, `HitService`에 위임해 전투 흐름과 기능 구현의 책임을 분리했습니다.

```text
입력과 게임 상태
       │
       ▼
AnimationConfig ── Section · Link · Notify · Module
       │
       ▼
CharacterActionRunner ── Section · Link · Module 실행
       │
       ├────────── CharacterAnimatorBridge: CrossFade 재생
       ├────────── AgentMotor / MonsterMotor: 이동과 회전
       └────────── CharacterNotifyRunner: Notify 시간과 생명주기
                              │
                              ├── EffectService / AudioService: 연출
                              ├── CameraFeedbackService: 카메라 피드백
                              └── HitService: 판정과 피격 전달
```

[구현 요약: 전투 애니메이션](Documentation/AnimationArchitecture.md) · [주요 설계 결정](Documentation/자료구조_선택.md)

### 전투 데이터 제작 도구

<!-- GIF 추가 예정: ![AnimationConfigTool 편집 과정](Documentation/Images/animation-tool.gif) -->

- `AnimationConfigTool`에서 Section, Link, Notify와 Module을 하나의 타임라인으로 편집합니다.
- 콤보와 루트 모션을 미리 재생하고, 플레이 중인 Config와 입력 상태를 확인할 수 있습니다.
- Hit Payload의 모양, 크기와 원점을 Scene View에서 편집하고 Game View 디버그 라인으로 검증합니다.
- `EffectTool`에서 여러 프리팹으로 구성된 `CompositeEffect`를 편집하고 애니메이션과 함께 미리 재생합니다.

[구현 요약: 전투 데이터 제작 도구](Documentation/AnimationArchitecture.md#에디터-툴-연동) · [EffectTool 구조](Documentation/EffectArchitecture.md#에디터-툴)

### 조합형 이펙트와 전투 피드백

<!-- GIF 추가 예정: ![이펙트와 전투 피드백](Documentation/Images/combat-feedback.gif) -->

- 여러 원시 이펙트를 `CompositeEffect`로 조합하고, 풀링은 프리팹 단위로 분리합니다.
- `EffectService`와 `EffectPool`이 지연 재생, 소켓 추종, 상태 전환과 자동 반납을 관리합니다.
- 피격 결과와 공격 강도에 따라 `HitFeedbackProfile`이 이펙트와 타격음을 선택합니다.
- `AudioService`가 `CompositeSound`의 클립 변형을 선택하고 3D AudioSource voice를 재사용합니다.
- 패링과 퍼펙트 회피 성공에 히트스톱, 카메라 Shake·Shot과 공격 경고 UI를 연결합니다.

```text
HitService → IHittable.ReceiveHit
    ├─ Ignored  → 피드백 없음
    ├─ Parried  ┐
    └─ Accepted ┴→ HitFeedbackService
                    └─ HitFeedbackProfile
                        ├─ CompositeEffect → EffectService → EffectPool
                        └─ CompositeSound  → AudioService
```

[구현 요약: 이펙트 구조](Documentation/EffectArchitecture.md) · [전투 피드백 구조](Documentation/CombatFeedbackArchitecture.md)

### 플레이어 런타임과 캐릭터 교체

<!-- GIF 추가 예정: ![캐릭터 교체](Documentation/Images/character-switch.gif) -->

- 입력 장치와 카메라는 씬의 공용 `PlayerRuntime`이 한 번만 소유합니다.
- `SquadController`가 캐릭터 프리팹 생성, 활성 캐릭터 교체와 입력 타깃 이전을 담당합니다.
- 캐릭터별 애니메이션 설정, 이동, 자원과 전투 상태는 각 프리팹 안에 독립적으로 유지합니다.
- 캐릭터 교체 시 실행 중인 Config와 이펙트를 정리한 뒤 위치, 입력과 카메라 타깃을 안전하게 이전합니다.

[구현 요약: 플레이어 런타임 및 스쿼드 구조](Documentation/PlayerRuntimeArchitecture.md)

## 기술 스택 및 개발 환경

- Unity `6000.3.16f1`
- Universal Render Pipeline `17.3.0`
- Input System `1.19.0`
- Cinemachine `3.1.4`
- Unity Test Framework `1.6.0`

## 실행 방법

1. Unity Hub에서 프로젝트를 Unity `6000.3.16f1`로 엽니다.
2. `Assets/99.Scenes/SampleScene.unity` 씬을 엽니다.
3. Play Mode를 실행해 전투 기능을 확인합니다.

## 추가 문서

- [강화 공격 상태 제어](Documentation/EnhanceStateControl.md)
- [Burnice 애니메이션 리소스 교체 기록](Documentation/BurniceAnimationResourceMigration.md)
- [코딩 컨벤션](Documentation/CodingConventions.md)
- [개발 작업 목록과 로드맵](Documentation/TODO.md)

## 향후 개선 계획

- 적 AI와 체력·경직·사망을 연결해 짧은 전투 수직 단면 완성
- 전이, 타격 판정과 이펙트 수명주기 EditMode 테스트 보강
- 대표 전투 및 에디터 도구 GIF 추가
- Addressables를 이용한 VFX·캐릭터 비동기 로드와 해제, 메모리 전후 측정
- Android 실기 빌드와 Profiler 기반 성능 검증
