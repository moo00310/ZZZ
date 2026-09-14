# ZZZ Unity Project — TODO

> 다음 작업은 **Addressables/AssetBundle → 툰 셰이딩 → Android 통합 빌드**를 중심으로 진행한다.
> 각 단계는 `변경 전 측정 → 구현 → 동일 조건 재측정 → 결과 기록`까지 완료해야 끝난 것으로 본다.
> Deep Profile은 원인 추적에만 사용하고 최종 비교 수치는 끈 상태에서 기록한다.

## 최근 완료

- [x] **페이로드 기반 Notify** — `TrackNotify`의 공통 타이밍과 `[SerializeReference] NotifyPayload`를 분리하고 기존 에셋 마이그레이션
- [x] **공용 타격 판정** — `HitService`에서 Sphere/Cone/Box/Capsule/ExpandingSphere와 Overlap/Sweep 지원
- [x] **이펙트 원점 바인딩** — 풀링된 실제 이펙트 Transform을 캐릭터별 스코프에서 추적
- [x] **타격 범위 디버그** — AnimationConfigTool Scene View 기즈모와 플레이 중 Game View 디버그 라인 지원
- [x] **FireBeam 풀 반납 검증** — 최상위 ParticleSystem의 Stop Action을 Callback으로 설정하고 재사용 확인
- [x] **핵심 코드 경계 분리** — Condition/Trigger, Module, `CharacterNotifyRunner`의 책임 분리
- [x] **문자열 Notify 제거** — 전용 payload와 `ConfigEventType`을 사용하고 `SendMessage` 제거

## 1. Addressables/AssetBundle 리소스 관리

> Addressables를 런타임 API로 사용하고, 하위 AssetBundle의 구성·의존성·캐싱·해제 원리를 함께 검증한다.

### 설계와 기반 구성

- [ ] **현재 리소스 기준값 측정** — 직접 참조 상태의 시작 메모리, 전투 진입 시간과 VFX 반복 재생 시 최고 메모리 기록
- [ ] **Addressables 패키지와 Profile 구성** — 개발용 Local 경로를 먼저 구성하고 이후 Remote 확장이 가능한 구조로 설정
- [ ] **그룹·주소·Label 규칙 정의** — 캐릭터, 공용 VFX와 캐릭터 전용 VFX의 분류 및 Bundle Pack Mode 결정
- [ ] **AssetBundle 구성 검증** — Build Layout과 Analyze 결과로 중복 에셋, 번들 의존성과 예상치 못한 포함 관계 확인
- [ ] **리소스 소유권 정의** — Addressables 핸들, `EffectService`, `EffectPool`과 생성 인스턴스의 소유·해제 순서 문서화

### 실제 리소스 적용

- [ ] **VFX Addressable 전환** — 공용 VFX 하나를 비동기로 로드해 기존 이펙트 풀과 연결
- [ ] **풀과 핸들 수명주기 구현** — 풀 제거 → 인스턴스 파괴 → Addressables `Release` 순서를 보장
- [ ] **캐릭터 Addressable 전환** — 캐릭터 로드·교체 시 이전 인스턴스와 핸들 해제를 검증
- [ ] **중복 요청과 실패 처리** — 동시 로드 중복 방지, 취소, 타임아웃과 잘못된 주소의 복구 흐름 구현
- [ ] **씬 전환 수명주기 검증** — 로드 중 전환, 호출자 파괴와 반복 진입에서도 참조와 인스턴스가 남지 않는지 테스트

### Profiler 검증

- [ ] **로드 구간 측정 지점 추가** — 요청, 로드 완료, 풀 준비와 해제 구간에 `ProfilerMarker` 적용
- [ ] **Memory Profiler 전후 비교** — 로드 전, 사용 중, 풀 정리 후 Snapshot으로 메모리 회수 여부 확인
- [ ] **반복 부하 테스트** — 동일 VFX를 정해진 횟수만큼 재생하며 로드 시간, 최고 메모리, GC Alloc과 프레임 스파이크 기록
- [ ] **Addressables 결과 문서화** — 직접 참조 방식과 변경 후의 메모리·로딩 수치 및 설계 트레이드오프 정리

## 2. URP 툰 셰이딩

> Burnice 캐릭터 한 명을 기준으로 최소 기능을 완성한 뒤, 기능을 하나씩 추가하며 GPU 비용을 측정한다.

### 기본 셰이더

- [ ] **기존 렌더링 기준값 측정** — 동일 장면에서 기존 머티리얼의 CPU/GPU 프레임 시간, Batches와 SetPass Calls 기록
- [ ] **툰 셰이더 기본 패스 구현** — Base Map, 메인 라이트, 2단 명암과 그림자 색상 지원
- [ ] **캐릭터 표현 확장** — 림 라이트, 하이라이트와 Emission을 독립적으로 켜고 조절할 수 있게 구현
- [ ] **URP 필수 패스 검증** — Forward, ShadowCaster, DepthOnly/DepthNormals 동작 확인
- [ ] **전투 연출 연결** — 피격·강조 효과를 머티리얼 복제 없이 `MaterialPropertyBlock`으로 적용
- [ ] **ShaderGUI와 키워드 정리** — 사용하지 않는 기능의 Variant가 불필요하게 생성되지 않도록 관리

