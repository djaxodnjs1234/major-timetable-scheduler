# AGENTS.md

Codex-compatible project instructions. These preserve the useful guidance from
`CLAUDE.md` without relying on Claude-specific commands, slash commands,
agents, or tool assumptions.

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## 5. Plan File & Test Workflow

**Every non-trivial task: write a plan file first, test after implementing, then archive.**

1. **Create the plan file** — Before starting, create a plan file (`.md`) in
   `docs/implementation/`. Include steps and a verify check for each step.
   - **Write the plan file in English** — it is read by AI, so keep it clear and concise
     in English regardless of the conversation language.
   - **Number the plan file** — prefix the filename with a zero-padded sequence number
     based on the existing files in `docs/implementation/` and `docs/implementation/archive/`
     (e.g. `06-edit-existing-timetable.md`).
2. **Implement** — Follow the plan.
3. **Test** — When implementation is done, always run tests (the project's test suite,
   or run the real app to confirm behavior). Never report a task complete without testing.
4. **Archive** — When the task is fully done, move the plan file to
   `docs/implementation/archive/`.

Trivial tasks (typo fixes, etc.) are left to judgment.

## 6. Explain Hard Concepts

**When a reply uses a technical concept the user may not know, add a glossary at the bottom.**

The user has told us that terms like "DI singleton", "session mode", "CRUD",
"binding-target instance", and "snapshot data" are hard to follow.

- When a user-facing reply leans on a non-trivial technical term, append a short
  **용어 정리 / Glossary** section at the end of that reply.
- Define each term in one or two plain sentences, tied to this project where possible.
- Only include terms actually used in that reply; do not pad. Skip the glossary entirely
  for replies that use no such terms.
- The conversation language is Korean — write the glossary in Korean.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

---

# Project: 전공 시간표 CSP 프로토타입

**OR-Tools CP-SAT** + **Tkinter GUI**. WPF 이식 고려한 layered + UI-agnostic ViewModel 구조.

## 실행

```bash
cd prototype-py
pip install -r requirements.txt
python main.py
```

테스트·린터·빌드 단계 없음. 진입점은 `prototype-py/main.py` 하나. 솔버 노브(`N_SOLUTIONS`, `M_TOP`, `TIME_LIMIT_SEC`)는 main.py 상단 상수.

## 의존성 화살표 (단방향)

```
domain ← csp ← scoring ← ui
domain ← data ← main
ui ← main
```

## 폴더 구조

```
prototype-py/        Python 프로토타입 (CP-SAT + Tkinter)
  domain/      순수 도메인 (의존성 없음)
  csp/
    constraints/   HC 함수 (concern별 분리)
    objectives.py  SC 패널티 + 정책 상수
    solver.py      4단계 lex 빌드/솔버
  scoring/     SC raw 점수 + ranking
  data/        xlsx 로더
  ui/
    viewmodel.py   AppState (Tkinter 비의존)
    app.py         TimetableApp 셸
    views.py       렌더
    sidebar.py     3탭 (교과목/교수/강의실)
    editors/       다이얼로그
  main.py
  data_source.py   GUI 가 덮어쓰는 자동 생성 데이터
wpf/                 C#/WPF 본 제품 (포팅 대상)
docs/                공유 설계 문서 + UI 디자인 목업
```

## 도메인 핵심 필드

`Course` ([domain/models.py](prototype-py/domain/models.py)):
- `fixed_rooms: List[str]` — **빈=자동 1개, 길이 N>1=N방 동시 점유** (캡스톤형)
- `is_fixed` + `fixed_slots` — 사전 배정 시간 슬롯 (HC-13). `fixed_slots = [(day, period), ...]` — **시간만**. 강의실은 `fixed_rooms` 또는 교수 prof_room 따라감.
- `block_structure: List[int]` — 주당 시수의 블록 분할
- `coteach_profs` — 팀티칭 추가 교수

`Professor`:
- `allowed_rooms: List[str]` — **빈=무제한**, 비면 그 방들만 (팀티칭 시 교집합)
- `unavailable_slots` — 외래/겸임 출강 표현

`CrossGroup` (HC-16) / `RetakeScenario` (HC-17).

## 결정 변수

- `x[(course_id, day, period, room_id)] ∈ {0,1}`
- `y[(course_id, day, period)] ∈ {0,1}` — 슬롯 점유 indicator. **다중방 정합 위해 도입**: `Σ_r x = K · y` (K=`len(fixed_rooms)` or 1). 분반·학년·교수·Cross·Retake 교차 제약은 모두 `y` 사용.

## 강의실 충돌 해결 (과목 우선 정책)

과목에 방이 명시되면(`is_fixed` 또는 `fixed_rooms` 비어있지 않음) 그 방이 **무조건 우선**. 교수 `allowed_rooms` 와 충돌해도 과목이 이김. HC-21(교수 `allowed_rooms`)은 **방 미지정·솔버 자동 배정** 과목에만 적용.

**HC-13 은 시간만 박음** — 강의실은 아래 표대로 별도 결정. 상세 케이스: [docs/room_test_cases.md](docs/room_test_cases.md).

| 과목 상태 | 방 결정 주체 |
|----------|------------|
| `fixed_rooms=[방]` 명시 | HC-14 (그 방으로 고정) |
| `fixed_rooms=[방1..방N]` 다중 | HC-14 (N 개 동시 점유) |
| `fixed_rooms=[]` (비어있음) | HC-21 (main 교수 prof_room — `allowed_rooms` 후보 중 1방, 비면 전체 중 1방) |

