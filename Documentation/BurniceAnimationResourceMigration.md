# Burnice 애니메이션 리소스 교체 기록

2026-08-05 기준 Burnice 애니메이션과 캐릭터 리소스 교체 범위를 기록한다.

## 교체 범위

| 대상 | 현재 경로 | 처리 |
|---|---|---|
| AnimationClip | `Assets/01.Characters/Burnice/Animations/Anim` | 같은 이름의 루트모션 보정 클립 146개로 교체 |
| Animator Controller | `Assets/01.Characters/Burnice/Animations/Avatar_Female_Size02_Burnice_Controller.controller` | 교체된 클립 GUID를 참조하도록 갱신 |
| 캐릭터 프리팹 | `Assets/01.Characters/Burnice/Prefabs/Avatar_Female_Size02_Burnice.prefab` | 새 Controller와 현재 본 구조를 사용하는 프리팹으로 교체 |
| AnimationConfig | `Assets/01.Characters/Burnice/SO_Anim` | 9개 config의 Clip 참조와 섹션 모듈 데이터를 갱신 |
| 테스트 씬 | `Assets/99.Scenes/SampleScene.unity` | 새 프리팹 GUID와 fileID 기준으로 인스턴스를 갱신 |

교체 과정에서 사용한 `Anim_Rm`, `Avatar_Female_Size02_Burnice_old` 같은 중간 이름은 현재 소스가
아니다. 최종 리소스는 위의 기존 경로에 정리되어 있으므로 별도의 복제 폴더나 일회성 교체 도구를
저장소에 남기지 않는다.

## GUID와 참조 규칙

- 이번 변경은 파일 내용뿐 아니라 `.meta` GUID도 교체한다. 애니메이션 폴더, 146개 클립,
  Animator Controller와 캐릭터 프리팹의 GUID 변경은 의도된 변경이다.
- 클립 파일과 `.meta`는 항상 한 쌍으로 커밋한다.
- Controller의 모든 Motion, 9개 `AnimationConfig`의 `TrackClip.Clip`, `SampleScene`의 프리팹
  인스턴스를 같은 커밋에서 갱신한다.
- `Assets/_Recovery/0.unity`는 Unity가 생성한 복구 데이터이며 빌드 씬이 아니다. 이전 Controller
  GUID가 남아 있어도 교체 검증 대상이나 커밋 대상에 포함하지 않는다.

## TurnBack 설정

`Burnice_Run_config`의 TurnBack은 `SectionTurnModule`로 최상위 캐릭터를 정확히 180도 회전시킨다.
런타임은 `Root`와 `Bip001` 중 실제 프레임 회전 델타가 나오는 본을 선택하고, 최상위 오브젝트에
넘긴 월드 yaw만큼 `Bip001`을 역보정해 모델이 360도 도는 현상을 막는다. TurnBack에서
`Run_Loop`으로 가는 링크는 전이 중 회전 포즈가 다시 섞이지 않도록 `BlendDuration=0`을 사용한다.

## 체크아웃 후 검증

1. Unity에서 프로젝트를 열어 전체 리임포트와 스크립트 컴파일이 오류 없이 끝나는지 확인한다.
2. Burnice Animator Controller의 Motion에 Missing 클립이 없는지 확인한다.
3. 9개 Burnice `AnimationConfig`의 Clip 필드에 Missing 참조가 없는지 확인한다.
4. `SampleScene`의 Burnice가 새 프리팹 인스턴스로 연결되는지 확인한다.
5. Run 중 반대 방향 입력으로 TurnBack을 실행해 최상위 오브젝트가 180도 회전하고, 이후 이동이
   새 정면으로 이어지며 `Run_Loop` 전이에서 튀지 않는지 확인한다.
