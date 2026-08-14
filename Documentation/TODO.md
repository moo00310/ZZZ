# ZZZ Unity Project — TODO.md


## 진행중


## 최근 완료

- [x] **페이로드 기반 Notify** — `TrackNotify`의 공통 타이밍과 `[SerializeReference] NotifyPayload`를 분리하고 기존 에셋을 마이그레이션
- [x] **공용 타격 판정** — 플레이어·몬스터가 같은 `HitService`를 사용하며 Sphere/Cone/Box/Capsule/ExpandingSphere와 Overlap/Sweep 지원
- [x] **이펙트 원점 바인딩** — `CompositeEffectEntry.BindingKey`와 `HitData.EffectKey`로 풀링된 실제 이펙트 Transform을 캐릭터별 스코프에서 추적
- [x] **타격 범위 디버그** — AnimationConfigTool Scene View 편집 기즈모와 플레이 중 Game View 디버그 라인 지원
- [x] **FireBeam 풀 반납 검증** — 최상위 ParticleSystem의 Stop Action을 Callback으로 설정하고 재사용 확인


## 클라이언트 프로그래머 포트폴리오 우선순위

> 목표: 프레임워크의 기능 수보다 **완성된 전투 루프, 안정성, 제작 효율과 측정 결과**를 먼저 증명한다.
> 포트폴리오에서는 게임플레이를 먼저 보여주고, Animation/Effect Tool은 이를 빠르고 안전하게 제작하는 근거로 제시한다.

### P0 — 플레이 가능한 전투 수직 단면

- [ ] **적 공격 루프 완성** — 적이 거리와 상태에 따라 접근·공격을 선택하고 `OpenIncomingAttack`을 실제 AI 흐름에서 호출
- [ ] **공방 상호작용 연결** — 공격 예고 → 회피/패링 → 피격/반격이 테스트 키 없이 연속해서 동작
- [ ] **공격 결과를 게임 상태로 연결** — 체력, 경직, 다운과 사망 중 현재 데모 범위에 필요한 상태를 구현
- [ ] **타격감 연출 연결** — 히트스톱, 카메라 반응, 사운드와 피격 이펙트를 Notify 흐름에 연결
- [ ] **짧은 플레이 구간 완성** — 처음 실행한 사람이 설명 없이도 2~3분 동안 전투 시스템의 핵심을 확인할 수 있는 데모 구성

### P1 — 재사용성과 안정성 증명

- [ ] **공용 ConfigState의 두 번째 소비자 완성** — 몬스터의 이동·공격·피격을 같은 실행 엔진으로 구동해 플레이어 전용 구조가 아님을 증명
- [ ] **데이터만으로 공격 추가 사례 작성** — 코드 수정 없이 Config/Module 조합으로 새 공격을 제작하고 작업 과정과 소요 시간을 기록
- [ ] **전이 로직 EditMode 테스트** — 전이 윈도우 경계, 입력 버퍼 소비, OnRelease/OnEndIfMatched와 피격 인터럽트 검증
- [ ] **타격 판정 테스트** — Sweep 경계, 동일 대상 중복 타격 방지, EffectKey 원점 바인딩과 판정 종료 검증
- [ ] **이펙트 수명 테스트** — 지연 재생, 구간 이펙트 중단, Config 전환, 풀 반납과 재사용 시 상태 초기화 검증
- [ ] **에디터 데이터 검증 강화** — 중복 Section, 누락된 대상/클립, 잘못된 Window와 BindingKey를 저장 또는 재생 전에 명확히 표시
- [ ] **핵심 코드 경계 점검** — `ConfigState`가 계속 비대해지지 않도록 입력 판단은 Condition/Trigger, 구간 동작은 Module, 외부 연출은 Notify에 유지

### P2 — 포트폴리오 전달력

- [ ] **60~90초 대표 영상 제작** — 완성 전투 → 타임라인에서 전이/Notify 편집 → 실행 결과 확인 순서로 구성
- [ ] **README 첫 화면 개선** — 대표 GIF/영상, 실행 방법, 조작법과 핵심 기술 3~5개를 문서 상단에 배치
- [ ] **실행 가능한 빌드 제공** — PC 빌드를 우선 제공하고 배포 환경에서 정상 실행되는지 확인
- [ ] **설계 트레이드오프 정리** — Animator 전이 대신 Config 실행기를 선택한 이유, 얻은 이점과 중앙 실행기 복잡도·직렬화 위험을 함께 기록
- [ ] **본인 기여와 에셋 출처 명시** — 코드·툴·데이터 구성·연출 중 직접 작업한 범위와 캐릭터/애니메이션/VFX 원본 출처를 구분
- [ ] **성과를 수치로 기록** — 새 공격 제작 시간, 런타임 GC, 프레임 시간, 풀링 전후 Instantiate 또는 메모리 수치를 가능한 범위에서 비교

