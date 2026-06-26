# ZZZ Unity Project — TODO.md

## 최근 완료

- [x] **패링(Parry)** — `ParryModule`(활성 윈도우) + `ParryTrigger`(push 진입). 활성 중 적 공격이 닿으면 `HitTrigger`가 피격 대신 쳐냄(`ParryAid_L/H`)으로 분기. i-frame('무시')과 대칭('반격으로 응수')
- [x] **강화공격(Attack_Normal_Enhance)** — 방향(앞W/뒤S) 우선 → 중립이면 적과의 거리(근/중/원)로 진입 섹션 분기. 콤보 링크가 못 받은 입력의 전역 폴백 트리거

### Attack_Normal_Enhance 리워크 (이번 묶음)
- [x] **Special → Attack_Normal_Enhance 전면 리네임** — enum / 트리거 클래스 / 입력 액션 / 에디터 툴
- [x] **링크 타이밍·조건 확장** — `OnWindowMiss → OnRelease`(키 릴리스, 홀드 상태 기준), `RequireHeld`(홀드 차지 루프), `EntryOffset`(중간 프레임 진입), `OnEndIfMatched`
- [x] **타겟 조준 통합(FaceTarget)** — Snap/Lock-on을 `FaceWindow` 하나로, 이동 워프(`EnableTracking`)와 **독립 토글**
- [x] **트리거 4종 데이터화** — `[Serializable]`로 각자 설정 보유 → `PlayerStateMachine` 인스펙터 폴드 노출 (런타임 의존은 `Init`)
- [x] **모듈 추가 드롭다운** — 등록된 `SectionModule` 타입 자동 나열 (`WindowModule` 베이스로 구간 편집 일반화)
- [x] **에디터 정리** — 콤보 입력 단일 드롭다운, 라이브 `Held` 표시, 프리뷰 전용 Bip/RM 자동감지 시 숨김
- [x] **Explode에서 E 재입력 → `Attack_ExSpecial_01`** (Explode 섹션 링크), 임시 더블탭 코드 제거

## 진행중

- [ ] **ExSpecial 모션 제작 & 연결** — `Attack_ExSpecial_01` 모션 신규 제작. **진입은 배선 완료**(Explode 재생 중 E 재입력 → ExSpecial 링크). 모션·연출만 채우면 됨

## 예정 (로드맵)

- [ ] **적 공격 시스템** — `OpenIncomingAttack` 호출 주체(실제 적 AI). 현재 테스트키 K로 시뮬레이션
- [ ] **노티파이 트랙 확장** — 본 소켓 바인딩, 구간형 노티파이, 이펙트 풀링
- [ ] **툰 셰이더** — `ShaderGUI` + 렌더 타겟 디버거

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
