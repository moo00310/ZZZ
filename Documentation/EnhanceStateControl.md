# 강화 공격 상태 제어

2026-07-29 기준 강화 공격에서 사용하는 입력 차단, 회전 소유권, 이펙트 상태 전환을 정리한다.

## 입력 차단

`InputBlockModule`은 지정한 normalized time 구간에서 특정 `ComboInput`을 차단한다.
`AgentActionController`는 일반 강화 공격 트리거를 실행하기 전에 현재 섹션의
`CharacterActionRunner.ActiveSectionBlocks()`를 확인한다. 링크가 입력을 소비하는 것과 별개로,
현재 동작 중 같은 입력이 전역 트리거로 빠지는 상황을 막을 때 사용한다.

- `Input = Any`: 모든 공격 입력 차단
- 그 외 값: 지정한 입력만 차단
- 현재 `Attack_ExSpecial_01_Explode`는 전체 구간에서 `Enhance`를 차단한다.

## 회전 모듈

| 모듈 | 역할 |
|---|---|
| `RotationLockModule` | WASD 등 일반 이동 입력에 의한 캐릭터 회전을 막는다. 루트 모션 회전에는 관여하지 않는다. |
| `RootRotationKillModule` | `OnAnimatorMove`에서 `deltaRotation`을 버린다. 위치 루트 모션은 유지한다. |
| `SectionTurnModule` | `Root`와 `Bip001` 중 실제 회전 델타가 나오는 본을 선택해 지정 `Source Axis` twist를 최상위 캐릭터의 월드 yaw로 적용한다. 넘긴 누적 yaw만큼 `Bip001`을 역보정해 모델의 이중 회전을 막는다. `Rotation Scale`과 `Target Angle`로 적용량을 제어한다. |
| `FaceInputModule` | 진입 시 입력 방향을 바라본다. `Follow Input`이 켜지면 매 프레임 입력 방향을 따른다. |
| `FaceViewModule` | 진입 순간 카메라 정면을 저장하고 해당 방향을 유지한다. 카메라를 계속 추적하지 않는다. |

`Attack_Normal_Enhance_01/02/03`은 최종적으로 `FaceInputModule`, `FaceTargetModule`,
`FaceViewModule`을 사용하지 않는다. `RotationLockModule`만으로 E 진입 당시 캐릭터 방향을
유지하므로, E 사용 중 카메라 위치나 방향을 바꿔도 캐릭터가 따라 회전하지 않는다.

`Attack_Normal_Enhance_Back`은 `RootRotationKillModule`을 사용한다. 루트 회전은 제거하지만
`AdditionalMovementModule`의 후방 이동은 유지한다. 섹션 이탈 시 `CharacterActionRunner`가
`KillRootRotation`을 초기화하므로 다음 상태로 회전 잠금이 누수되지 않는다.

`Run`의 TurnBack은 `SectionTurnModule`의 목표 각도를 180도로 설정한다. 적용 중에는 누적 회전의
상한으로 사용하고, 윈도우가 끝나면 검출된 회전 방향의 정확한 180도로 마무리한다. 원본 회전량의
비율 조절은 `Rotation Scale`을 사용한다. `Run_Loop` 링크는 회전 포즈가 다시 블렌딩되지 않도록
`BlendDuration=0`을 사용한다.

## 이펙트 상태 전환

Effect Notify의 기본 전환 정책은 `Keep`이다.

| 정책 | 동작 |
|---|---|
| `Keep` | 상태 소유권 없이 생성하고 이펙트 자체 수명이 끝날 때까지 유지한다. |
| `Stop` | 현재 섹션을 실제로 이탈할 때 정지한다. `Carry Section`을 지정하면 해당 목적지로 소유권을 넘기고 목적지 이탈 시 정지한다. |
| `Next` | Notify 시점에는 생성하지 않는다. 실제 링크 목적지가 `Next Section`과 일치할 때 목적지 섹션에서 생성한다. |

동일 섹션 self-link는 실제 섹션 이탈로 취급하지 않는다. `CharacterActionRunner`가 동일 섹션으로
재진입하면 `CharacterNotifyRunner`는 진행 중인 `Stop`/`Next` 상태와 아직 목적지가 확정되지 않은
`Next` 예약을 유지한다. 따라서
루프마다 이펙트가 중복 생성되거나 조기에 정지하지 않는다.

`Next`는 목적지 분기를 먼저 확인한 뒤 생성하므로 잘못된 분기에서 이펙트가 한 프레임
노출되지 않는다. 강화 공격에서 회피 후 중간 프레임으로 복귀하는 경우 Notify의
`Next Section`과 실제 링크 목적지를 반드시 함께 맞춘다.

자세한 이펙트 풀링 및 `BakeToWorldEffectModule` 동작은
[EffectArchitecture.md](EffectArchitecture.md)를 참고한다.
