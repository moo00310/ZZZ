# ZZZ Unity Project — TODO.md

## 최근 완료

- [x] **`ConfigState` 공유 엔진화** — 플레이어 구상 타입 의존을 인터페이스(`ConfigDriving.cs`: `IConfigContext`/`IConfigMover`/`IAnimatorBridge`/`IConfigSignals`)로 추출. 전이 조건도 다형성 `LinkCondition`(`InputCondition`/`AlwaysCondition`)으로 분리(`ILinkConditionContext` 주입). `PlayerAnimatorBridge` → `AnimatorBridge`로 공용화
- [x] **몬스터(Durahan) 스캐폴드** — 같은 `ConfigState`로 Idle+Hit 구동(`MonsterStateMachine`/`MonsterController`/`MonsterContext`/`MonsterConditionContext`). 피격 시 앞/뒤 분기 후 Hit config 인터럽트, 경직 A안(히트 쿨다운). 라이브 모니터 다중 캐릭터 추적
- [x] **플레이어 공격 → 몬스터 피격 파이프라인** — `MeleeHitter`(센서 범위 기반) / `EffectHitVolume`(이펙트 범위 기반, A안)로 `HitTarget.TakeDamage` 호출
- [x] **패링(Parry)** — `ParryModule`(활성 윈도우) + `ParryTrigger`(push 진입). 활성 중 적 공격이 닿으면 `HitTrigger`가 피격 대신 쳐냄(`ParryAid_L/H`)으로 분기. i-frame('무시')과 대칭('반격으로 응수')
- [x] **강화공격(Attack_Normal_Enhance)** — 방향(앞W/뒤S) 우선 → 중립이면 적과의 거리(근/중/원)로 진입 섹션 분기. 콤보 링크가 못 받은 입력의 전역 폴백 트리거
- [x] **이펙트 풀링 시스템 + 전용 툴** — `CompositeEffect`(조합 SO, 프리팹 직접 참조) + `EffectService`/`EffectPool`(프리팹 단위 풀, 자동 반납) + `EffectTool`/AnimationConfigTool **Effect 탭**(애니 프레임 보며 발동 시점·소켓·시차 편집, 소켓 Simulate 프리뷰). `DispatchNotify`의 `Instantiate` → `EffectService.Play` 교체 완료. 상세: [EffectArchitecture.md](EffectArchitecture.md)

### Attack_Normal_Enhance 리워크 (이번 묶음)
- [x] **Special → Attack_Normal_Enhance 전면 리네임** — enum / 트리거 클래스 / 입력 액션 / 에디터 툴
- [x] **링크 타이밍·조건 확장** — `OnWindowMiss → OnRelease`(키 릴리스, 홀드 상태 기준), `RequireHeld`(홀드 차지 루프), `EntryOffset`(중간 프레임 진입), `OnEndIfMatched`
- [x] **타겟 조준 통합(FaceTarget)** — Snap/Lock-on을 `FaceWindow` 하나로, 이동 워프(`EnableTracking`)와 **독립 토글**
- [x] **트리거 4종 데이터화** — `[Serializable]`로 각자 설정 보유 → `PlayerStateMachine` 인스펙터 폴드 노출 (런타임 의존은 `Init`)
- [x] **모듈 추가 드롭다운** — 등록된 `SectionModule` 타입 자동 나열 (`WindowModule` 베이스로 구간 편집 일반화)
- [x] **에디터 정리** — 콤보 입력 단일 드롭다운, 라이브 `Held` 표시, 프리뷰 전용 Bip/RM 자동감지 시 숨김
- [x] **Explode에서 E 재입력 → `Attack_ExSpecial_01`** (Explode 섹션 링크), 임시 더블탭 코드 제거

## 진행중


## 예정 (로드맵)

