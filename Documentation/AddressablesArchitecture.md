# Addressables 구성과 리소스 수명주기

## 도입 배경과 선택 이유

이 프로젝트는 AnimationConfig가 애니메이션, VFX와 사운드를 연결하는 전투 프로토타입이다.
기존에는 씬의 캐릭터 프리팹과 CompositeEffect의 VFX 프리팹을 직접 참조했다. 참조 그래프를 따라
리소스가 연결되므로, 전투에 필요한 원본을 언제 준비하고 사용이 끝난 뒤 누가 반환하는지 명확하게
표현하기 어려웠다. 특히 풀의 인스턴스 수명과 원본 리소스의 수명을 함께 관리할 경계가 필요했다.

Addressables를 도입한 목적은 캐릭터와 VFX의 로드·사용·해제 책임을 코드로 명시하고,
공유 리소스가 여러 번들에 중복 포함되는지 빌드 결과로 검증하는 것이다. 전투 진입 전에 필요한
리소스를 준비하고 일반 캐릭터 교체에서는 유지하며, 스쿼드 또는 소유자 종료 시 사용권을 반환한다.
시작 메모리와 전투 중 로딩 지연을 제어할 기반을 만드는 것이 목표이며, 메모리 절감이나 프레임
개선 수치는 아직 변경 후 실기 측정으로 입증하지 않았다.

| 선택지 | 프로젝트에서의 판단 |
|---|---|
| 기존 직접 참조 | 설정이 간단하지만 캐릭터와 VFX의 로딩 경계를 분리하려면 참조 구조를 바꿔야 한다 |
| Resources 기반 로딩 | 에셋별 로드는 가능하지만 번들 구성·공유 의존성·콘텐츠 배포까지 관리할 별도 설계가 필요하다 |
| AssetBundle API 직접 사용 | 번들 위치, 의존성 로드와 사용권 반환을 직접 연결해야 한다 |
| Addressables | 주소와 카탈로그로 에셋 위치를 해석하고 번들 의존성을 처리하므로, 게임 쪽은 사용 범위와 소유권에 집중할 수 있다 |

현재는 로컬 번들을 앱에 포함한다. 원격 경로는 확장을 위한 설정이며, 서버 배포나 앱과 독립적인
콘텐츠 패치를 구현했다는 의미는 아니다. AssetBundle은 배포 리소스 파일이고 Addressables는 그 위의
로딩·관리 계층이다. 스토어 업로드 여부와 관계없이 로컬 리소스 관리에도 사용한다.

## 현재 범위

Addressables를 런타임 로딩 API로 사용하고 배포 파일은 AssetBundle로 빌드한다.
SampleScene의 캐릭터 슬롯과 CompositeEffect의 VFX 프리팹은 `AssetReferenceGameObject`로 저장한다.
모델, 애니메이션 설정, 머티리얼과 사운드는 기존 직접 의존성으로 캐릭터와 함께 제공한다.
VFX 원본은 캐릭터 설정을 순회해 별도로 로드하고 기존 EffectService 풀에 연결한다.

## Profile과 그룹

기본 Profile은 `Local Development`다.

| 변수 | 값 |
|---|---|
| Local.BuildPath | [Addressables.BuildPath]/[BuildTarget] |
| Local.LoadPath | {Addressables.RuntimePath}/[BuildTarget] |
| Remote.BuildPath | ServerData/[BuildTarget] |
| Remote.LoadPath | http://localhost/[BuildTarget] |

Remote Catalog는 비활성화한다. Player 빌드 시 Addressables 콘텐츠도 함께 빌드한다.
모든 콘텐츠 그룹은 Local 경로, Pack Together와 LZ4 압축을 사용한다.
주소는 소문자 `<종류>/<소유 범위>/<이름>` 형식이며 Label은 character/vfx와 shared/burnice로 분류한다.
런타임은 AssetReference의 GUID를 키로 사용한다.

