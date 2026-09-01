# Git 커밋 & 브랜치 컨벤션

ZZZ 프로젝트의 git 작업 규칙. 혼자 작업하지만 현업(브랜치 + PR + 리뷰) 흐름을 연습하기 위한 문서.

---

## 1. 커밋 메시지 형식 (Conventional Commits)

```
<type>(<scope>): <제목>

<본문 — 무엇을·왜 (선택)>

<푸터 — 이슈 링크 등 (선택)>
```

예시:
```
feat(combat): 강공격(Strong) 좌클릭 홀드 추가

- 좌클릭 Tap/Hold 분리로 강공격 진입
- Idle/Run에서 홀드 시 Attack_ExSpecial_02로 분기
```

### 규칙
- **제목**: 50자 내외, 마침표 없음, "무엇을 했는지" 간결하게 (한글 OK)
- **본문**: 한 줄 비우고 작성. *어떻게*보다 **무엇을·왜**를 적는다 (코드를 보면 "어떻게"는 알 수 있음)
- 제목에 **"A + B" / "A 하고 B"** 가 들어가면 → 보통 커밋을 나눠야 한다는 신호

---

## 2. Type (필수)

| type | 언제 | 예시 |
|---|---|---|
| `feat` | 새 기능 | `feat(combat): 강공격 좌클릭 홀드 추가` |
| `fix` | 버그 수정 | `fix(input): 회피 후 멈칫하는 입력 누수 수정` |
| `refactor` | 기능 변화 없는 구조 개선/이름변경 | `refactor(input): ComboInput enum 정리` |
| `docs` | 문서만 | `docs: 커밋 컨벤션 추가` |
| `chore` | 빌드/설정/잡일 (코드 동작 무관) | `chore: 에디터 설정 정리` |
| `style` | 포맷/공백 (로직 무관) | `style: 들여쓰기 정리` |
| `perf` | 성능 개선 | `perf(fx): Notify 이펙트 풀링` |
| `test` | 테스트 추가/수정 | `test: 콤보 전이 테스트` |

> **핵심:** 한 커밋엔 type 하나. `feat`과 `refactor`가 섞이면 나눈다.

---

## 3. Scope (선택, 이 프로젝트 기준)

변경이 속한 영역. 괄호 안에 소문자로.

| scope | 영역 |
|---|---|
| `combat` | 공격/콤보/전투 로직 |
| `input` | 입력(InputSystem), ComboInput |
| `state` | 상태머신/CharacterActionRunner |
| `anim` | 애니메이션 config/클립 |
| `tool` | 에디터 툴(AnimationConfigTool) |
| `camera` | 카메라 |
| `fx` | 이펙트 |

scope가 애매하면 생략해도 됨: `feat: ...`

---

## 4. 좋은 커밋 단위 (가장 중요)

**한 커밋 = 한 논리 변경 (atomic).** 판단 기준:

- **"+/and" 테스트** — 제목을 "A 하고 B"로 써야 하면 두 커밋
- **타입 혼합 테스트** — `feat`과 `refactor`가 섞이면 분리
- **되돌리기 테스트** — 이 커밋만 `revert`해도 다른 게 안 깨지면 OK
- **빌드 테스트** — 각 커밋 시점에 컴파일/실행되면 OK (`git bisect` 생존)
- 줄 수보다 **"리뷰어가 한 호흡에 이해되는 한 가지"** 가 단위

### 비결: 끝나고 쪼개지 말고, 단계마다 커밋
작업을 다 한 뒤 한꺼번에 올리면 파일이 겹쳐서 쪼개기 어렵다.
**한 단계 끝낼 때마다 그 자리에서 커밋**하면 작업 흐름이 곧 커밋 단위가 된다.

### 예외: 같이 묶어야 하는 변경
서로 없으면 **빌드/로드가 깨지는** 변경은 한 커밋으로.
예) `ComboInput enum 순서 변경` + `.asset의 Attack 인덱스 리맵` — 따로 올리면 중간 커밋에서 프로젝트가 깨진다.

---

## 5. 브랜치 컨벤션

```
<type>/<짧은-설명>
```
예: `feat/strong-attack`, `fix/dodge-input`, `refactor/combo-input`

- `main`엔 직접 커밋하지 않는다 (작업 시작 시 브랜치부터 분기)
- 브랜치는 작게, 한 PR이 한 주제

---

## 6. 작업 흐름 (한 사이클)

```bash
# 1. main에서 브랜치 분기 (작업 시작할 때!)
git switch main && git pull
git switch -c feat/strong-attack

# 2. 단계마다 작게 커밋
git add <관련 파일>
git commit -m "feat(combat): ..."

# 3. 푸시
git push -u origin feat/strong-attack

# 4. GitHub에서 PR 생성 (푸시 후 뜨는 링크)

# 5. 셀프 리뷰 (Files changed 탭에서 diff 확인)

# 6. 머지 — 드롭다운에서 "Squash and merge" 선택!
#    (버튼이 "Confirm squash and merge"로 바뀌어야 제대로 선택된 것)

# 7. 정리
git switch main && git pull
git branch -d feat/strong-attack
```

### ⚠️ GitHub 머지 UI 주의
머지 버튼 옆 **드롭다운 ▾** 에서 방식을 고른다:
- **Squash and merge** (권장) — 브랜치의 커밋들을 main에 **1개로 합침**. 이력이 깔끔.
- **Create a merge commit** (기본값) — 커밋들이 그대로 남고 머지 커밋이 추가됨.

기본값이 "Create a merge commit"이라, 스쿼시하려면 **매번 직접 골라야** 한다.

---

## 7. 빠른 예시 모음

```
feat(combat): 강공격(Strong) 좌클릭 홀드 추가
fix(state): Run에서 강공 진입 시 방향 안 맞던 문제 수정
refactor(input): ComboInput enum 정리 (None/Any 상단, Strong/Enhance 리네임)
docs: Git 커밋 컨벤션 추가
chore: 디버그 HUD/에디터 설정 정리
perf(fx): Notify 이펙트 Instantiate를 오브젝트 풀로 교체
```