## HC/SC 매핑

| HC | 의미 | 위치 |
|----|------|------|
| 01 | 강의실 동시 1개 | basic |
| 02 | 교수 동시 1개 (팀티칭, **y**) | basic |
| 03 | 교수 불가능 시간 | basic |
| 04 | 시수 충족 (`Σ x = hours·K`) | basic |
| 06 | 블록 연속 + 다중방 동시 점유 | blocks |
| 08 | 분반 중복 금지 (**y**) | basic |
| 11 | 같은 학년 중복 (분반/Cross 제외, **y**) | basic |
| 12 | 점심(5교시) 금지 | basic |
| 13 | is_fixed 과목의 시간(요일·교시) 슬롯 점유 강제 — 강의실은 별도 결정 | basic |
| 14 | `fixed_rooms` 외 사용 금지 | blocks |
| 15 | 같은 교수 분반 인접 | blocks |
| 16 | Cross cyclic shift (**y**) | grouping |
| 17 | 재수강 안전 분반 (**y**) | grouping |
| 18 | 블록 페어 요일 차 ≤ 2 | blocks |
| 19 | 블록 시작 교시 ∈ {1,3,6,8} — 길이 2 블록 OR cross 그룹 멤버 (모든 길이) | blocks |
| 20 | 같은 과목 블록들 다른 요일 | blocks |
| 21 | 교수 단위 강의실 일관성 — main 교수의 자동 배정 과목들을 한 방으로 묶음 (후보: `allowed_rooms` 또는 전체) | blocks |

| SC | 의미 | weight |
|----|------|--------|
| 01 | 월오전/금오후 회피 | 1 |
| 02 | 교수당 강의 요일 ≤ 3 | 1 |
| 03 | 블록 페어 요일 간격 ≥ 2 | 1 |

## 솔버 흐름 ([csp/solver.py](prototype-py/csp/solver.py))

`build_and_solve_diverse` 4단계 lex:
1. Phase 1A → SC-01 opt 측정 후 `sc01_bound` 잠금
2. Phase 1B → SC-02 opt (SC-01 제약 유지) 잠금
3. Phase 1C → SC-03 opt (SC-01/02 유지) 잠금
4. Phase 2 → 본 모델 + 모든 slack hard 제약 + 시드 loop 로 다양성 확보

각 phase 는 일회용 모델 (objective 비우는 API 일관성 문제로 분리). 시드 loop 가 `enumerate_all_solutions` 보다 다양성↑.

## UI 핵심 패턴

- **AppState** ([ui/viewmodel.py](prototype-py/ui/viewmodel.py)) — Tkinter 비의존. 명령 실행 후 `_emit(kind)` 로 구독자에게 알림.
- **Dirty 버퍼** — 편집은 `_dirty++` + 첫 변경 시 backup. `commit_recalc` 솔버 1회 실행, 실패 시 자동 `rollback`. **편집 다이얼로그에서 인라인 솔버 호출 금지**.
- **Persist 콜백** — `state.set_persist_callback(fn)` 주입. 강의실/교수 변경(`add_professor`/`add_room`/`update_professor`/`delete_*`)은 `_persist_now()` 로 즉시 `data_source.py` 저장 (silent).
- **`expand_all_grades: bool`** — 통합 시간표 학년 컬럼. False(default)=그 요일에 교과목 있는 학년만, True=4학년 모두 강제.
- **Sidebar 3탭** — 교과목 / 교수 / 강의실. 교수·강의실 추가는 **인라인 입력란** (다이얼로그 X). 교수 편집은 더블클릭 → `open_prof_editor`.

## 시간 슬롯

요일 0=월 ~ 4=금. 교시 1~9. 5교시=점심(HC-12 금지). VALID_PERIODS = [1,2,3,4,6,7,8,9]. `time_label(p) = f"{8+p:02d}:00"`.

## 데이터 소스 전환

[main.py](prototype-py/main.py) `_load_data()`:
1. `data_source.py` 의 `COURSES` 가 비어있지 않으면 그걸 사용.
2. 비면 `개설강좌 편람.xlsx` 를 [data/xlsx_loader.py](prototype-py/data/xlsx_loader.py) 로 파싱.

GUI `📤 코드 내보내기` → `data_source.py` 덮어씀. 강의실/교수 변경 시 자동 silent 저장.

## 확장 포인트

- **새 HC** → [csp/constraints/](prototype-py/csp/constraints/) 적절 파일에 `add_hcNN_*` 추가, [`__init__.py`](prototype-py/csp/constraints/__init__.py) re-export, [`solver.py`](prototype-py/csp/solver.py) `_build_model()` 등록. 슬롯 점유 비교 필요하면 `y` 사용.
- **새 SC** → [`csp/objectives.py`](prototype-py/csp/objectives.py) `scNN_penalty_term` + [`scoring/soft_constraints.py`](prototype-py/scoring/soft_constraints.py) raw 함수 + `SC_KEYS`/`SC_WEIGHTS` 갱신, `solver.py` 에 Phase 1X 추가.
- **새 AppState 명령** → `viewmodel.py` 메서드 + `_emit("kind")`. UI 는 `_on_state_change` 분기.
- **새 다이얼로그** → [ui/editors/](prototype-py/ui/editors/) 모듈, `__init__.py` re-export.