| 그룹 | 명시적 에셋 | 분리 이유 |
|---|---|---|
| Characters - Burnice | Burnice 프리팹 1개 | 모델·애니메이션 등 캐릭터 한 명의 종속 리소스 |
| VFX - Shared | AttackWarningCross, BigExplosion, Flare, LightBloom, MeeleString, PaticleSprite, ParticlesLight | 여러 캐릭터가 공유하는 작은 VFX 묶음 |
| VFX - Burnice | FireBeam | 캐릭터 전용 연출 |
| VFX - Shared Dependencies | Fire_052 텍스처와 패키지 셰이더·머티리얼 5개 | 여러 번들이 참조하는 리소스의 중복 복사 방지 |

Shared Dependencies는 상주 그룹이 아니다. 이를 참조하는 번들의 수명에 따라 로드·해제한다.
공용 VFX가 커지면 사용 빈도와 의존성을 측정해 그룹을 나눈다.

### 번들을 나누는 기준과 각 선택의 비용

분류 기준은 폴더나 파일 확장자보다 사용 시점, 해제 시점, 공유 범위와 실제 의존성이다.
함께 사용하고 함께 정리할 리소스를 묶으며, 서로 다른 번들에서 반복 사용하는 리소스는 중복 여부를
확인해 분리한다. 이후 원격 배포를 도입한다면 변경 빈도와 재다운로드 크기도 판단 기준에 추가한다.

**Characters - Burnice**는 캐릭터 한 명을 전투에 투입하는 단위다. 모델, Animator Controller,
참조된 AnimationClip, AnimationConfig와 사운드가 프리팹의 직접 의존성으로 함께 포함된다.
현재 전투에서는 이 리소스들이 캐릭터와 함께 필요하므로 별도 로드 순서와 사용권을 늘리지 않았다.
그룹에 명시적으로 등록된 것은 프리팹이며, 캐릭터 폴더의 모든 파일을 자동으로 포함하는 것은 아니다.
큰 대사 묶음, BGM 또는 여러 캐릭터의 공용 사운드처럼 사용 범위가 달라지면 독립 그룹으로 분리한다.

**VFX - Burnice**는 FireBeam처럼 캐릭터 전용인 원시 이펙트의 범위다. CompositeEffect의 조합
데이터는 캐릭터 설정에서 참조하고, 실제 VFX 프리팹은 AssetReference로 연결한다. 원본을 별도로
로드하고 프리팹별 풀을 공유할 수 있도록 제작 데이터와 인스턴스 관리의 경계를 분리했다.

**VFX - Shared**는 여러 조합과 소유자가 사용하는 작은 원시 VFX 7개를 묶었다. 현재 규모에서는
프리팹마다 번들을 만드는 대신 묶음을 유지해 번들 수와 관리 비용을 제한한다. 공용 그룹에 있다는
이유로 상주하지는 않으며, 소유자별 사용권으로 유지한다. 일부 VFX만 오래 사용하더라도 해당 번들을
완전히 언로드할 수 없으므로, 규모가 커지면 전투에서 함께 쓰는 효과와 드물게 쓰는 효과를 구분한다.

