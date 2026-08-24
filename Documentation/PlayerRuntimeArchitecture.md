# 플레이어 런타임 및 스쿼드 구조

## 목적

캐릭터 프리팹이 입력 장치와 카메라를 직접 소유하지 않도록 분리한다.
씬에는 공용 `PlayerRuntime`을 하나만 두고, 실제 조작 캐릭터는 `SquadController`가 생성하고 교체한다.

```text
PlayerRuntime (scene root)
├── PlayerInput
├── PlayerInputRouter
├── HitStopController
└── SquadController
    ├── 활성 캐릭터의 PlayerActionController로 입력 전달
    └── 활성 캐릭터의 CameraPoint로 카메라 타깃 변경

PlayableCharacter 프리팹
├── PlayerMotor
├── PlayerActionController
└── CameraPoint
```

## 책임 분리

### PlayerRuntime 오브젝트

- `PlayerInput`: 입력 장치와 액션 맵을 한 번만 소유한다.
- `PlayerInputRouter`: 입력을 현재 `IPlayerInputTarget`으로 전달한다.
- `HitStopController`: 활성 캐릭터의 성공 이벤트를 구독하고 패링·퍼펙트 회피별 히트스톱을 요청한다.
- `SquadController`: 캐릭터 명단, 활성 인덱스, 생성 및 교체를 관리한다.

`PlayerRuntime` 오브젝트는 캐릭터 모델이나 전투 상태를 소유하지 않는다.

### PlayableCharacter

캐릭터 프리팹에 붙는 파사드다. 해당 프리팹의 `PlayerActionController`와
카메라가 바라볼 `CameraPoint`를 `SquadController`에 제공한다.

캐릭터별 애니메이션 설정, 이동, 자원, 전투 상태는 각 프리팹 내부에 유지한다.

## 캐릭터 교체 순서

`Previous` 또는 `Next` 액션이 들어오면 다음 순서로 교체한다.

1. 현재 입력 타깃을 해제하고 남은 입력을 초기화한다.
2. 이전 캐릭터의 `ConfigState.Exit()`을 호출해 상태 플래그와 실행 중인 이펙트를 정리한다.
3. 이전 캐릭터를 비활성화한다.
4. 이전 캐릭터의 월드 위치를 새 캐릭터에 전달한다.
5. 새 캐릭터를 활성화하고 기본 `AnimationConfig`를 시작한다.
6. 입력 타깃을 새 `PlayerActionController`로 변경한다.
7. TPS 카메라 타깃을 새 캐릭터의 `CameraPoint`로 변경한다.

현재 교체 데이터는 위치만 공유한다. 캐릭터별 회전, 자원, 콤보 상태는 각 인스턴스가
독립적으로 보관하며 새 캐릭터는 기본 상태에서 시작한다.

## 새 캐릭터 추가

1. 캐릭터 루트에 `PlayerMotor`, `PlayerActionController`와 필수 의존 컴포넌트를 구성한다.
2. 루트에 `PlayableCharacter`를 추가한다.
3. `Action Controller`에 루트의 `PlayerActionController`를 연결한다.
4. 프리팹 하위에 `CameraPoint`를 만들고 `Camera Point`에 연결한다.
5. 캐릭터를 프리팹으로 저장한다.
6. 씬의 `PlayerRuntime > SquadController > Character Prefabs` 목록에 프리팹을 추가한다.

캐릭터 프리팹에는 `PlayerInput`, `PlayerInputRouter`, 메인 카메라를 넣지 않는다.
씬에 캐릭터 인스턴스를 별도로 배치하지 않아도 `SquadController`가 시작 시 생성한다.

## 현재 범위

일반 교체는 한 캐릭터만 활성화한다. 퀵 어시스트처럼 두 캐릭터가 잠시 동시에 등장하는 연출은
위치 전달과 입력 소유권 전환은 재사용하되, 기존 캐릭터를 즉시 비활성화하지 않는 별도 교체 모드로 확장한다.
