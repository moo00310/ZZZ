# Android 초기 성능 기준

## 목적

Addressables 적용 전 Android 실기 성능을 기록한다. Addressables 적용 후에도 같은 기기, 빌드 설정, 장면 및 조작 조건으로 다시 측정해 결과를 비교한다.

## 측정 환경

| 항목 | 값 |
|---|---|
| 측정 일시 | 2026-09-14 KST |
| Unity | 6000.3.16f1 |
| 기기 | Samsung SM-A556E |
| Android | 16 / API 36 |
| 화면 | 1080×2340, 가로 방향 |
| 기기 지원 주사율 | 60Hz / 120Hz |
| 애플리케이션 목표 FPS | 60fps |
| 그래픽 API | Vulkan |
| 품질 설정 | Mobile |
| 테스트 장면 | `Assets/99.Scenes/SampleScene.unity` |
| 빌드 | Development Build, Autoconnect Profiler On |
| 프로파일링 | Deep Profiling Off |
| 압축 | LZ4 |
| 텍스처 압축 | ETC2 |
| 스크립팅 백엔드 | IL2CPP |
| 대상 아키텍처 | ARM64 |
| Minimum API | 25 |
| Target API | Automatic |

현재 Android Application Identifier는 템플릿 값인 `com.UnityTechnologies.com.unity.template.urpblank`이다. 배포 전 프로젝트 고유 값으로 변경해야 한다.

## 측정 시나리오

### Idle

- 인트로가 끝나고 조작 가능한 상태에서 장면이 안정된 후 측정
- 별도 조작 없이 1,999프레임, 약 33.31초 기록
- 캡처: `Android_60fps_Idle`

### Attack to Idle 1차

- 공격과 이펙트를 실행한 후 Idle 상태로 돌아오는 구간 측정
- 1,999프레임, 약 33.29초 기록
- 캡처: `Android_60fps_AttackToIdle`

### Attack to Idle 반복 확인

- 동일한 공격 후 Idle 절차를 반복해 결과 재현 여부 확인
- 2,000프레임, 약 33.33초 기록
- 캡처: `Android_DirectRef_AttackToIdle2`
- 실제 공격 횟수는 캡처에 자동 기록되지 않았으므로, 다음 Addressables 전후 비교부터 횟수를 고정해 별도로 기록한다.

## 측정 결과

| 지표 | Idle | Attack 1차 | Attack 반복 |
|---|---:|---:|---:|
| 프레임 수 | 1,999 | 1,999 | 2,000 |
| 측정 시간 | 33.31초 | 33.29초 | 33.33초 |
| 평균 프레임 시간 | 16.66ms | 16.66ms | 16.66ms |
| p95 프레임 시간 | 17.81ms | 17.81ms | 17.83ms |
| p99 프레임 시간 | 18.38ms | 18.28ms | 18.25ms |
| 최대 프레임 시간 | 18.95ms | 19.27ms | 19.38ms |
| 평균 FPS | 60.02 | 60.04 | 60.01 |
| 평균 GC Alloc | 2.90B/frame | 18.22B/frame | 18.61B/frame |
| 최대 GC Alloc | 1,622B | 4,640B | 13,330B |
| 평균 사용 메모리 | 414.72MB | 423.41MB | 420.35MB |
| 최대 사용 메모리 | 415.43MB | 423.55MB | 420.41MB |
| 평균 Batches | 46.84 | 48.23 | 48.37 |
| 평균 SetPass Calls | 22.07 | 22.23 | 22.37 |
| 평균 Draw Calls | 46.84 | 48.31 | 48.49 |
| 평균 Triangles | 78,421.49 | 80,788.89 | 80,846.44 |

## 기준 판정

- 세 캡처 모두 평균 약 60fps를 유지했다.
- 공격 반복 측정의 평균, p95, p99 및 최대 프레임 시간이 1차 측정과 거의 같아 결과가 재현됐다.
- 공격 반복 측정의 평균 GC Alloc은 18.61B/frame으로 매우 작다. 최대 13,330B 할당은 한 프레임의 일시적인 값이며 지속 할당으로 보이지 않는다.
- 공격 반복 측정의 평균 및 최대 메모리 차이는 약 0.06MB다. 측정 구간에서 메모리가 계속 증가한 징후는 확인되지 않았다.
- 공격 시 Idle보다 약 5.6~8.7MB 많은 메모리를 사용했다. 최초 생성된 이펙트 풀이나 관련 리소스가 유지된 결과일 수 있으며, 반복 측정마다 계속 증가하지 않는지 Addressables 적용 후에도 확인한다.
- 렌더링 지표는 두 공격 측정에서 사실상 동일하다.
- Unity GPU Profiler가 이 기기의 Android Vulkan GPU 시간을 제공하지 않아 GPU 시간은 기준에 포함하지 않았다. CPU 프레임, 메모리, GC 및 렌더링 카운터를 비교 기준으로 사용한다.

## Addressables 비교용 대표 기준

Addressables 적용 전 대표 캡처는 다음 두 개를 사용한다.

1. 평상시 기준: `Android_60fps_Idle`
2. 공격 후 안정화 기준: `Android_DirectRef_AttackToIdle2`

Addressables 적용 후에는 같은 기기와 설정에서 대응 캡처를 만들고 다음 항목을 비교한다.

- 평균, p95, p99 및 최대 프레임 시간
- 평균 FPS
- 평균 및 최대 GC Alloc
- 평균 및 최대 사용 메모리
- Batches, SetPass Calls, Draw Calls 및 Triangles
- 최초 이펙트 실행 시 지연과 반복 공격 후 메모리 안정화 여부

60fps 목표의 한 프레임 예산은 약 16.67ms다. `WaitForTargetFPS`는 목표 프레임까지 기다리는 시간이라 성능 작업으로 계산하지 않는다. p95, p99 및 최대값이 기존 기준보다 눈에 띄게 증가했을 때 실제 스파이크의 원인을 CPU Timeline에서 확인한다.

## 보관 자료

- 자동 요약: `ProfilerCaptures/ProfilerCaptureSummary.md`
- Idle 원본: `ProfilerCaptures/Android_60fps_Idle.data`
- Attack 1차 원본: `ProfilerCaptures/Android_60fps_AttackToIdle.data`
- Attack 반복 원본: `ProfilerCaptures/Android_DirectRef_AttackToIdle2.data`

Profiler `.data`와 `.highlights`는 각각 100MB가 넘을 수 있는 로컬 분석 자료라 Git에서 제외한다. 요약 Markdown과 필요한 PNG만 저장소에 보관할 수 있다.

## 다음 측정 규칙

1. 같은 기기, Mobile 품질, 60fps 및 Deep Profiling Off 설정을 유지한다.
2. 인트로가 끝나고 조작 가능해진 뒤 측정을 시작해 컷씬 시간을 제외한다.
3. Idle 측정은 약 2,000프레임으로 맞춘다.
4. 공격 측정은 같은 공격을 같은 횟수만큼 실행하고, 공격 횟수를 캡처 이름이나 기록에 남긴다.
5. 공격과 이펙트가 끝난 뒤 Idle 구간을 포함해 메모리가 안정되는지 확인한다.
6. `ZZZ > Profiling > Analyze Saved Captures`로 요약을 갱신한다.
7. 기기 온도와 백그라운드 앱 상태를 가능한 한 비슷하게 유지한다.
