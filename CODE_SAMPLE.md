# 코드 샘플 가이드

> Unity 6 / C# 3인칭 액션 전투 프로토타입에서 두 주제를 골랐습니다. 예상 소요 시간은 15분입니다.
> 프로젝트 전체 소개는 [README](README.md)에 있습니다.

| 주제 | 보여주고 싶은 점 | 정독 분량 |
|---|---|---|
| [1. 데이터 기반 전투 액션 파이프라인](#1-데이터-기반-전투-액션-파이프라인) | 확장 지점을 둔 설계, 플레이어·몬스터 공용 실행기 | 약 400줄 |
| [2. 리소스 수명주기와 풀링](#2-리소스-수명주기와-풀링) | 비동기 로드의 소유권·취소·실패 처리, 테스트로 검증 | 약 350줄 |
| [3. 모바일 성능 측정](#3-모바일-성능-측정) | Android 실기기 측정 기준 | 문서 1개 |

---

## 1. 데이터 기반 전투 액션 파이프라인

<!-- GIF: 콤보 → 회피 → 패링 전이를 에디터 타임라인과 함께 녹화 -->

**문제**
콤보 입력 구간, 캔슬, 무적 구간 같은 조건을 Animator Controller 전이나 캐릭터별 코드에 두면, 행동 하나를 고칠 때 여러 곳을 같이 추적해야 합니다.

**설계**
- 행동은 `AnimationConfig`(ScriptableObject) 데이터로 표현합니다. 데이터는 Section(클립), Link(전이), Notify(시점 이벤트), Module(구간 기능)로 구성됩니다.
- `CharacterActionRunner` 하나가 이 데이터를 해석합니다. Animator는 재생만 맡습니다.
- 확장은 상속 1개로 합니다. 새 전이 조건은 `LinkCondition`, 새 구간 기능은 `SectionModule`을 상속하고, `[SerializeReference]`로 다형 직렬화합니다. 실행기에 타입 분기를 추가하지 않습니다.
- 전이를 "언제 평가할지"(Timing, Window)는 `ClipLink`가, "무엇이 충족인지"는 `LinkCondition`이 맡도록 책임을 나눴습니다.

**읽는 순서**
1. 데이터 모델: [AnimationConfig.cs L39-99](Assets/04.Scripts/Core/AnimationConfig.cs#L39-L99). `TrackClip`과 `ClipLink`, 다형 필드인 `Modules`(L57)와 `Condition`(L93)을 보시면 됩니다.
2. 실행기 프레임 루프: [CharacterActionRunner.cs L85-155](Assets/04.Scripts/Core/CharacterAction/CharacterActionRunner.cs#L85-L155). `Update` → Notify → Module → Link 순서로 평가합니다.
3. 전이와 섹션 진입: [CharacterActionRunner.cs L208-263](Assets/04.Scripts/Core/CharacterAction/CharacterActionRunner.cs#L208-L263). 섹션에 진입할 때마다 무적·패링 상태를 리셋해서, 상태가 다음 섹션으로 새지 않게 합니다.
4. 확장 지점:
   - [SectionModule.cs](Assets/04.Scripts/Core/CharacterAction/Modules/SectionModule.cs) → [WindowModule.cs](Assets/04.Scripts/Core/CharacterAction/Modules/WindowModule.cs) → [IFrameModule.cs](Assets/04.Scripts/Core/CharacterAction/Modules/IFrameModule.cs) (무적 구간 모듈은 한 줄로 구현됩니다)
   - [LinkCondition.cs L6-51](Assets/04.Scripts/Core/LinkCondition.cs#L6-L51): 조건 베이스와 조건 평가 컨텍스트 인터페이스

**결과**
- 플레이어 입력 조건 [`InputCondition`](Assets/04.Scripts/Core/LinkCondition.cs#L53-L126)과 몬스터 AI 조건 [`AIDecisionCondition`](Assets/04.Scripts/Monster/MonsterConditionContext.cs#L76-L95)이 같은 실행기 위에서 동작합니다. 실행기는 두 조건의 구체 타입을 알지 못합니다.
- 현재 구간 기능 모듈은 14종(무적, 패링, 타깃 워프, 회전 잠금 등)이고, 모두 같은 방식으로 추가했습니다.

---

## 2. 리소스 수명주기와 풀링

**문제**
같은 프리팹을 여러 캐릭터와 이펙트가 동시에 비동기로 요청합니다. 요청 중 일부는 도중에 취소되고, 로드가 실패하기도 합니다. 이때 핸들을 언제 해제하는지 불명확하면 모바일에서 메모리 누수나 중복 로드가 생깁니다.

**설계: [`SharedAssetStore<T>`](Assets/04.Scripts/ResourceManagement/SharedAssetStore.cs)**
- 키마다 로드 작업을 하나만 만들고, 참조 카운트로 공유합니다. 호출자는 `AssetLease`를 받고, `Dispose`로 반납합니다. `Dispose`는 두 번 호출해도 안전합니다.
- 대기 중인 호출자 한 명이 취소해도 공유 로드는 취소하지 않습니다. 모든 호출자가 떠난 뒤 로드가 늦게 완료되면, 그 결과를 스스로 해제합니다.
- Addressables에 의존하지 않도록 `IAssetLoadOperation<T>`로 추상화했습니다. 그래서 Unity 런타임 없이 테스트할 수 있습니다.

**읽는 순서**
1. [SharedAssetStore.cs L52-111](Assets/04.Scripts/ResourceManagement/SharedAssetStore.cs#L52-L111): 획득, 완료 관찰, 반납, 제거
2. [SharedAssetStoreTests.cs](Assets/Tests/EditMode/SharedAssetStoreTests.cs): 아래 6개 시나리오를 검증합니다.
   - 마지막 lease가 반납될 때만 해제한다 (중복 `Dispose` 포함)
   - 동시 요청은 로드 작업 하나를 공유한다
   - 한 호출자의 취소가 다른 호출자에게 영향을 주지 않는다
   - 모두 취소한 뒤 늦게 도착한 결과는 해제한다
   - 실패한 로드는 핸들을 해제하고, 재시도할 수 있다
   - 이미 취소된 요청은 로드를 시작하지 않는다

**연관 코드: [EffectPool.cs](Assets/04.Scripts/Effects/EffectPool.cs)**
프리팹 단위 이펙트 풀입니다. 소유자(Owner) 집합이 비면 teardown에 들어가, 대여 중인 인스턴스까지 회수하고 파괴합니다([L74-90](Assets/04.Scripts/Effects/EffectPool.cs#L74-L90)). 씬 전환 뒤에도 이펙트가 살아남거나, 이미 파괴된 풀로 반납되는 문제를 막기 위한 구조입니다.

---

## 3. 모바일 성능 측정

[AndroidPerformanceBaseline.md](Documentation/AndroidPerformanceBaseline.md)
Galaxy A55(Vulkan, IL2CPP)에서 공격 시나리오를 반복하며 측정했습니다.

| 지표 | 결과 |
|---|---|
| 평균 FPS | 60fps |
| p99 프레임 시간 | 18.3ms |
| 평균 GC Alloc | 18.6B/frame |
| SetPass Calls | 약 22 |

반복 측정에서 메모리가 계속 증가하는지도 확인했습니다. 리소스 관리 구조를 바꾼 뒤 같은 조건으로 다시 비교하기 위한 기준 문서입니다.