- [ ] **몬스터 루트모션/추격** — `MonsterController`의 `IConfigMover` no-op들(`FlushRootPos`/워프 등)을 실제 구현(현재 제자리 재생). **이때 같이**: `PlayerController.ComputeRootDeltaLocal`의 코어 델타 로직과 `RootMotionTracker`(현재 에디터 프리뷰/테스트 전용)의 **중복을 공용 헬퍼로 통합** → 플레이어·몬스터·프리뷰가 한 소스 공유, 기존 `RootMotionTrackerTests`가 런타임 경로까지 검증
- [ ] **적 공격 시스템** — `OpenIncomingAttack` 호출 주체(실제 적 AI). 현재 테스트키 K로 시뮬레이션
- [ ] **이펙트 시스템 잔여** — 플레이 모드 실전 검증(풀 반납/지연 재생), **구간형(지속) 노티파이**(현재는 시점 발동만 — Looping+Fixed 반납으로 우회 중). 소켓 바인딩·풀링·툴은 완료(위 최근 완료 참조)
- [ ] **툰 셰이더 + RenderFeature** — 셀 셰이딩/림라이트 셰이더 + `ShaderGUI`(키워드 자동 관리), 아웃라인/포스트 RenderFeature. 전투 중 셰이더 연출은 `MaterialPropertyBlock`으로 적용(머티리얼 오염 금지). 렌더 타겟 디버거

## 모바일 빌드 & 최적화 (목표)

> 최종 목표: **Android 실기 빌드 + 안정적 프레임(목표 30/60fps)**. 모바일 타겟이 있어야 최적화·메모리 관리에 명확한 기준이 생김.
> 원칙: **측정 먼저(Profiler) → 병목 확인 → 최적화**. 감으로 고치지 않기.

### 1) 빌드 환경
- [ ] Android 빌드 타겟 설정 (IL2CPP, ARM64) — 실기 1대에서 돌아가는 최소 빌드부터
- [ ] 모바일용 URP Renderer/Quality 에셋 분리 — MSAA·HDR·그림자 해상도 등 모바일 프로파일

### 2) 측정 (베이스라인)
- [ ] Profiler / Frame Debugger / Memory Profiler로 현재 프레임·draw call·메모리 베이스라인 기록
- [ ] 전투 연출(스킬 난사) 시 스파이크 구간 식별

### 3) 메모리 관리 — 리소스 비동기 로딩 (Addressables)
> 무거운 에셋을 필요할 때 비동기 로드, 안 쓰면 `Release`로 메모리에서 해제. 모바일에서 효과 큼.
- [ ] Addressables 패키지 설치 + Group 기본 세팅
- [ ] **스킬 VFX/파티클(02.Effects) Addressable 전환** — 스킬 사용 직전 로드, 종료 후 `Release` (메모리 before/after 비교)
- [ ] 캐릭터 프리팹(Burnice 등) Addressable 로드 — 캐릭터 전환 시 이전 인스턴스 `Release`

### 4) 렌더/성능 최적화
- [ ] 툰 셰이더 모바일 대응 — half precision, 연산 절감 (예정인 툰 셰이더 작업과 연계)
- [ ] SRP Batcher / GPU Instancing 확인, draw call 절감
- [ ] 텍스처 압축(ASTC)·해상도 정리, 이펙트 파티클 수 예산 설정

### 5) GC / 런타임 메모리 (코드)
> 런타임 핫패스(전투 로직)는 이미 무할당 양호. 아래만 정리하면 됨.
- [x] **디버그 HUD 빌드 제외** — `PlayerStateHUD`/`AnimatorLayerHUD`를 `#if UNITY_EDITOR || DEVELOPMENT_BUILD`로 가드. 릴리스에서 빌드 용량 + 매 프레임 OnGUI 문자열 GC 제거
- [x] **Notify 이펙트 풀링** — `ConfigState.DispatchNotify`의 `Object.Instantiate` → `EffectService.Play`(프리팹별 풀 Get/Release + 자동 반납 + 프리워밍)로 교체 완료. 상세: [EffectArchitecture.md](EffectArchitecture.md)
- [ ] **`SendMessage` 대체 검토** — 같은 `DispatchNotify`의 `SendMessage(EventName)`는 리플렉션 할당 → 이벤트/델리게이트 디스패치로

## 발견된 버그

- (없음)
