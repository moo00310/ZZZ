# 코드 샘플: 데이터 기반 전투 액션 파이프라인

> **콤보·캔슬·무적 구간을 코드가 아닌 데이터로 정의하고, 플레이어와 몬스터가 같은 실행 코드로 움직입니다.**
>
> Unity 6 · C# · 핵심 코드 약 400줄 · 프로젝트 소개는 [README](https://github.com/moo00310/ZZZ/blob/main/README.md)

---

## 예시: 앞 회피 동작 하나

캐릭터 Burnice의 앞 회피 데이터([Burnice_Evade_config.asset](https://github.com/moo00310/ZZZ/blob/main/Assets/01.Characters/Burnice/SO_Anim/Burnice_Evade_config.asset))입니다. 숫자는 클립 재생 위치(0 = 시작, 1 = 끝)이고, 링크 일부는 생략했습니다.

**섹션 `Evade_Front`** (앞 회피 클립 1개)

| 종류 | 구간 | 내용 |
|---|---|---|
| Notify | 0.00 | 회피 효과음 재생 |
| Module | 0.00 ~ 0.22 | 무적: 이 구간 동안 피격 무시 |
| Link | 0.00 ~ 0.23 | 일반공격 입력 → `Attack_Rush` (회피 직후 누르면 돌진 공격) |
| Link | 0.23 ~ 1.00 | 일반공격 입력 → `Attack_Normal_01` (늦게 누르면 일반 콤보 1타) |
| Link | 클립 종료 | → `Idle1` |

같은 공격 버튼이라도 회피 중 언제 눌렀는지에 따라 다른 공격으로 이어집니다. 이 분기를 바꿀 때는 코드를 고치지 않고 구간 숫자만 조정합니다.

## 문제

이런 조건을 Animator 전이나 캐릭터 스크립트에 나눠 두면, 회피 무적 시간 하나를 바꾸는 데도 Animator Controller, 스크립트의 타이머, 애니메이션 이벤트를 모두 열어 봐야 합니다. 몬스터에게 같은 동작 규칙이 필요하면 그 로직을 한 번 더 작성해야 합니다.

## 구조

- 위 예시 전체가 `AnimationConfig`(ScriptableObject) 에셋 하나입니다. 섹션(Section)마다 클립 1개와, 그 클립에 붙은 Notify·Module·Link 목록이 있습니다.
- `CharacterActionRunner`(실행기)가 매 프레임 이 데이터를 읽고 다음 섹션을 결정합니다. Animator는 결정된 클립을 `CrossFade`로 재생만 합니다.
- 링크 하나는 "언제 볼지"와 "무엇을 볼지"로 나뉩니다. 예시의 `0.00 ~ 0.23`이 앞(`ClipLink`의 Window·Timing), `일반공격 입력`이 뒤(`LinkCondition`)입니다.
- 새 조건이나 새 구간 기능은 클래스 하나로 추가합니다. `LinkCondition`이나 `SectionModule`을 상속하면 `[SerializeReference]`로 저장되고 편집 툴 목록에도 자동으로 나타납니다. 실행기 코드는 바뀌지 않습니다.

## 읽는 순서

1. **데이터 모델** — [AnimationConfig.cs L39-99](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/AnimationConfig.cs#L39-L99)
   예시의 섹션이 `TrackClip`, 링크가 `ClipLink`입니다. 다형 필드인 `Modules`(L57)와 `Condition`(L93)을 보시면 됩니다.
2. **프레임 루프** — [CharacterActionRunner.cs L85-155](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/CharacterAction/CharacterActionRunner.cs#L85-L155)
   매 프레임 Notify → Module → Link 순서로 평가합니다.
3. **전이와 섹션 진입** — [CharacterActionRunner.cs L208-263](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/CharacterAction/CharacterActionRunner.cs#L208-L263)
   섹션에 들어갈 때마다 무적·패링 상태를 초기화해서, 이전 섹션의 상태가 남지 않게 합니다.
4. **확장 예시** — [IFrameModule.cs](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/CharacterAction/Modules/IFrameModule.cs)
   예시의 무적 구간 모듈입니다. 구간 판정 베이스인 `WindowModule`을 상속해 한 줄로 구현했습니다.

## 결과

- 플레이어 입력 조건 [`InputCondition`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/LinkCondition.cs#L53-L126)과 몬스터 AI 조건 [`AIDecisionCondition`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Monster/MonsterConditionContext.cs#L76-L95)이 같은 실행기에서 동작합니다. 실행기는 두 조건의 구체 타입을 모릅니다.
- 구간 기능 모듈은 현재 14종(무적, 패링, 타깃 워프, 회전 잠금 등)이고, 모두 같은 방식으로 추가했습니다.

---

## 함께 보실 수 있는 자료

- **리소스 수명주기 관리** — [`SharedAssetStore<T>`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/ResourceManagement/SharedAssetStore.cs): 같은 프리팹에 대한 동시 비동기 요청을 참조 카운트로 공유하고, 취소·실패·늦은 완료를 처리합니다. [EditMode 테스트](https://github.com/moo00310/ZZZ/blob/main/Assets/Tests/EditMode/SharedAssetStoreTests.cs)로 검증했습니다.
- **모바일 성능 측정** — [AndroidPerformanceBaseline.md](https://github.com/moo00310/ZZZ/blob/main/Documentation/AndroidPerformanceBaseline.md): Galaxy A55 실기기 기준 측정 결과입니다.
