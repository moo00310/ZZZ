# 전투 성공 피드백 구조

패링과 퍼펙트 회피 성공 판정, 히트랙 재생, 디버그 확인 방법을 한곳에 정리한다.

## 책임 경계

| 구성 요소 | 책임 |
|---|---|
| `Hit Notify(Action = ParryWarning)` | 데미지 없이 경고 오버랩을 검사하고 공격자별 경고 대상을 등록 |
| `PlayerActionController` | 경고 윈도우, 회피 후보, 패링/퍼펙트 회피 성공 이벤트 관리 |
| `HitService` | 실제 데미지 Hit 처리 및 경고 대상의 공격 시점 확정 |
| `PlayerRuntime` | 활성 캐릭터의 성공 이벤트 구독 및 패링/회피별 히트랙 설정 소유 |
| `HitStopService` | 전체 게임 속도와 공격 몬스터의 로컬 속도 곡선 재생·복구 |
| `PlayerStateHUD` | 경고 수신, 회피 후보, 성공 여부와 누적 횟수 표시 |

`PlayerActionController`는 성공 여부만 발행한다. 연출 수치와 재생 정책은 씬의 `PlayerRuntime`이 소유한다.

## 퍼펙트 회피 판정

```text
ParryWarning Hit Notify
  → 경고 오버랩 안의 플레이어가 IncomingAttack 상태 진입
  → 경고 중 회피 입력 시 Perfect Dodge 후보 등록
  → 같은 공격자의 실제 Damage Hit Notify 실행
      ├─ 데미지 오버랩이 닿음: 공격 무시 후 성공
      └─ 회피 이동으로 빗나감: 등록된 경고 대상으로 성공 확인
  → PerfectDodgeSucceeded(source)
  → PlayerRuntime이 퍼펙트 회피 히트랙 요청
```

성공은 회피 입력 순간이 아니라 실제 공격 Notify 시점에 확정한다. 다만 짧은 i-frame이나 데미지 오버랩과 정확히 겹치도록 강제하지 않는다. 경고를 받았고 해당 공격 시점까지 회피 상태인지를 사용한다.

방향 입력이 있으면 `Evade_Left/Right`, 중립이면 `Evade_Back`을 재생한다. 어느 경우든 경고 중 시작한 회피는 후보가 될 수 있다. 일반 회피의 피격 무시는 `IFrameModule` 구간만 사용한다.

Durahan의 `Attack_01_01`, `Attack_01_03`은 현재 다음 값을 사용한다.

- 경고 오버랩: 캐릭터 루트 기준 반경 4m
- 경고 입력 윈도우: 0.45초
- 경고 만료 보정: 0.15초
- 실제 성공 확정: 같은 공격자의 다음 데미지 Hit Notify

## 패링 성공 판정

```text
ParryWarning → IncomingStrength/source 저장
ParryModule 활성 중 Damage Hit 수신
  → HitTrigger.TryDeflect
  → ParryAid_L/H 진입
  → ParrySucceeded(HitContext)
  → PlayerRuntime이 패링 히트랙 요청
```

실제 Hit가 쳐냄으로 분기된 경우에만 성공 이벤트를 발행한다. `FaceOppositeTargetModule`은 성공 진입 시 플레이어 Look을 공격 몬스터 Look의 반대 방향으로 맞춘다.

## 히트랙 곡선

`PlayerRuntime > Success Hit Lag`에서 패링과 퍼펙트 회피를 각각 설정한다.

- `Duration`: 곡선 전체를 재생할 실제 시간
- `Game Speed Curve`: X는 정규화 진행도, Y는 실제 `Time.timeScale`
- `Monster Speed Curve`: X는 정규화 진행도, Y는 공격 몬스터의 추가 로컬 속도 배율

Monster Speed는 Game Speed에 곱해진다. 예를 들어 같은 시점에 Game이 `0.2`, Monster가 `0.1`이면 해당 몬스터의 체감 진행 배율은 `0.02`다. 곡선의 음수 값은 0으로 제한하며 Duration 종료 시 요청 전 속도로 복구한다. 부드럽게 끝내려면 마지막 키를 `Y = 1`로 둔다.

몬스터 로컬 속도는 애니메이터, Config 타임라인, AI 판단 타이머, 모터 회전에 함께 적용한다. 전체 히트랙 시간은 `realtimeSinceStartup`으로 계산하므로 Game Speed가 0이어도 복구된다.

## HUD 진단

개발 HUD는 F1로 표시를 전환한다.

| HUD 상태 | 의미 |
|---|---|
| `Atk Window` 활성 | 경고 오버랩이 플레이어에게 도달함 |
| `Perfect Dodge: CANDIDATE` | 경고 중 회피가 후보로 등록됨 |
| `Perfect Dodge: SUCCESS xN` | 실제 공격 시점에 성공이 확정됨 |

진단 순서는 다음과 같다.

1. `Atk Window`가 없으면 경고 Notify 시점·원점·반경·Target Mask를 확인한다.
2. `Atk Window`만 있고 Candidate가 없으면 회피 입력 처리와 회피 Config 진입을 확인한다.
3. Candidate 이후 Success가 없으면 같은 공격자의 데미지 Hit Notify 실행 여부와 Warning Duration을 확인한다.
4. Success는 보이지만 히트랙이 약하면 `PlayerRuntime`의 두 속도 곡선 시작 키를 확인한다.

Animation Tool에서 경고 Hit Notify를 선택하면 Scene View에 오버랩 범위를 표시할 수 있다. 런타임에서는 `MonsterActionController.Show Hit Gizmos`와 Game View의 Gizmos를 함께 켠다.