### 후순위 원칙

- 툰 셰이더와 RenderFeature는 렌더링 직무용 확장보다 현재 전투 데모의 시각적 완성에 필요한 최소 범위를 우선한다.
- Addressables는 실제 캐릭터/VFX 로드·해제와 메모리 전후를 측정할 수 있는 단계에서 진행한다.
- 새로운 범용 시스템을 추가하기 전에 현재 시스템을 사용하는 실제 콘텐츠와 검증 사례를 먼저 추가한다.


## 예정 (로드맵)

- [ ] **몬스터 루트모션/추격** — `MonsterMotor`에 플레이어와 같은 `OnAnimatorMove` 기반 `deltaPosition/deltaRotation` 처리와 워프·추격을 구현(현재 Idle+Hit 제자리 재생)
- [ ] **적 공격 시스템** — `OpenIncomingAttack` 호출 주체(실제 적 AI). 현재 테스트키 K로 시뮬레이션
- [ ] **이펙트 시스템 잔여** — 지연 재생과 구간 이펙트 트레일의 플레이 모드 실전 검증. 소켓 바인딩·풀링·프리웜(EffectPrewarmer)·**구간형(지속) 노티파이**(`TrackNotify.EndNormalizedTime`+`EffectHandle`)·툴은 구현 완료
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

#### 화염방사 충돌 최적화 비교

- [ ] 현재 `ExpandingCone`을 기준으로 아래 판정 방식을 동일한 공격 데이터와 몬스터 배치에서 비교
  - `ExpandingCone`: `OverlapSphereNonAlloc` 후보를 거리와 각도로 필터링
  - 확장 캡슐/원기둥: 진행 방향의 길이와 반경을 시간에 따라 확장
  - 이동 구체 + `SphereCast`: 화염 속도에 맞춰 논리 구체를 이동시키고 이전 위치부터 현재 위치까지 Sweep
  - 박스: `OverlapBoxNonAlloc`으로 화염 전체 영역을 한 번에 근사
- [ ] Hit Notify에 실험용 판정 모드를 추가하되, 피격 쿨다운과 타깃별 중복 방지 조건은 모든 방식에서 동일하게 유지
- [ ] 실제 파티클에 콜라이더를 추가하지 않고, 이동 구체는 `HitService`가 관리하는 논리 판정으로 구현
- [ ] Deep Profile을 끈 동일 빌드에서 몬스터 수와 공격 반복 시간을 고정하고 다음 항목을 기록
  - 프레임당 물리 쿼리 횟수와 `HitService` CPU 시간
  - 쿼리 후보 수와 최종 유효 타격 수
  - GC Alloc과 최대·평균 프레임 시간
  - 낮은 프레임레이트에서의 관통 또는 타격 누락
  - 판정 경계와 이펙트의 시각적 일치도
- [ ] 반복 공격의 타격 누락·의도하지 않은 중복 타격이 없는 후보 중 성능이 가장 좋은 방식을 최종 선택

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
- [ ] **`SendMessage` → 이벤트 릴레이** — `DispatchNotify`의 `default` 분기(`Ctx.GameObject.SendMessage(EventName, DontRequireReceiver)`, Camera/Sound/Custom 공용)를 **캐릭터별 이벤트 릴레이**(강타입 `event Action<string>`)로 교체. SendMessage 단점: 리플렉션 비용 / 오타 조용한 실패 / 타입 안전성 없음.
  - 왜 릴레이(전역 버스 아님): Notify 연출은 대부분 그 캐릭터 자신이 반응(피격·사운드) → 인스턴스별 릴레이면 그 캐릭터를 직접 구독해 **인스턴스 구분·`Source` 필터 불필요**, 전역 정적 상태 없음(캐릭터와 함께 GC). 제약: 데이터는 공유 SO(`TrackNotify`)라 페이로드는 문자열 `EventName` 유지(**UnityEvent 불가**).
  - 구현: 캐릭터에 릴레이 컴포넌트(`event Action<string> OnNotify`) → `ConfigState`가 `ConfigContext`의 릴레이 참조로 발행. 구독자(사운드/카메라/피격 핸들러)는 같은 캐릭터에서 `OnEnable`/`OnDisable`로 구독·해제. `ConfigContext.GameObject`(SendMessage 전용)를 **릴레이 참조로 교체** → 설정처 `MonsterActionController`/`PlayerActionController` 2곳 수정.
  - 남는 숙제: 구독 리스너가 없으면 무동작 → 실제 연출 붙일 때 함께 구현. 한 이벤트를 여러 캐릭터/전역 시스템이 들어야 하면 그때 이벤트 버스·SO 이벤트 채널로 승격.

## 발견된 버그

- (없음)
