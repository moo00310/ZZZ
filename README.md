# ZZZ 전투 애니메이션 데모

Unity 6와 URP로 제작 중인 3인칭 액션 전투 프로젝트다. Burnice 캐릭터의 공격, 콤보, 회피, 패링, 피격과 이펙트 재생을 구현하고 있다.

이 프로젝트에서는 Animator Controller에 전투 전이를 구성하지 않는다. 전투 흐름은 `AnimationConfig` 에셋에 저장하고, `ConfigState`가 이를 실행한다. Animator는 `CrossFade`를 통한 클립 재생만 담당한다.

## 주요 기능

- 공격, 콤보, 강화 공격, 회피, 패링, 피격
- 입력 버퍼와 조건부 애니메이션 전이
- 루트 모션 이동과 공격 거리·방향 보정
- 플레이어와 몬스터가 함께 사용하는 `ConfigState`
- 애니메이션 구간, 전이, 이벤트를 편집하는 전용 타임라인 도구
- 여러 프리팹을 묶어 재생하는 이펙트 에셋과 프리팹 단위 오브젝트 풀
- 애니메이션과 이펙트를 함께 확인하는 에디터 프리뷰

## 구조

```text
입력과 게임 상태
       │
       ▼
AnimationConfig ── 전이, 재생 구간, 이벤트, 구간 기능
       │
       ▼
  ConfigState ──── 조건 평가와 애니메이션 흐름 실행
       │
       ├────────── Animator: 클립 재생
       ├────────── PlayerController: 이동과 회전
       └────────── EffectService: 이펙트 재생과 풀링
```

`AnimationConfig`는 다음 요소로 구성된다.

| 요소 | 역할 |
|---|---|
| Section | 재생할 애니메이션 클립과 루트모션 사용 여부 |
| Link | 다른 Section 또는 Config로 넘어가는 조건과 시점 |
| Notify | 특정 시점에 이펙트나 게임 이벤트 실행 |
| Module | 추가 이동, 회전, 타깃 보정, 무적·패링 등 섹션 기능 |

추가 이동과 회전 잠금 같은 섹션 동작은 `TrackClip`의 고정 필드가 아니라
`SectionModule` 조합으로 구성한다. 새로운 전투 행동은 주로 Config 에셋으로 만들고,
새로운 전이 조건이나 섹션 기능이 필요할 때 `LinkCondition` 또는 `SectionModule` 구현을 추가한다.

## 에디터 도구

### AnimationConfigTool

`AnimationConfig`를 타임라인에서 편집한다.

- Section 배치와 재생 구간 편집
- Link, Notify, Module 편집
- 모듈 라인 접기·펼치기와 Window 구간 핸들 드래그 편집
- 콤보와 루트 모션 프리뷰
- 플레이 중 현재 Config, Section, 입력 상태 확인
- 애니메이션 위에서 이펙트 시점과 위치 조정

### EffectTool

`CompositeEffect`를 편집하고 씬에서 미리 재생한다. 하나의 조합에 여러 이펙트 프리팹과 각 프리팹의 지연 시간, 소켓, 위치, 재생 설정을 저장할 수 있다.

## 프로젝트 구조

```text
Assets/
├── 04.Scripts/
│   ├── Core/               AnimationConfig와 공통 실행 인터페이스
│   ├── Player/             플레이어와 상태 머신
│   ├── Monster/            몬스터 동작
│   ├── Movement/           루트 모션 계산
│   ├── Combat/             타격 판정과 전투 보조 기능
│   └── Effects/            이펙트 재생과 풀링
├── 05.Editor/
│   ├── AnimationTool/      AnimationConfig 편집 도구
│   └── EffectTool/         CompositeEffect 편집 도구
└── Tests/EditMode/         EditMode 테스트
```

## 문서

- [애니메이션 아키텍처](Documentation/AnimationArchitecture.md): Config 실행 흐름, 루트 모션, 전이, 입력 버퍼와 전투 기능
- [이펙트 아키텍처](Documentation/EffectArchitecture.md): 이펙트 조합, 재생, 풀링과 자동 반납
- [설계 결정](Documentation/자료구조_선택.md): 성능과 직렬화를 고려한 주요 구현 선택
- [코딩 규칙](Documentation/CodingConventions.md)
- [작업 목록](Documentation/TODO.md)

## 개발 환경

- Unity `6000.3.16f1`
- Universal Render Pipeline `17.3.0`
- Input System `1.19.0`
- Cinemachine `3.1.4`
- Unity Test Framework `1.6.0`

셰이더, 모바일 빌드와 Addressables 적용은 아직 작업 전이다. 진행 상태는 [TODO](Documentation/TODO.md)에 기록한다.