**VFX - Shared Dependencies**는 Build Layout에서 중복이 검출된 불꽃 텍스처와 패키지
셰이더·머티리얼을 명시적으로 등록한 그룹이다. 머티리얼의 텍스처 참조는 유지하고 패킹 위치를
한 곳으로 정해 번들 간 복사를 없앴다. 프리팹을 로드할 때 의존성으로 처리되므로 텍스처를 별도로
요청하는 코드가 필요하지 않다. 같은 번들 안에서만 공유하는 텍스처까지 모두 분리하지는 않는다.
[Unity의 명시적·암시적 의존성 설명](https://docs.unity3d.com/Packages/com.unity.addressables@2.9/manual/AssetDependencies.html)

공통 그룹도 로딩 결합을 만든다. 현재 캐릭터 번들은 패키지 폴백 셰이더 때문에 Shared Dependencies에
의존한다. VFX 프리팹을 분리해도 이 의존 번들은 함께 준비된다. 공통 그룹이 커지면 캐릭터가 필요한
렌더링 의존성과 VFX 전용 의존성을 분리할지 다시 판단해야 한다.

### 그룹, 번들과 실제 메모리 로드의 차이

그룹은 에디터의 구성 규칙이고, 번들은 그 규칙으로 생성한 파일이다. 현재 Pack Together를 사용해
각 콘텐츠 그룹의 에셋을 한 콘텐츠 번들에 패킹한다. 빌드 파이프라인은 별도의 Built-in 및 MonoScripts
번들도 생성하므로 전체 번들 수가 콘텐츠 그룹 수와 항상 같지는 않다.

게임 코드는 그룹 이름으로 전투 상황을 전달하지 않는다. 특정 에셋을 요청하면 Addressables가
필요한 번들과 의존 번들을 준비한다. 번들을 로드하는 것과 그 안의 모든 에셋 객체를 로드하는 것은
다르다. 요청한 에셋과 참조된 리소스를 사용하며, 같은 번들의 다른 에셋이 사용 중이면 번들 전체를
언로드할 수 없다. 그래서 번들 크기와 에셋 사용권 수를 실제 메모리 사용량과 구분한다.
[Unity의 메모리 관리 설명](https://docs.unity3d.com/Packages/com.unity.addressables@2.9/manual/MemoryManagement.html)

## 로드와 소유권

| 구성 요소 | 역할 |
|---|---|
| AddressableResources | 원본 프리팹의 실제 Addressables 핸들을 보관 |
| SharedAssetStore<T> | 같은 키의 진행 중·완료된 로드를 공유하고 호출자별 참조 수를 계산 |
| AssetLease<T> | 호출자의 사용권. Dispose는 한 번만 소유권을 반환 |
| ResourceTask | 호출자의 대기만 취소하고 다른 호출자의 공유 로드는 유지 |
| ResourceLifetime | Unity의 지연된 Destroy 완료까지 대기 |
| EffectOwnership | 소유자별 VFX 원본 로드·풀 준비와 해제를 관리 |
| SquadController | 명단의 캐릭터 원본 사용권과 인스턴스를 관리 |

저장소 요청과 사용권 반환은 Unity 메인 스레드에서 호출한다.
마지막 사용권을 반환하면 원본 핸들을 해제한다. 모든 대기자가 취소된 진행 중 로드는 완료를 관찰한 뒤 해제한다.
실패한 로드는 저장소에서 제거하므로 다음 요청은 새로 시도할 수 있다.

### 호출 흐름과 LoadedAgent의 책임

```text
SquadController.Start
  → InitializeAsync → LoadAgentsAsync
    → AddressableResources.AcquirePrefabAsync
      → SharedAssetStore.AcquireAsync
        → Addressables.LoadAssetAsync<GameObject>
    → 캐릭터 Instantiate
    → AgentActionController.PrepareResourcesAsync
      → EffectOwnership.Register → Scope.PrepareAsync
        → 필요한 VFX의 AcquirePrefabAsync → 풀 등록·예열
    → HitFeedbackReceiver의 리소스 준비
    → 초기 캐릭터 활성화
```

LoadedAgent는 로딩을 수행하는 객체가 아니라 캐릭터 한 명의 보관 기록이다. Agent는 생성한
인스턴스, Lease는 원본 프리팹 사용권, ResourceOwners는 VFX를 정리할 소유자 목록이다.
SquadController가 요청·대기·타임아웃·생성·종료 순서를 관리하고, 공용 저장소가 같은 키의 로드를
공유한다. 종료 시 LoadedAgent에 기록한 정보를 사용해 정리하므로 부분 로딩 실패도 추적할 수 있다.

| 확인할 구현 | 위치 |
|---|---|
| 전투 진입, 캐릭터 요청과 인스턴스 보관 | [SquadController](../Assets/04.Scripts/Player/SquadController.cs) |
| 실제 Addressables 로드·Release | [AddressableResources](../Assets/04.Scripts/ResourceManagement/AddressableResources.cs) |
| 공유 로드, 개별 취소와 사용권 반환 | [SharedAssetStore](../Assets/04.Scripts/ResourceManagement/SharedAssetStore.cs) |
| VFX 수집, 준비와 소유자별 정리 | [EffectOwnership](../Assets/04.Scripts/Core/EffectOwnership.cs) |
| 마지막 소유자의 풀 정리 | [EffectPool](../Assets/04.Scripts/Effects/EffectPool.cs) |
| 지연된 파괴 완료와 Play Mode 종료 처리 | [ResourceLifetime](../Assets/04.Scripts/ResourceManagement/ResourceLifetime.cs) |

### 전투 준비

1. 스쿼드는 입력 타깃을 비우고 명단의 캐릭터를 비동기로 로드한다.
2. 비활성 준비 루트 아래에서 생성해 Awake와 행동 시작 전에 VFX를 준비한다.
3. EffectOwnership은 기본 설정과 연결된 AnimationConfig를 순회하고 중복 VFX를 제거한다.
4. VFX 원본을 로드하고 풀 소유자를 등록하며 프리팹의 풀 설정으로 예열한다.
5. 피격 연출도 준비한 뒤 초기 캐릭터를 활성화한다. 인트로는 준비 완료를 기다린다.

스쿼드 준비 제한 시간은 기본 30초이며 VFX 소유 스코프에도 30초 제한을 둔다.
취소·실패·시간 초과 시 부분 생성된 풀과 캐릭터를 정리한다. 실패 후 InitializeAsync로 재시도할 수 있다.
ShutdownAsync는 해당 스쿼드를 종료하므로 다시 진입할 때 새 스쿼드 인스턴스를 사용한다.

### 교체와 종료

일반 캐릭터 교체는 준비된 인스턴스를 활성화·비활성화한다. 명단의 캐릭터와 VFX 사용권은 유지한다.
몬스터와 피격 연출도 소유자가 파괴될 때 사용권을 반환한다.

1. 스쿼드 준비 대기를 취소하고 입력·행동을 정지한다.
2. VFX 소유자를 해제한다. 마지막 소유자가 사라진 풀은 새 대여를 차단한다.
3. 대기 및 대여 중인 풀 인스턴스를 모두 정지·파괴하고 실제 파괴 완료를 기다린다.
4. VFX 사용권을 반환한다. 다른 소유자가 사용하는 원본은 계속 유지한다.
5. 캐릭터 인스턴스를 파괴하고 완료를 기다린 뒤 캐릭터 사용권을 반환한다.

원본 핸들을 먼저 반환하면 인스턴스가 사용하는 머티리얼·텍스처가 언로드될 수 있으므로 순서가 중요하다.
Release 완료가 모든 실제 메모리의 즉시 회수를 보장하지는 않는다. 공유 번들과 Unity 참조는 Memory Profiler로 확인한다.

Play Mode 종료는 플레이 시작부터 등록한 Application.quitting 이벤트로 감지한다. 정리 오브젝트가 아직
생성되지 않았어도 종료 중에는 ResourceLifetime을 만들지 않으며, 기존 파괴 대기는 종료 시 완료 처리한다.
씬 이탈의 파괴 완료 대기와 Play Mode 종료를 구분한다. 활성 스쿼드를 둔 채 바로 Play Mode를 종료하는
회귀 테스트는 StoppingPlayModeWithLoadedSquadDoesNotCreateCleanupObjects다.

## 제작과 마이그레이션

캐릭터는 Addressables에 등록한 뒤 SquadController의 Agent References에 추가한다.
VFX는 등록된 프리팹을 Composite Entry의 Prefab Reference에 연결한다.
EffectTool과 AnimationConfigTool 프리뷰는 GUID를 AssetDatabase로 해석한다.

기존 직렬화 필드는 FormerlySerializedAs와 숨겨진 레거시 필드로 보존한다.
`ZZZ > Addressables > Migrate Runtime References`는 CompositeEffect 전체와 활성 빌드 씬의 직접 참조를
등록된 GUID 참조로 변환하고 Unity API로 저장한다.
2026-09-15 VFX Entry 32개와 SampleScene의 캐릭터 슬롯 1개를 변환했다.

## 빌드와 검증

- `ZZZ > Addressables > Configure Local Development`: Profile, 그룹, 주소와 Label 구성.
- `ZZZ > Addressables > Build Active Content`: 현재 타깃의 Packed 콘텐츠와 Build Layout 생성.
- Build Layout에서 번들 크기, 의존성과 중복 에셋 확인.
- 콘텐츠는 Library/com.unity.addressables에 생성하며 Player 빌드 시 StreamingAssets에 복사한다.

2026-09-15 Android Packed 빌드 결과다. Built-in 및 MonoScripts 번들은 제외한다.

| 그룹 | 번들 수 | 압축 크기 |
|---|---:|---:|
| Characters - Burnice | 1 | 48.17MiB |
| VFX - Shared | 1 | 5.34MiB |
| VFX - Burnice | 1 | 0.23MiB |
| VFX - Shared Dependencies | 1 | 0.09MiB |

초기 Pack Separately에서 검출된 중복 6개를 분리해 현재 중복 에셋은 0개다.
캐릭터 번들은 VFX 프리팹 번들인 Shared와 Burnice에 의존하지 않는다.
패키지 폴백 셰이더를 참조하므로 Shared Dependencies 번들에 대한 의존성은 유지한다.
기존 SG_FX_LightBloom의 GLES3·Vulkan 0 나눗셈 경고는 별도 문제다.

공유 로드·개별 취소·전부 취소 후 늦은 완료·실패 재시도 테스트와 SampleScene 반복 진입·종료,
로드 중 씬 이탈 및 대여 중인 풀 인스턴스 파괴 테스트로 수명주기를 검증한다.
2026-09-15 Unity 컴파일 및 전체 EditMode 테스트 15개가 통과했다.
이 결과는 Play Mode 직접 종료 회귀 테스트 추가 전의 실행 기록이다. 이후 추가한 직접 종료 테스트는
열려 있는 Unity 프로젝트 때문에 별도 배치 실행이 시작되지 못했으며, 수정 코드의 컴파일만 확인했다.
측정 Marker는 ZZZ.Resources.LoadRequest, LoadComplete, Release와 ZZZ.Effects.PoolPrepare, PoolRelease다.
Marker는 호출과 처리 구간을 표시하며 비동기 대기 전체 시간은 요청·완료 시점 사이에서 측정한다.

에디터 씬 테스트는 Use Asset Database 모드다. Android 실기 번들 로딩, Memory Profiler Snapshot과
직접 참조 대비 성능 비교는 다음 검증 단계다.

## 면접에서 설명할 주요 판단

### Addressable 등록만으로 로딩 구조가 바뀌지 않는 이유

등록은 빌드와 주소 해석의 설정이다. 씬이나 CompositeEffect에 직접 프리팹 참조가 남아 있다면
그 참조 경로가 계속 리소스를 연결한다. 실제 전환에서는 캐릭터 슬롯과 VFX Entry를 AssetReference로
바꾸고 레거시 직접 참조를 비웠으며, 런타임 요청과 사용권 반환을 연결했다. 이후 Build Layout에서
캐릭터 번들의 Shared·Burnice VFX 프리팹 번들 의존성이 제거됐는지 확인했다.

### 비동기 로딩으로 해결하는 범위

로드 완료까지 await로 기다리고, 준비가 끝나기 전에는 캐릭터 행동과 입력 전달을 허용하지 않는다.
VFX 설정을 미리 수집하고 풀을 예열하는 이유는 최초 공격 시점에 원본 로드와 풀 준비가 겹치는 것을
피하기 위해서다. 현재 캐릭터와 각 스코프의 VFX 준비는 순차적으로 await한다. 서로 다른 소유자의
동시 요청이 같은 키를 사용하면 공용 작업을 공유한다.

비동기는 모든 작업이 백그라운드에서 실행된다는 뜻이 아니다. Instantiate, 풀 예열과 Unity 객체
접근은 메인 스레드에서 수행하며 여전히 프레임 비용이 발생할 수 있다. 준비 시간이 길어지거나
예열 스파이크가 측정되면 로드 동시성 제한과 프레임별 생성 분산을 검토한다.

### 일반 교체에서 이전 캐릭터를 해제하지 않는 이유

스쿼드 명단의 캐릭터는 다시 선택할 가능성이 높으므로 준비된 인스턴스와 원본을 유지한다.
이는 빠른 교체를 위해 명단 전체의 메모리를 유지하는 선택이다. 메모리 부족으로 비활성 캐릭터를
내리는 정책은 현재 구현하지 않았다. 그 정책을 도입하면 재로드 대기, 입력 제한과 풀 정리를 함께
설계해야 한다. 현재 해제 경계는 일반 교체가 아니라 스쿼드 종료와 VFX 소유자 파괴다.

### 풀링과 원본 핸들을 별도로 관리하는 이유

풀링은 생성한 인스턴스의 재사용이고 Addressables 사용권은 원본 리소스의 유지 조건이다.
Object.Instantiate로 생성한 인스턴스 수는 Addressables 참조 수에 자동으로 반영되지 않는다.
따라서 풀을 유지하는 소유자가 원본 사용권을 보유하고, 마지막 소유자 해제 시 대기·대여 인스턴스를
파괴한 뒤 반환한다. Unity의 Destroy가 지연되므로 호출 직후와 실제 파괴 완료를 구분한다.

AssetLease의 Dispose는 중복 호출되어도 한 번만 반환한다. SharedAssetStore는 게임 호출자의
사용권 수를 관리하고, 실제 Addressables 핸들은 공유 로드 작업 하나가 소유한다. 두 참조 수가
인스턴스 수 또는 Addressables Profiler의 핸들 수와 항상 일치하는 것은 아니다.

### 한 호출자의 취소가 다른 호출자를 중단하지 않는 이유

플레이어와 몬스터가 같은 VFX를 요청한 상태에서 플레이어가 사라져도 몬스터의 준비는 계속되어야
한다. 취소는 해당 호출자의 대기와 사용권만 정리한다. 모든 대기자가 사라져도 이미 진행 중인
Addressables 작업은 완료를 관찰한 뒤 핸들을 반환해 늦게 도착한 결과가 남지 않게 한다.
실패한 작업은 사용권이 정리된 뒤 저장소에서 제거하며 새 요청은 다시 로드할 수 있다.
자동 재시도 횟수, 지수 백오프와 실패 UI는 현재 구현 범위에 포함하지 않았다.

### 개선을 입증하는 기준

현재 확인한 성과는 직접 참조의 분리, 중복 에셋 0건, 공유 로드와 취소 처리, 반복 씬 진입·이탈 후
원본 사용권 및 풀 인스턴스 정리다. 압축 번들 크기는 설치·배포 데이터 크기이며 런타임 메모리 절감
수치로 해석하지 않는다. 에디터 Use Asset Database 테스트도 Android 번들 제공자의 실기 검증을
대체하지 않는다.

변경 전 실기 기준은 [AndroidPerformanceBaseline](AndroidPerformanceBaseline.md)에 있다.
이후 동일 기기·품질·공격 조건에서 전투 진입 시간, 최초 공격 지연, 평균·최대 메모리, GC Alloc과
프레임 스파이크를 비교한다. 로드 전·사용 중·씬 이탈 후 Snapshot과 반복 진입 결과로 실제 회수와
지속 증가 여부를 확인한다. 스쿼드만 종료했을 때 몬스터가 사용하는 공용 리소스가 남는 것은 정상이며,
마지막 관련 소유자가 사라진 뒤를 최종 정리 시점으로 본다.

### 몬스터와 공용 리소스로 확장하는 방향

몬스터의 VFX 소유권은 현재 구조를 사용하지만 몬스터 프리팹 자체의 Addressables 로드·스폰은
아직 전환하지 않았다. 다음 단계에서는 전투 구역 또는 웨이브가 등장할 몬스터 원본을 준비하고,
여러 개체가 같은 원본을 공유하도록 한다. 한 개체의 사망과 원본 반환을 구분하고, 재등장용 풀이
남아 있다면 사용권을 유지한다. 웨이브 간 유지 여부는 재사용 빈도와 실기 메모리로 결정한다.

플레이어와 몬스터가 공유하는 텍스처는 Build Layout에서 번들 간 중복을 확인해 공통 그룹에
등록한다. VFX 공용 텍스처와 캐릭터 공용 텍스처를 무조건 한 그룹에 몰지 않고 사용 범위와
해제 시점이 겹치는지 판단한다. 사운드 독립 로딩이나 원격 배포를 추가할 때도 같은 기준을 적용한다.
