# 전투 피드백 구조

공격 경고, 패링과 퍼펙트 회피 성공 판정, 히트랙, 카메라 피드백과 디버그 확인 방법을 한곳에 정리한다.

## 책임 경계

| 구성 요소 | 책임 |
|---|---|
| `Hit Notify(Action = ParryWarning)` | 데미지 없이 경고 오버랩을 검사하고 공격자별 경고 대상을 등록 |
| `PlayerActionController` | 경고 윈도우, 회피 후보, 패링/퍼펙트 회피 성공 이벤트 관리 |
| `HitService` | 실제 데미지 Hit 처리 및 경고 대상의 공격 시점 확정 |
| `HitFeedbackReceiver` | 피격 대상의 `HitFeedbackProfile` 제공 및 Composite의 이펙트 풀 소유권 관리 |
| `HitFeedbackService` | HitResult와 공격 강도로 CompositeEffect·CompositeSound를 조회해 각각 재생 |
| `AudioService` | `CompositeSound` 하나를 실행하고 3D AudioSource voice를 재사용 |
| `HitStopController` | 활성 캐릭터의 패링·퍼펙트 회피 성공 이벤트 구독 및 성공별 히트스톱 설정 소유 |
| `HitStopService` | 전체 게임 속도와 공격 몬스터의 로컬 속도 곡선 재생·복구 |
| `AttackWarningCrossEffect` | 몬스터 얼굴의 월드 위치를 화면 좌표로 바꿔 4방향 경고선을 UI로 재생 |
| `CameraFeedbackService` | Camera Notify와 현재 카메라 수신자를 분리하는 공용 런타임 진입점 |
| `TPSCameraController` | 기본 TPS 추적·충돌 위에 Shot을 합성하고 마지막에 Shake를 적용 |
| `PlayerStateHUD` | 경고 수신, 회피 후보, 성공 여부와 누적 횟수 표시 |

`PlayerActionController`는 성공 여부만 발행한다. 패링·퍼펙트 회피별 히트스톱 수치와 재생 정책은 씬의
`HitStopController`가 소유한다.

## 일반 피격 이펙트와 타격음

`HitService`는 판정 중심에서 피격 Collider의 `ClosestPoint`를 구해 `HitContext.HitPoint`로 전달한다.
`Ignored`가 아닌 결과만 `HitFeedbackService`로 전달하므로 플레이어와 몬스터가 같은 경로를 사용하고,
무적·퍼펙트 회피로 무시한 공격은 연출을 재생하지 않는다.

```text
HitService → IHittable.ReceiveHit
    ├─ Ignored  → 연출 없음
    ├─ Parried  ┐
    └─ Accepted ┴→ HitFeedbackService
                    └─ 대상 HitFeedbackReceiver
                        └─ HitFeedbackProfile
                           (HitResult + AttackStrength)
                            ├─ CompositeEffect → EffectService
                            │   └─ VFX Entries
                            └─ CompositeSound → AudioService
                                └─ Clip Variant + Playback Settings
```

공격자는 `HitData.Strength`(`Light`/`Heavy`)과 판정 결과를 전달한다. 맞는 대상의
`HitFeedbackReceiver`가 결과·강도 조합에 대응하는 `CompositeEffect`와 `CompositeSound`를 선택하므로,
패링과 일반 피격 모두 같은 조회 경로를 사용한다. VFX의 표면 보정은 프로필이 아니라
CompositeEffect Entry의 `PositionOffset`에 저장한다. CompositeSound는 별도로 실제 `HitPoint`에서
재생되므로 VFX 표면 보정이 타격음 위치에 섞이지 않는다.

피격 가능한 플레이어와 몬스터의 루트에는 `HitFeedbackReceiver`를 추가하고 대상에 맞는
`HitFeedbackProfile` 하나를 연결한다. Profile Entry는 `HitResult`, `AttackStrength`,
`CompositeEffect`, `CompositeSound`를 가진다.

휘두름·발사처럼 적중 여부와 무관한 소리는 Animation Tool의 `Sound` Notify가 담당한다.
Sound Notify는 `CompositeSound`를 직접 참조한다. 실제 충돌음은 `HitFeedbackProfile`이 선택한
`CompositeSound`에 넣어 Hit가 빗나가거나 무시됐을 때 재생되지 않게 한다.

## 몬스터 공격 경고 이펙트

Durahan 공격은 `Hit Notify(Action = ParryWarning)`와 같은 시점에 별도의 잠긴 Effect Notify를 둔다.
Effect Notify는 `Cmp_AttackWarningCross`를 재생하며, Composite Entry가 몬스터의 `Bip001 Head`를 따라간다.
따라서 판정 경고와 화면 연출의 타이밍은 같지만, 판정 로직이 UI 구현을 직접 알지는 않는다.

```text
ParryWarning Hit Notify ── 플레이어 경고 오버랩 등록
같은 시점 Effect Notify ── EffectService.Play(Cmp_AttackWarningCross)
                              └─ Bip001 Head 추종
                                  └─ WorldToScreenPoint
                                      └─ 화면 중심점에서 상·하·좌·우 Ray 확장
```

`AttackWarningCrossEffect`는 Screen Space Overlay Canvas의 RawImage 네 개를 사용한다. 한 개의 선 텍스처를
0/90/180/270도로 회전하고, 몬스터 얼굴의 화면 좌표부터 각 화면 끝까지 길이를 개별 계산한다. 화면 밖이나
카메라 뒤에 있는 몬스터는 숨긴다. 색은 `ZZZ/UI/Attack Warning Additive` 셰이더의 가산 합성으로 표현한다.

