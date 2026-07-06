# ZZZ Unity Project — TODO.md


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
- [ ] **`SendMessage` → 이벤트 릴레이** — `DispatchNotify`의 `default` 분기(`Ctx.GameObject.SendMessage(EventName, DontRequireReceiver)`, Camera/Sound/Custom 공용)를 **캐릭터별 이벤트 릴레이**(강타입 `event Action<string>`)로 교체. SendMessage 단점: 리플렉션 비용 / 오타 조용한 실패 / 타입 안전성 없음.
  - 왜 릴레이(전역 버스 아님): Notify 연출은 대부분 그 캐릭터 자신이 반응(피격·사운드) → 인스턴스별 릴레이면 그 캐릭터를 직접 구독해 **인스턴스 구분·`Source` 필터 불필요**, 전역 정적 상태 없음(캐릭터와 함께 GC). 제약: 데이터는 공유 SO(`TrackNotify`)라 페이로드는 문자열 `EventName` 유지(**UnityEvent 불가**).
  - 구현: 캐릭터에 릴레이 컴포넌트(`event Action<string> OnNotify`) → `ConfigState`가 `ConfigContext`의 릴레이 참조로 발행. 구독자(사운드/카메라/피격 핸들러)는 같은 캐릭터에서 `OnEnable`/`OnDisable`로 구독·해제. `ConfigContext.GameObject`(SendMessage 전용)를 **릴레이 참조로 교체** → 설정처 `MonsterStateMachine`/`PlayerStateMachine` 2곳 수정.
  - 남는 숙제: 구독 리스너가 없으면 무동작 → 실제 연출 붙일 때 함께 구현. 한 이벤트를 여러 캐릭터/전역 시스템이 들어야 하면 그때 이벤트 버스·SO 이벤트 채널로 승격.

## 발견된 버그

- (없음)
