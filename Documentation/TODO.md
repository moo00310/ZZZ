# ZZZ Unity Project — TODO.md

## 진행중

- [ ] **패리 다듬기** — deflect 분기(`HitTrigger.TryDeflect`) + counter follow-up 링크 배선 완료. 윈도우(`ParryModule` Start/End)·연출 튜닝 및 검증 중

## 예정 (로드맵)

- [ ] **강화 상태 자동 전환** — 일정 콤보 히트 후 강화 콤보 config로 자동 전환
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

## 발견된 버그

- (없음)

## 완료

| 시스템 | 설명 | 커밋 |
|--------|------|------|
| **타겟 워프** | 락온 facing — 워프 윈도우 동안 매 프레임 타겟 주시. config: `WarpFaceTarget`·`WarpTurnSpeed`. 접근 거리는 클립 전진 루트모션 담당 | 2e96871 |
| **루트 회전 / TurnBack** | TurnBack 루트 회전 이슈 해결 | ced2881, e6db859 |
| **회피 이동 튐 수정** | Evade 루트모션 튐 버그 → 루트모션 방식 변경·평균 전진속도 적용 | a63e907 |
| **카운터** | 적 공격 윈도우 기반 판정 → deflect→Counter 동작 확인 (입력 바인딩·config 오써링) | — |
| **특수스킬** | Special 입력(Q / 패드 RB) → SpecialConfig(Enhance_01) 진입. 전용 키 push + 노멀 콤보 중 Special 캔슬 링크 양쪽 지원 | — |
