# 코드 샘플: 데이터 기반 전투 액션 파이프라인

> **콤보·캔슬·무적 구간을 코드가 아닌 데이터로 정의하고, 플레이어와 몬스터가 같은 실행 코드로 움직입니다.**
>
> Unity 6 · C# · 핵심 코드 약 400줄 · 프로젝트 소개는 [README](https://github.com/moo00310/ZZZ/blob/main/README.md)

---

## 핵심 설계

**① 행동은 데이터, 실행은 한 곳에서**
콤보 입력 구간, 캔슬, 무적 구간을 `AnimationConfig`(ScriptableObject)에 담습니다. `CharacterActionRunner`(실행기) 하나가 이 데이터를 해석하고, Animator는 클립 재생만 맡습니다.

**② 플레이어와 몬스터가 같은 실행기를 씁니다**
플레이어 입력 조건 [`InputCondition`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Core/LinkCondition.cs#L53-L126)과 몬스터 AI 조건 [`AIDecisionCondition`](https://github.com/moo00310/ZZZ/blob/main/Assets/04.Scripts/Monster/MonsterConditionContext.cs#L76-L95)은 둘 다 `LinkCondition`을 구현합니다. 실행기는 어느 쪽인지 모르고 같은 방식으로 평가합니다. ([처음엔 플레이어 전용이었던 구조를 바꾼 과정](#몬스터를-붙이며-바꾼-구조))

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

이전 팀 프로젝트(조선 관문 일지)에서는 동작 하나를 고칠 때마다 Animator 전이, 코드, 툴을 오가며 디버깅해야 했습니다. 그래서 이번에는 Unreal의 Anim Montage처럼 클립을 구간으로 나누고 전이 규칙을 데이터에 두는 구조를 Unity에 맞게 다시 설계했습니다.

- Animator는 재생·블렌딩·루트 모션만 맡고, 전이 판단은 C# 실행기가 합니다. 전투 규칙이 한 곳에 모입니다.
- 구간은 비율(0~1)로 저장해서, 클립 길이가 바뀌어도 타이밍이 유지됩니다.

## 몬스터를 붙이며 바꾼 구조

처음 만든 실행기(당시 `ConfigState`)는 플레이어 전용이었습니다. `PlayerController`, `PlayerStateMachine` 같은 플레이어 타입을 직접 참조했고, 링크 조건도 `ClipLink`의 공격·방향 입력 필드로 고정돼 있었습니다. 입력이 없는 몬스터는 이 실행기로 전이 조건을 표현할 수 없었습니다.

그래서 몬스터를 붙이기 전에 두 가지를 바꿨습니다([PR #5](https://github.com/moo00310/ZZZ/pull/5)).

```diff
  // 1. 플레이어 타입 대신 인터페이스(이동·Animator·컨텍스트·상태 신호)에 의존
- public ConfigState(PlayerStateContext ctx, PlayerStateMachine machine, AnimationConfig homeConfig)
+ public ConfigState(IConfigContext ctx, IConfigSignals signals,
+     ILinkConditionContext condCtx, AnimationConfig homeConfig)

  // 2. 입력 필드 고정 조건을 다형 조건(LinkCondition)으로 교체
- if (!ConditionMatches(link, moveDir)) continue;
+ if (!Cond(link).Matches(_condCtx)) continue;

- if (link.Attack != ComboInput.None) Machine.ConsumeInput();
+ Cond(link).Consume(_condCtx);
```

이미 만들어 둔 에셋이 깨지지 않도록, 조건이 비어 있는 링크는 기존 입력 필드로 `InputCondition`을 즉석에서 만들어 쓰게 했습니다. 그 뒤 에디터 메뉴로 에셋을 일괄 변환하고, 레거시 필드는 [PR #8](https://github.com/moo00310/ZZZ/pull/8)에서 제거했습니다. 바로 다음에 몬스터를 붙인 [커밋](https://github.com/moo00310/ZZZ/commit/b91438d)에서는 몬스터 쪽 클래스만 추가했고, 실행기 파일은 한 줄도 바뀌지 않았습니다.

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
