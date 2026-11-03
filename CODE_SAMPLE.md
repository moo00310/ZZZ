# 코드 샘플: 데이터 기반 전투 액션 파이프라인

> **콤보·캔슬·무적 구간을 코드가 아닌 데이터로 정의하고, 플레이어와 몬스터가 같은 실행 코드로 움직입니다.**
>
> Unity 6 · C# · 핵심 코드 약 400줄 · 프로젝트 소개는 [README](https://github.com/moo00310/ZZZ/blob/main/README.md)

---

## 핵심 설계

**① 행동은 데이터, 실행은 한 곳에서**
콤보 입력 구간, 캔슬, 무적 구간을 `AnimationConfig`(ScriptableObject)에 담습니다. `CharacterActionRunner`(실행기) 하나가 이 데이터를 해석하고, Animator는 클립 재생만 맡습니다.

**② 플레이어와 몬스터가 같은 실행기를 씁니다**
플레이어 입력 조건 [`InputCondition`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/LinkCondition.cs#L53-L126)과 몬스터 AI 조건 [`AIDecisionCondition`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Monster/MonsterConditionContext.cs#L76-L95)은 둘 다 `LinkCondition`을 구현합니다. 실행기는 어느 쪽인지 모르고 같은 방식으로 평가합니다.

**③ 기능 추가는 클래스 하나로**
무적, 패링, 타깃 워프, 회전 잠금 등 구간 기능 14종을 모두 `SectionModule` 상속으로 추가했습니다. `[SerializeReference]`로 저장되고 편집 툴에도 자동으로 등록되며, 실행기에는 타입별 분기가 없습니다.

## 데이터 예시: 회피 후 공격 분기

실제 캐릭터의 앞 회피 데이터([Burnice_Evade_config.asset](https://github.com/moo00310/ZZZ/blob/main/Assets/01.Characters/Burnice/SO_Anim/Burnice_Evade_config.asset))입니다. 구간은 클립 재생 위치(0 = 시작, 1 = 끝)이고, 링크 일부는 생략했습니다.

**섹션 `Evade_Front`** (앞 회피 클립 1개)

| 종류 | 구간 | 내용 |
|---|---|---|
| Notify | 0.00 | 회피 효과음 재생 |
| Module | 0.00 ~ 0.22 | 무적: 이 구간 동안 피격 무시 |
| Link | 0.00 ~ 0.23 | 일반공격 입력 → `Attack_Rush` (회피 직후 누르면 돌진 공격) |
| Link | 0.23 ~ 1.00 | 일반공격 입력 → `Attack_Normal_01` (늦게 누르면 일반 콤보 1타) |
| Link | 클립 종료 | → `Idle1` |

같은 공격 버튼이라도 회피 중 언제 눌렀는지에 따라 다른 공격으로 이어집니다. 링크는 "언제 볼지"(구간, `ClipLink`)와 "무엇을 볼지"(일반공격 입력, `LinkCondition`)로 나뉘어 있어서, 몬스터 AI 조건도 같은 구간 규칙을 그대로 씁니다.

## 왜 데이터로 만들었나

이런 조건을 Animator 전이나 캐릭터 스크립트에 나눠 두면, 회피 무적 시간 하나를 바꾸는 데도 Animator Controller, 스크립트의 타이머, 애니메이션 이벤트를 모두 열어 봐야 합니다. 몬스터에게 같은 동작 규칙이 필요하면 그 로직을 한 번 더 작성해야 합니다. 지금은 위 표의 숫자만 고치면 됩니다.

## 읽는 순서

1. **데이터 모델** (①) — [AnimationConfig.cs L39-99](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/AnimationConfig.cs#L39-L99)
   예시의 섹션이 `TrackClip`, 링크가 `ClipLink`입니다. 다형 필드인 `Modules`(L57)와 `Condition`(L93)을 보시면 됩니다.
2. **프레임 루프** (①②) — [CharacterActionRunner.cs L85-155](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/CharacterAction/CharacterActionRunner.cs#L85-L155)
   매 프레임 Notify → Module → Link 순서로 평가합니다. 조건은 `Matches()` 호출 하나로만 다룹니다.
3. **전이와 섹션 진입** (①) — [CharacterActionRunner.cs L208-263](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/CharacterAction/CharacterActionRunner.cs#L208-L263)
   섹션에 들어갈 때마다 무적·패링 상태를 초기화해서, 이전 섹션의 상태가 남지 않게 합니다.
4. **확장 예시** (③) — [IFrameModule.cs](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/CharacterAction/Modules/IFrameModule.cs)
   예시의 무적 구간 모듈입니다. 구간 판정 베이스인 `WindowModule`을 상속해 한 줄로 구현했습니다.

---

## 함께 보실 수 있는 자료

- **리소스 수명주기 관리** — [`SharedAssetStore<T>`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/ResourceManagement/SharedAssetStore.cs): 같은 프리팹에 대한 동시 비동기 요청을 참조 카운트로 공유하고, 취소·실패·늦은 완료를 처리합니다. [EditMode 테스트](https://github.com/moo00310/ZZZ/blob/main/Assets/Tests/EditMode/SharedAssetStoreTests.cs)로 검증했습니다.
- **모바일 성능 측정** — [AndroidPerformanceBaseline.md](https://github.com/moo00310/ZZZ/blob/main/Documentation/AndroidPerformanceBaseline.md): Galaxy A55 실기기 기준 측정 결과입니다.