| 설정 | 의미 |
|---|---|
| `Duration` | 전체 재생 시간. 프리팹과 Composite Fixed Lifetime은 같은 값으로 맞춘다 |
| `Alpha Over Lifetime` | 정규화 시간에 따른 전체 투명도 |
| `Thickness Over Lifetime` | 굵은 시작선이 얇아지는 정도 |
| `Length Over Lifetime` | 얼굴에서 화면 가장자리로 Ray가 뻗는 진행도 |
| `Maximum/Minimum Thickness` | Ray 두께 범위(픽셀) |
| `Center Overlap` | 네 Ray가 중앙에서 갈라져 보이지 않도록 겹치는 길이 |
| `Edge Padding` | 해상도 가장자리 바깥까지 덮는 추가 길이 |

에셋을 다시 만들거나 Durahan 경고 Notify에 연결할 때는
`ZZZ > Effects > Create Attack Warning Cross Assets`를 사용한다. 빌더는 일부 에셋만 존재하면 덮어쓰지 않고
중단하며, 전체 세트가 있으면 기존 Composite를 재사용하고 누락된 경고 연결만 추가한다.

## 카메라 피드백

Camera Notify는 카메라 컴포넌트를 직접 참조하지 않는다. `ConfigState`가 payload를 요청 구조체로 바꾸고
`CameraFeedbackService`에 전달하면, 현재 등록된 `TPSCameraController`가 요청을 재생한다.

```text
AnimationConfig Camera Notify
  → ConfigState.DispatchNotify
      → CameraFeedbackService
          → TPSCameraController
              기본 TPS 위치·충돌 계산
              → Camera Shot 합성
              → Camera Shake 합성
```

### Shake

| 설정 | 의미 |
|---|---|
| `Duration (f)` | 선택 클립의 FPS와 Speed를 반영한 지속 프레임. 런타임 요청에는 초로 변환해 저장 |
| `Position Amplitude` | 카메라 로컬 축의 위치 흔들림 크기(월드 단위) |
| `Rotation Amplitude` | 로컬 Euler 회전 흔들림 크기(도) |
| `Frequency` | Perlin Noise가 진행하는 속도. 높을수록 빠르고 잘게 떨림 |
| `Envelope` | 정규화 시간 X에 대한 흔들림 배율 Y |

Shake는 `Time.unscaledDeltaTime`으로 진행하므로 히트랙 중에도 설정한 실제 시간대로 끝난다. 새 Shake 요청이
들어오면 현재 요청을 교체하며, Shot 결과 위에 마지막으로 더해진다.

### Shot

Shot은 캐릭터 기준 로컬 Start/End 포즈 두 개를 사용한다. Config Tool에서 Target과 Scene View 구도를 잡은 뒤
`Capture Start`와 `Capture End`로 저장하고, `View Start/End` 또는 Scene View 핸들로 확인·수정한다.

| 설정 | 의미 |
|---|---|
| `Blend In (s)` | 현재 TPS 구도에서 Start 포즈까지 전환 |
| `Move Duration (s)` | Start에서 End 포즈까지 `Move Curve`로 이동 |
| `End Hold (s)` | End 포즈 유지 |
| `Blend Out (s)` | End에서 현재 TPS 구도로 `Blend Curve`를 사용해 복귀 |
| `Return Behind Target` | Blend Out 시작 시 TPS yaw를 현재 캐릭터 방향에 맞춰 복귀 목표를 캐릭터 뒤로 설정 |
| `Start/End FOV` | 두 포즈 사이에 함께 보간되는 화각 |

Shot 시간은 애니메이션 클립 길이에 제한되지 않는다. 상태나 클립이 끝나도 카메라 수신자가 독립적으로 끝까지
재생한다. Blend Out은 Shot 시작 때의 월드 좌표로 돌아가지 않고 매 프레임 계산한 현재 TPS 위치로 섞인다.
`Return Behind Target`이 켜져 있으면 캐릭터가 Shot 도중 회전·이동해도 현재 캐릭터 뒤로 복귀한다.

Config Tool 상단의 시간은 전체 config 누적 시간이 아니라 현재 플레이헤드가 속한 섹션의
`로컬 시간 / 클립 재생 길이`를 표시한다. Camera Shot의 네 구간은 클립 프레임 키가 아니라 Inspector의
초 단위 값으로 편집한다.

## 퍼펙트 회피 판정

```text
ParryWarning Hit Notify
  → 경고 오버랩 안의 플레이어가 IncomingAttack 상태 진입
  → 경고 중 회피 입력 시 Perfect Dodge 후보 등록
  → 같은 공격자의 실제 Damage Hit Notify 실행
      ├─ 데미지 오버랩이 닿음: 공격 무시 후 성공
      └─ 회피 이동으로 빗나감: 등록된 경고 대상으로 성공 확인
  → PerfectDodgeSucceeded(source)
  → HitStopController가 퍼펙트 회피 히트스톱 요청
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
  → HitStopController가 패링 히트스톱 요청
```

실제 Hit가 쳐냄으로 분기된 경우에만 성공 이벤트를 발행한다. `FaceOppositeTargetModule`은 성공 진입 시 플레이어 Look을 공격 몬스터 Look의 반대 방향으로 맞춘다.

## 히트스톱 곡선

`HitStopController > Success Hit Stop`에서 패링과 퍼펙트 회피를 각각 설정한다.

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
4. Success는 보이지만 히트스톱이 약하면 `HitStopController`의 두 속도 곡선 시작 키를 확인한다.

Animation Tool에서 경고 Hit Notify를 선택하면 Scene View에 오버랩 범위를 표시할 수 있다. 런타임에서는 `MonsterActionController.Show Hit Gizmos`와 Game View의 Gizmos를 함께 켠다.
