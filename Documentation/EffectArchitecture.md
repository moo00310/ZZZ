# 이펙트 구현 요약

여러 원시 이펙트를 `CompositeEffect` 하나로 조합해 재생하고, 실제 인스턴스는 프리팹별 `EffectPool`에서 재사용한다. 조합 단위의 제작 편의성과 원시 에셋 단위의 메모리 관리를 분리한 구조다.

## 해결하려는 문제

- 하나의 공격 연출은 섬광, 파티클, 트레일처럼 여러 프리팹을 서로 다른 시점과 소켓에서 재생한다.
- 조합마다 프리팹을 복제하면 설정과 메모리가 중복된다.
- 풀링된 오브젝트는 재생이 끝날 때 상태를 복원하고 정확한 소유 풀로 돌아가야 한다.
- 이동하는 이펙트와 타격 판정의 원점 및 생명주기를 맞춰야 한다.

## 전체 흐름

    AnimationConfig Effect Notify
                  │
                  ▼
       CharacterNotifyRunner
                  │
                  ▼
          CompositeEffect
          ├─ Entry A: Prefab · Delay · Socket · Offset
          ├─ Entry B: Prefab · Delay · Socket · Offset
          └─ Entry C: Prefab · Delay · Socket · Offset
                  │
                  ▼
            EffectService
                  │
                  ▼
       프리팹별 EffectPool → PooledEffectHandle → 자동 반납

| 구성 요소 | 역할 |
|---|---|
| `CompositeEffect` | 여러 Entry의 재생 순서와 조합 설정을 저장 |
| `CompositeEffectEntry` | 프리팹, 지연, 소켓, 위치와 재생 옵션을 정의 |
| `CharacterNotifyRunner` | Effect Notify의 발동 시점과 실행 중 Handle의 생명주기 관리 |
| `EffectService` | 재생 요청을 받고 프리팹별 풀을 조회·생성 |
| `EffectPool` | 원시 프리팹 인스턴스를 대여하고 회수 |
| `PooledEffectHandle` | 재생 상태, 정지와 원래 풀로의 반환을 관리 |
| `BindingKey` | 실행 중인 실제 이펙트 Transform을 Hit 원점으로 연결 |

## 핵심 설계

### 실행은 조합 단위, 풀링은 프리팹 단위

게임 로직은 `CompositeEffect` 하나만 요청한다. `EffectService`는 내부 Entry를 펼쳐 각 프리팹의 풀에서 인스턴스를 가져온다. 같은 불꽃 프리팹을 여러 조합에서 사용해도 풀과 원본 설정은 공유한다.

### 재생 설정과 런타임 상태를 분리한다

Entry와 Module은 직렬화 가능한 설정만 보유한다. 시간, Transform과 이전 프레임 값 같은 실행 상태는 인스턴스의 `EffectModuleRunner`가 재생마다 새로 만든다. 풀 인스턴스를 다시 사용해도 이전 재생 상태가 설정 에셋에 남지 않는다.

### 반납 책임을 핸들에 모은다

`PooledEffectHandle`은 고정 수명, 파티클 종료 또는 명시적 정지에 따라 인스턴스를 풀에 반환한다. 반환 전에는 파티클, Transform, 머티리얼 오버라이드와 Module 상태를 기준값으로 복원한다. 조합은 재생을 요청하지만 실제 풀 소유권과 회수는 서비스와 핸들이 담당한다.

### 상태 전환 수명을 데이터로 결정한다

Effect Notify는 전환 시 동작을 선택한다.

| 정책 | 의미 |
|---|---|
| `Keep` | 현재 재생을 자체 종료 시점까지 유지 |
| `Stop` | 현재 Section을 벗어날 때 정지하며, 지정한 Carry Section으로 전이하면 그 Section까지 유지 |
| `Next` | 지정한 전이 목적 Section에 진입한 뒤 생성 |

Config 인터럽트나 캐릭터 교체 시에는 실행 중인 핸들을 정리해 풀 밖에 인스턴스가 남지 않게 한다.

## Entry Module

`CompositeEffectEntry.Modules`는 조합마다 다른 움직임과 표현을 추가한다.

- 위치: 원호 이동과 좌표계 전환
- 회전: 진행 방향 바라보기와 축 보정
- 파티클: 재생 시간, 속도, 수명, 크기와 색상
- 렌더링: 머티리얼 교체와 `MaterialPropertyBlock` 값

Module 실행 순서는 위치 → 회전 → 시뮬레이션 채널로 고정한다. 목록 순서에 따라 결과가 달라지지 않으며, 에디터 프리뷰와 런타임은 같은 평가 코드를 사용한다.

## Hit 원점과 생명주기 연동

Entry에 `BindingKey`를 지정하면 `EffectService`가 실행 중인 실제 풀 인스턴스의 Transform을 캐릭터별 범위에 등록한다. Hit Notify가 같은 `EffectKey`를 사용하면 캐릭터 루트 대신 이동 중인 빔이나 파티클을 원점으로 판정한다.

판정과 이펙트 수명을 완전히 묶어야 할 때는 `Sync Hit With Effect`를 사용한다.

    Effect 생성 → Binding 등록 → Hit 시작
           │
           └─ Effect 정지 또는 풀 반납 → Hit 종료

이 방식은 별도 콜라이더 프리팹 없이 시각적 이펙트와 논리 판정을 동기화한다. 단순 공격은 Hit Notify가 데이터를 소유하고 이펙트 Transform만 참조하도록 분리할 수 있다.

## 사운드와의 경계

이펙트 시스템은 시각 연출만 관리한다. 사운드는 `CompositeSound`와 `AudioService`가 별도로 재생한다. 애니메이션 동작음은 Sound Notify가 요청하고, 실제 충돌음은 `HitFeedbackProfile`이 적중 결과에 따라 선택한다.

## 에디터 툴

- `EffectTool`: Composite Entry, 지연, 소켓, 위치와 Module을 편집하고 독립적으로 미리 재생한다.
- `AnimationConfigTool`: 애니메이션 타임라인 위에서 Effect Notify의 시점과 위치를 조정한다.
- 두 도구는 공용 편집·프리뷰 로직을 사용해 저장 데이터와 런타임 결과의 차이를 줄인다.

## 트레이드오프

| 장점 | 비용 |
|---|---|
| 원시 프리팹과 풀을 여러 조합이 공유한다 | 풀 용량과 조합별 동시 사용량을 함께 고려해야 한다 |
| 조합 데이터만으로 복합 연출을 제작한다 | Entry와 Module 조합이 많아지면 에디터 검증이 필요하다 |
| 핸들이 반납과 상태 복원을 책임진다 | 모든 종료 경로가 핸들을 거쳐야 한다 |
| Hit와 이동 이펙트를 동기화할 수 있다 | `BindingKey` 불일치를 저장 전에 검증해야 한다 |

Addressables를 적용할 때는 풀에 인스턴스가 남아 있는 동안 원본 에셋 핸들을 유지하고, 풀을 비운 뒤 Addressables 핸들을 해제해야 한다.

## 관련 문서

- [전투 애니메이션 구현 요약](AnimationArchitecture.md)
- [전투 피드백 구현 요약](CombatFeedbackArchitecture.md)
- [주요 설계 결정](자료구조_선택.md)
- [개발 작업 목록과 로드맵](TODO.md)
