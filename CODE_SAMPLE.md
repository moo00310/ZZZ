# 코드 샘플 가이드

> Unity 6 / C# 3인칭 액션 전투 프로토타입의 핵심인 **데이터 기반 전투 액션 파이프라인**을 소개합니다. 정독 분량은 약 400줄, 예상 소요 시간은 15분입니다. 프로젝트 전체 소개와 편집 툴은 [README](https://github.com/moo00310/ZZZ/blob/main/README.md)에 있습니다.

---

## 데이터 기반 전투 액션 파이프라인

**문제** 콤보 입력 구간, 캔슬, 무적 구간 같은 조건을 Animator Controller 전이나 캐릭터별 코드에 두면, 행동 하나를 고칠 때 여러 곳을 같이 추적해야 합니다.

**설계**

- 행동은 `AnimationConfig`(ScriptableObject) 데이터로 표현합니다. 데이터는 Section(클립), Link(전이), Notify(시점 이벤트), Module(구간 기능)로 구성됩니다.
- `CharacterActionRunner` 하나가 이 데이터를 해석합니다. Animator는 재생만 맡습니다.
- 확장은 상속 1개로 합니다. 새 전이 조건은 `LinkCondition`, 새 구간 기능은 `SectionModule`을 상속하고, `[SerializeReference]`로 다형 직렬화합니다. 실행기에 타입 분기를 추가하지 않습니다.
- 전이를 "언제 평가할지"(Timing, Window)는 `ClipLink`가, "무엇이 충족인지"는 `LinkCondition`이 맡도록 책임을 나눴습니다.

**읽는 순서**

1. 데이터 모델: [AnimationConfig.cs L39-99](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/AnimationConfig.cs#L39-L99). `TrackClip`과 `ClipLink`, 다형 필드인 `Modules`(L57)와 `Condition`(L93)을 보시면 됩니다.
2. 실행기 프레임 루프: [CharacterActionRunner.cs L85-155](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/CharacterAction/CharacterActionRunner.cs#L85-L155). `Update` → Notify → Module → Link 순서로 평가합니다.
3. 전이와 섹션 진입: [CharacterActionRunner.cs L208-263](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/CharacterAction/CharacterActionRunner.cs#L208-L263). 섹션에 진입할 때마다 무적·패링 상태를 리셋해서, 상태가 다음 섹션으로 새지 않게 합니다.
4. 확장 예시: [IFrameModule.cs](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/CharacterAction/Modules/IFrameModule.cs). 무적 구간 모듈은 `WindowModule`(구간 판정을 담당하는 `SectionModule` 하위 베이스)을 상속해 한 줄로 구현됩니다.

**결과**

- 플레이어 입력 조건 [`InputCondition`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/LinkCondition.cs#L53-L126)과 몬스터 AI 조건 [`AIDecisionCondition`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Monster/MonsterConditionContext.cs#L76-L95)이 같은 실행기 위에서 동작합니다. 실행기는 두 조건의 구체 타입을 알지 못합니다.
- 현재 구간 기능 모듈은 14종(무적, 패링, 타깃 워프, 회전 잠금 등)이고, 모두 같은 방식으로 추가했습니다.

---

## 함께 보실 수 있는 자료

- **리소스 수명주기 관리** — [`SharedAssetStore<T>`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/ResourceManagement/SharedAssetStore.cs): 같은 프리팹에 대한 동시 비동기 요청을 참조 카운트로 공유하고, 취소·실패·늦은 완료를 처리합니다. [EditMode 테스트](https://github.com/moo00310/ZZZ/blob/main/Assets/Tests/EditMode/SharedAssetStoreTests.cs)로 검증했습니다.
- **모바일 성능 측정** — [AndroidPerformanceBaseline.md](https://github.com/moo00310/ZZZ/blob/main/Documentation/AndroidPerformanceBaseline.md): Galaxy A55 실기기 기준 측정 결과입니다.
