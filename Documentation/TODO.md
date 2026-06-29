# ZZZ Unity Project — TODO.md

## 진행중


## 예정 (로드맵)

- [ ] **몬스터 루트모션/추격** — `MonsterController`의 `IConfigMover` no-op들(`FlushRootPos`/워프 등)을 실제 구현(현재 제자리 재생). **이때 같이**: `PlayerController.ComputeRootDeltaLocal`의 코어 델타 로직과 `RootMotionTracker`(현재 에디터 프리뷰/테스트 전용)의 **중복을 공용 헬퍼로 통합** → 플레이어·몬스터·프리뷰가 한 소스 공유, 기존 `RootMotionTrackerTests`가 런타임 경로까지 검증
- [ ] **적 공격 시스템** — `OpenIncomingAttack` 호출 주체(실제 적 AI). 현재 테스트키 K로 시뮬레이션
- [ ] **노티파이 트랙 확장 (이펙트)** — 본 소켓 바인딩, 구간형 노티파이, 이펙트 풀링. 별도 이펙트 툴은 만들지 않고 **`AnimationConfig`의 Notify 시스템을 확장**해 처리
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
- [ ] **Notify 이펙트 풀링** — `ConfigState.DispatchNotify`의 `Object.Instantiate(EffectPrefab)`를 오브젝트 풀로 교체. 콤보 반복 발동 시 GC/인스턴스화 비용 누적 → 프리팹별 풀 Get/Release, VFX 재생 끝나면 자동 반납 (위 노티파이 트랙 확장의 '이펙트 풀링'과 동일 작업)
- [ ] **`SendMessage` 대체 검토** — 같은 `DispatchNotify`의 `SendMessage(EventName)`는 리플렉션 할당 → 이벤트/델리게이트 디스패치로

## 발견된 버그

- (없음)