### 아웃라인과 RendererFeature

- [ ] **아웃라인 방식 비교** — Inverted Hull과 화면 공간 방식의 품질, 캐릭터 겹침과 모바일 비용 비교
- [ ] **아웃라인 RendererFeature 구현** — 선택한 방식으로 PC/Mobile Renderer에서 동작하도록 구성
- [ ] **렌더 패스 검증** — Frame Debugger로 실행 순서, 입력 Texture와 불필요한 패스 여부 확인

### Profiler 검증

- [ ] **기능별 비용 측정** — 기본 명암, 림, 하이라이트, 그림자와 아웃라인을 하나씩 켜며 GPU 시간 비교
- [ ] **캐릭터 수 증가 테스트** — 동일 캐릭터 수를 단계적으로 늘려 Batches, SetPass Calls와 CPU/GPU 시간을 기록
- [ ] **모바일 셰이더 최적화** — `half` 정밀도, 텍스처 샘플 수, 추가 광원과 그림자 비용을 점검
- [ ] **툰 셰이더 결과 문서화** — 기존/툰 셰이더의 품질 이미지, 프레임 비용과 선택한 트레이드오프 정리

## 3. Android 빌드와 실기 최적화

> Addressables와 툰 셰이딩 전에 스모크 빌드로 환경을 확인하고, 두 기능을 통합한 뒤 최종 실기 성능을 측정한다.

### 초기 스모크 빌드

- [ ] **Android 프로젝트 설정 검증** — 애플리케이션 ID, IL2CPP, ARM64, Min/Target SDK와 빌드 씬 확인
- [ ] **모바일 품질 설정 검증** — 기존 Mobile RP Asset/Renderer의 HDR, Render Scale, 그림자와 후처리 설정 확인
- [x] **Development Build 실기 실행** — Android 기기에서 실행, 입력, 화면 비율과 그래픽 오류 확인
- [ ] **초기 실기 베이스라인 기록** — CPU·메모리·렌더링 기준값 기록 완료, Android Vulkan GPU 시간 측정은 별도 도구로 보완 필요 (`AndroidPerformanceBaseline.md`)

### 고정 성능 시나리오

- [ ] **재현 가능한 테스트 장면 구성** — 기기, 품질, 캐릭터·몬스터 수, 공격 종류와 반복 시간을 고정
- [ ] **측정 양식 작성** — 빌드 버전, 평균·최대 프레임 시간, GC Alloc, 메모리, 로딩 시간과 발열 상태 기록
- [ ] **필요 구간에 ProfilerMarker 적용** — `HitService`, 리소스 로드와 이펙트 재생 구간을 Timeline에서 구분

### 화염방사 판정 방식 비교

- [ ] **화염방사 판정 방식 비교**
  - 동일한 공격 데이터와 몬스터 배치에서 ExpandingCone, 확장 Capsule, SphereCast와 Box 비교
  - 물리 쿼리 횟수, `HitService` CPU, 후보/유효 타격 수, GC와 평균·최대 프레임 시간 기록
  - 낮은 프레임레이트의 타격 누락, 중복 타격과 시각적 일치도를 확인해 최종 방식 선정
- [ ] **동일 판정 조건 보장** — 모든 방식에 같은 레이어, 피격 쿨다운과 대상별 중복 방지 조건 적용
- [ ] **실기기 반복 측정** — Deep Profile을 끈 동일 빌드에서 공격 횟수와 몬스터 수를 고정해 비교
- [ ] **최종 판정 방식 확정** — 정확도 조건을 만족하는 후보 중 가장 안정적인 방식을 선택하고 근거 기록

### 통합 빌드와 최종 검증

- [ ] **Addressables 콘텐츠 빌드 검증** — 번들, 카탈로그와 런타임 로드 경로가 Android 빌드에서 정상 동작
- [ ] **툰 셰이더 빌드 검증** — 필요한 Variant와 RenderFeature가 포함되고 분홍색 머티리얼이나 누락 패스가 없는지 확인
- [ ] **장시간 전투 테스트** — 동일 전투를 10분 이상 반복하며 메모리 증가, GC 스파이크와 발열 이후 성능 확인
- [ ] **30/60fps 프레임 예산 평가** — 33.3ms/16.7ms 초과 구간을 찾아 CPU 또는 GPU 병목으로 구분
- [ ] **확인된 병목 개선** — 측정으로 확인된 병목 하나 이상을 수정하고 동일 조건의 전후 수치 비교
- [ ] **최종 결과 정리** — Addressables, 툰 셰이더와 화염방사 판정의 설계·측정·개선 결과를 포트폴리오 문서에 연결

## 발견된 버그

- (없음)
