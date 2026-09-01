# 전투 피드백 구현 요약

타격 결과를 기준으로 이펙트, 사운드, 공격 경고, 카메라와 히트스톱을 재생한다. 판정 시스템은 연출 구현을 직접 알지 않고 결과와 위치만 전달한다.

## 전체 흐름

    Hit Notify
        │
        ▼
    HitService → IHittable.ReceiveHit
        ├─ Ignored  → 피드백 없음
        ├─ Parried  ┐
        └─ Accepted ┴→ HitFeedbackService
                        └─ HitFeedbackReceiver
                            └─ HitFeedbackProfile
                               (HitResult + AttackStrength)
                                ├─ CompositeEffect → EffectService
                                └─ CompositeSound  → AudioService

## 책임 분리

| 구성 요소 | 역할 |
|---|---|
| `HitService` | 타격 범위를 검사하고 결과와 실제 충돌 위치를 전달 |
| `HitFeedbackReceiver` | 피격 대상이 사용할 `HitFeedbackProfile` 제공 |
| `HitFeedbackService` | 결과와 공격 강도에 맞는 이펙트와 사운드 선택 |
| `EffectService` | `CompositeEffect` 재생과 풀링 |
| `AudioService` | `CompositeSound` 재생과 3D AudioSource voice 재사용 |
| `AgentActionController` | 공격 경고, 회피 후보와 패링·퍼펙트 회피 성공 이벤트 관리 |
| `HitStopController` | 성공 이벤트를 히트스톱 설정과 연결 |
| `CameraFeedbackService` | Animation Notify와 현재 카메라 수신자 연결 |

## 피격 이펙트와 타격음

`HitService`는 피격 Collider의 `ClosestPoint`를 `HitContext.HitPoint`로 전달한다. 무적이나 퍼펙트 회피로 `Ignored`된 공격은 피드백을 만들지 않는다.

피격 대상의 `HitFeedbackProfile`은 `HitResult`와 `AttackStrength` 조합으로 `CompositeEffect`와 `CompositeSound`를 선택한다. 플레이어와 몬스터가 같은 조회 경로를 사용하며, 일반 피격과 패링도 데이터만 다르게 구성한다.

- 휘두름과 발사처럼 적중 여부와 관계없는 소리: AnimationConfig의 Sound Notify
- 실제 충돌음: 적중 결과가 확정된 뒤 `HitFeedbackProfile`에서 재생
- VFX 위치 보정: CompositeEffect Entry가 소유
- 타격음 위치: 보정되지 않은 실제 `HitPoint` 사용

이 구분으로 빗나간 공격이나 무시된 공격에서 충돌음이 재생되지 않는다.

## 공격 경고와 방어 판정

몬스터 공격은 실제 Damage Hit보다 앞선 `ParryWarning` Notify로 경고 범위를 검사한다. 같은 시점의 Effect Notify는 몬스터 머리를 따라가는 `AttackWarningCrossEffect`를 재생한다. 판정 경고와 화면 연출은 타이밍만 공유하고 서로의 구현을 직접 참조하지 않는다.

### 퍼펙트 회피

    ParryWarning → 경고 대상 등록
         │
         ├─ 경고 중 회피 입력 → 후보 등록
         │
         └─ 같은 공격자의 실제 Damage Hit 시점
              ├─ 공격 범위에 닿음 → 공격 무시 후 성공
              └─ 회피로 범위를 벗어남 → 등록된 후보 확인 후 성공

회피 입력 순간이 아니라 실제 공격 시점에 성공을 확정한다. 일반 회피의 피격 무시는 `IFrameModule` 구간만 사용한다.

### 패링

    ParryWarning → 공격자와 강도 저장
         │
    ParryModule 활성 중 실제 Hit 수신
         │
    쳐냄 분기 → ParrySucceeded → 반격 Section 진입

패링 자세에 들어간 것만으로는 성공하지 않는다. 활성 구간에 실제 공격이 도달해 쳐냄으로 분기됐을 때만 성공 이벤트를 발행한다.

## 카메라 피드백

Camera Notify는 카메라 컴포넌트를 직접 참조하지 않는다.

    AnimationConfig Camera Notify
      → CharacterActionRunner
        → CameraFeedbackService
          → TPSCameraController
            → 기본 TPS 구도
            → Shot 합성
            → Shake 합성

- Shake는 히트스톱 중에도 실제 시간 기준으로 진행한다.
- Shot은 캐릭터 기준 Start/End 구도와 FOV를 보간한다.
- Shot 종료 시 시작 위치가 아니라 현재 TPS 추적 위치로 복귀한다.

## 히트스톱

`HitStopController`는 패링과 퍼펙트 회피 성공 이벤트를 구독하고 `HitStopService`에 요청한다. 성공 판정은 연출 수치를 모르며, 지속 시간과 속도 곡선은 씬 설정이 소유한다.

- Game Speed Curve: 전체 `Time.timeScale` 조절
- Monster Speed Curve: 공격 몬스터의 애니메이션, Config 시간과 AI 진행 속도 추가 조절
- 복구 시간: `realtimeSinceStartup` 기준으로 계산해 게임 속도가 0이어도 종료 보장

## 진단

개발 HUD와 Scene/Game View 기즈모로 다음 단계를 구분한다.

1. 공격 경고 범위가 플레이어에게 도달했는지 확인한다.
2. 경고 중 회피가 후보로 등록됐는지 확인한다.
3. 같은 공격자의 실제 Damage Hit 시점에 성공이 확정됐는지 확인한다.
4. 성공 이후 이펙트, 사운드, 카메라와 히트스톱 요청이 전달됐는지 확인한다.

## 트레이드오프

| 장점 | 비용 |
|---|---|
| 판정과 연출이 결과 데이터로 분리된다 | 씬에 각 피드백 서비스와 수신자를 올바르게 조립해야 한다 |
| 대상별 피드백을 Profile 데이터로 교체한다 | 결과·강도 조합의 누락을 에디터에서 검증해야 한다 |
| 실제 공격 시점에 방어 성공을 확정한다 | 공격자별 경고 후보와 만료 시간을 추적해야 한다 |
| 카메라와 히트스톱 정책을 전투 로직 밖에서 조정한다 | 이벤트 구독과 캐릭터 교체 시 해제를 관리해야 한다 |

## 관련 문서

- [전투 애니메이션 구현 요약](AnimationArchitecture.md)
- [이펙트 구현 요약](EffectArchitecture.md)
- [플레이어 런타임 및 스쿼드 구조](PlayerRuntimeArchitecture.md)
