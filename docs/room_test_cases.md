# 강의실 배정 테스트 케이스

`Course.fixed_rooms` / `Course.is_fixed` / `Professor.allowed_rooms` 의 모든 조합과 충돌 시나리오를 정리. 각 케이스마다 입력 / 기대 동작 / 작동 HC / 검증 방법 명시.

## 핵심 정책 요약

| 케이스 | 시간 결정 | 강의실 결정 |
|-------|---------|-----------|
| `is_fixed=True` + `fixed_rooms=[방]` | HC-13 | HC-14 |
| `is_fixed=True` + `fixed_rooms=[]` | HC-13 | **HC-21** (main 교수 prof_room) |
| `is_fixed=False` + `fixed_rooms=[방]` | HC-06 자동 | HC-14 |
| `is_fixed=False` + `fixed_rooms=[]` | HC-06 자동 | **HC-21** (main 교수 prof_room) |

**우선순위**: 과목 `fixed_rooms` 명시 → 교수 무시. 과목 `fixed_rooms` 빈 → 교수 prof_room 따라감.

## 변수 의미 빠른 참고

- `x[(cid, d, p, rid)] ∈ {0,1}` — 과목 c 가 (요일 d, 교시 p, 방 rid) 점유
- `y[(cid, d, p)] ∈ {0,1}` — 점유 indicator (`Σ_r x = K · y`, K=`max(len(fixed_rooms),1)`)
- `prof_room[pid] = {rid: BoolVar}` — 교수 pid 의 main 과목들 묶음 방 (sum=1)

---

## TC-01. 완전 자동 (가장 단순)

**입력**:
```python
Course(id='C1', professor_id='P1', fixed_rooms=[], is_fixed=False, ...)
Course(id='C2', professor_id='P1', fixed_rooms=[], is_fixed=False, ...)
Professor(id='P1', allowed_rooms=[])
```

**기대 동작**:
- C1, C2 시간은 솔버가 HC-06 으로 자동 배정
- C1, C2 강의실은 **같은 방** (P1 prof_room — 전체 9방 중 1개 자동 선택)

**작동 HC**: HC-06 (시간), HC-21 (방 묶음)

**FAIL 시그널**: C1 의 방 ≠ C2 의 방

---

## TC-02. 교수 allowed_rooms 단일

**입력**:
```python
Course(id='C1', professor_id='P1', fixed_rooms=[], ...)
Course(id='C2', professor_id='P1', fixed_rooms=[], ...)
Professor(id='P1', allowed_rooms=['R3'])
```

**기대**: C1, C2 → R3 (선택지 1개라 결정적)

**FAIL**: C1 또는 C2 가 R3 이외 방 사용

---

## TC-03. 교수 allowed_rooms 다중

**입력**:
```python
Professor(id='P1', allowed_rooms=['R1','R2','R3'])
Course(id='C1', professor_id='P1', fixed_rooms=[], ...)
Course(id='C2', professor_id='P1', fixed_rooms=[], ...)
```

**기대**: C1, C2 모두 R1·R2·R3 중 **하나로 묶임** (시드별로 R1 또는 R2 또는 R3, 셋 다 같은 방)

**FAIL**: C1 의 방 ≠ C2 의 방, 또는 R1/R2/R3 외 방 사용

---

## TC-04. 과목 단일방 명시 (교수 무관)

**입력**:
```python
Course(id='C1', fixed_rooms=['D332'], professor_id='P1', ...)
Professor(id='P1', allowed_rooms=[])  # 또는 ['D438'] 등 충돌
```

**기대**: C1 → D332 (과목 우선, 교수 allowed_rooms 무시)

**FAIL**: D332 외 방 사용

**작동 HC**: HC-14

---

## TC-05. 과목 다중방 동시 점유 (캡스톤형)

**입력**:
```python
Course(id='Capstone', hours_per_week=2, block_structure=[2],
       fixed_rooms=['R1','R2','R3','R4','R5','R6'], ...)
```

**기대**:
- 한 시간 블록(2시간 연속)에서 R1~R6 **모두 동시 점유**
- 총 점유 = `hours × K = 2 × 6 = 12`
- 같은 교수의 다른 과목과 시간 겹치면 안 됨 (HC-02)

**FAIL**: K 개 방이 동시에 점유되지 않음, 또는 시간이 분산됨

**작동 HC**: HC-04, HC-06, HC-14

---

## TC-06. 과목·교수 충돌 (과목 우선)

**입력**:
```python
Course(id='C1', fixed_rooms=['R1'], professor_id='P1', ...)
Professor(id='P1', allowed_rooms=['R2'])  # R1 미포함
```

**기대**: C1 → R1 (과목 우선). 교수 제약 무시 → infeasible 안 남.

**FAIL**: INFEASIBLE (이전 버그) 또는 R2 사용

---

## TC-07. is_fixed 단독 (강의실 미명시)

**입력**:
```python
Course(id='C1', is_fixed=True, fixed_slots=[(2,1),(2,2),(2,3)],
       fixed_rooms=[], professor_id='P1', ...)
Professor(id='P1', allowed_rooms=[])
```

**기대**:
- 시간: 수요일 1~3교시 (HC-13 강제)
- 강의실: P1 prof_room 1개 (3시간 모두 같은 방)
- P1 의 다른 자동 과목과도 **같은 방**

**FAIL**: 슬롯마다 다른 방, 또는 시간이 (2,1)/(2,2)/(2,3) 외에 들어감

**작동 HC**: HC-13 (시간), HC-21 (방)

---

## TC-08. is_fixed + 강의실 명시

**입력**:
```python
Course(id='C1', is_fixed=True, fixed_slots=[(2,1)],
       fixed_rooms=['D327'], professor_id='P1', ...)
```

**기대**: 수요일 1교시 D327 (시간·방 모두 박힘)

**작동 HC**: HC-13, HC-14

---

## TC-09. 같은 main 교수 다중 과목 + 과목별 다른 fixed_rooms

**입력**:
```python
Course(A, professor_id='P1', fixed_rooms=[])
Course(B, professor_id='P1', fixed_rooms=[])
Course(C, professor_id='P1', fixed_rooms=['D332'])  # 명시
Professor(id='P1', allowed_rooms=[])
```

**기대**:
- A, B → P1 prof_room (예: R5)
- C → D332 (과목 우선)
- **P1 교수가 두 방을 사용** (R5 + D332). 의도된 동작.

**FAIL**: C 가 R5 사용, 또는 A/B 가 D332 사용

---

## TC-10. 팀티칭 — main 만 따라감

**입력**:
```python
Course(X, professor_id='P1', coteach_profs=['P2'], fixed_rooms=[])
Course(Y, professor_id='P2', fixed_rooms=[])
Professor(id='P1', allowed_rooms=[])
Professor(id='P2', allowed_rooms=[])
```

**기대**:
- X → P1 prof_room (예: R1)
- Y → P2 prof_room (예: R2, R1 과 다를 수 있음)
- P2 입장: 본인 main 인 Y 는 R2, 협동으로 들어간 X 는 R1 → 두 방 사용

**FAIL**: X 가 P2 prof_room 따라감

---

## TC-11. main 교수 자동 과목 없음

**입력**:
```python
Course(C1, professor_id='P1', fixed_rooms=['D327'])  # 명시뿐
Professor(id='P1', allowed_rooms=[])
```

**기대**: P1 의 prof_room 변수 자체가 안 만들어짐 (자동 과목 0개). HC-21 미적용.

**FAIL**: 솔버 빌드 시 prof_room_P1 변수가 모델에 들어감

---

## TC-19. Cross 분반 페어 길이 1 블록 시작 정렬 (HC-19 확장)

**입력**:
```python
CrossGroup(base_ids=['A', 'B'])
Course(A-01, A-02, professor_id='P1', block_structure=[2, 1], hours=3)
Course(B-01, B-02, professor_id='P2', block_structure=[2, 1], hours=3)
```

**기대**:
- HC-15(분반 인접) + HC-16(cross cyclic) 결합으로 두 분반의 길이 1 블록이 같은 날 인접 슬롯에 들어감
- 그 페어의 **작은 sp ∈ {1, 3, 6, 8}** — 즉 두 분반 점유가 (1,2) / (3,4) / (6,7) / (8,9) 중 하나
- 시간표 중간에 어색하게 떠있는 (sp=2,4,7,9 시작) 케이스 회피

**작동 HC**: HC-19 (확장: 길이 2 OR cross 멤버 분반 페어 길이 1)

**FAIL**: 분반 페어가 (2,3) 또는 (4,5)/(5,6)(점심 끼임은 이미 invalid)/(7,8) 페어 형성 시 작은 sp ∉ {1,3,6,8}

---

## TC-12. Cross 그룹 (강의실 영향 X)

**입력**:
```python
CrossGroup(base_ids=['A', 'B'])
Course(A-01, professor_id='P1', fixed_rooms=[])
Course(A-02, professor_id='P1', fixed_rooms=[])
Course(B-01, professor_id='P2', fixed_rooms=[])
Course(B-02, professor_id='P2', fixed_rooms=[])
```

**기대**:
- 시간 cyclic shift 동기화: A-01 ↔ B-02 같은 시간, A-02 ↔ B-01 같은 시간 (HC-16)
- 강의실은 A-01, A-02 → P1 prof_room / B-01, B-02 → P2 prof_room (서로 무관)

---

## TC-13. 재수강 안전 분반 (강의실 영향 X)

**입력**:
```python
RetakeScenario(current_grade=4, retake_base_id='GA1004')
# GA1004 가 2학년 전필, 4학년 학생이 재수강
```

**기대**:
- GA1004 의 분반 중 적어도 1개는 4학년 전필과 시간 겹치지 않음 (HC-17)
- 강의실은 HC-21 정책 따름 (영향 없음)

---

## TC-14. 분반 인접 (HC-15) + 같은 prof_room

**입력**:
```python
Course(C-01, professor_id='P1', fixed_rooms=[], block_structure=[2])
Course(C-02, professor_id='P1', fixed_rooms=[], block_structure=[2])
Professor(id='P1', allowed_rooms=[])
```

**기대**:
- HC-15: 같은 base_id 두 분반 + 같은 교수 → 같은 날 인접 슬롯
- HC-21: 두 분반 같은 방
- 결과: C-01 (월 1-2교시 R3) + C-02 (월 3-4교시 R3) 같은 방 인접 시간
- HC-01 (방 동시 1수업) 자동 만족 (시간이 다름)

**FAIL**: 두 분반 다른 방, 또는 같은 시간 같은 방

---

## TC-15. 강의실 0개 시스템 (엣지)

**입력**: `ROOMS = []`

**기대**: 모든 자동 과목에 대해 `candidates=[]` → prof_room 변수 안 만들어짐. HC-04 가 `Σ x = hours·K` 강제하지만 x 자체에 방 후보가 없으니 **INFEASIBLE**.

(현실에서는 발생 안 함. 데이터 검증으로 차단해야)

---

## TC-16. 교수 unavailable_slots 충돌

**입력**:
```python
Professor(id='P1', allowed_rooms=['R1'],
          unavailable_slots=[(0, 1), (0, 2), ...])
Course(id='C1', professor_id='P1', hours_per_week=3, ...)
```

**기대**: P1 이 (월 1, 2 교시) 사용 못함 (HC-03). 그 외 시간에서 R1 사용.

**FAIL**: 월 1, 2 교시에 C1 점유

---

## TC-17. is_fixed 시간 vs 교수 unavailable 충돌

**입력**:
```python
Professor(id='P1', unavailable_slots=[(2, 1)])  # 수요일 1교시 불가
Course(id='C1', is_fixed=True, fixed_slots=[(2, 1)], professor_id='P1', ...)
```

**기대**: HC-13 강제 (수 1교시) ↔ HC-03 금지 (수 1교시) → **INFEASIBLE**

(데이터 입력 단계에서 검증해야 — 현재 솔버는 그냥 INFEASIBLE 반환)

---

## TC-18. fixed_rooms 가 교수 unavailable 강의실?

**상태**: 강의실 자체에는 unavailable 개념 없음. 교수의 시간 unavailable 만 있음. → 교수 시간 ↔ 과목 시간만 충돌 가능. 강의실은 별 충돌 없음.

---

## 검증 방법

### 자동 검증 (Python 스크립트)

```python
# tests/check_room_consistency.py 같은 곳에
from data_source import COURSES, PROFESSORS, ROOMS, CROSSES
from domain import derive_auto_retakes
from csp import build_and_solve_diverse

retakes = derive_auto_retakes(COURSES, (1,2,3,4))
status, sols = build_and_solve_diverse(
    COURSES, PROFESSORS, ROOMS,
    total_solutions=1, time_limit_sec=300, per_solve_time_sec=60,
    crosses=CROSSES, retakes=retakes,
    sc01_weight=1, sc02_weight=1, sc03_weight=1)

assert status == "OPTIMAL", f"풀이 실패: {status}"
a = sols[0]
course_map = {c.id: c for c in COURSES}

# TC-01/03 검증: 같은 main 교수의 자동 배정 과목 → 같은 방
by_pid = {}
for (cid, d, p, rid) in a:
    c = course_map[cid]
    if c.fixed_rooms:  # 명시된 과목은 교수 묶음 무관
        continue
    by_pid.setdefault(c.professor_id, set()).add(rid)
for pid, rooms in by_pid.items():
    assert len(rooms) == 1, f"교수 {pid} 가 두 방 사용: {rooms}"
print("✓ 교수 단위 강의실 일관성")

# TC-04 검증: fixed_rooms 단일방 명시 → 그 방만 사용
for (cid, d, p, rid) in a:
    c = course_map[cid]
    if c.fixed_rooms and len(c.fixed_rooms) == 1:
        assert rid == c.fixed_rooms[0], \
            f"{cid}: 명시방 {c.fixed_rooms[0]} 와 다른 {rid} 사용"
print("✓ 과목 단일방 명시 우선")

# TC-05 검증: 다중방 K 개가 동시 점유
from collections import defaultdict
multi_room_courses = [c for c in COURSES if len(c.fixed_rooms) > 1]
slot_rooms = defaultdict(set)
for (cid, d, p, rid) in a:
    slot_rooms[(cid, d, p)].add(rid)
for c in multi_room_courses:
    K = len(c.fixed_rooms)
    occupied_slots = [k for k, rs in slot_rooms.items()
                      if k[0] == c.id and len(rs) == K]
    assert len(occupied_slots) == c.hours_per_week, \
        f"{c.id}: {K}방 동시 점유 슬롯 수 불일치"
print("✓ 다중방 동시 점유")
```

### GUI 시각 확인

1. `python main.py` 실행
2. 통합 시간표에서:
   - 같은 교수의 과목들이 같은 방에 있는지 (셀 3번째 줄 강의실 확인)
   - `fixed_rooms` 명시 과목이 그 방으로 가는지
   - 캡스톤 같은 다중방 과목이 한 시간대에 여러 방에 있는지
3. 사이드바 "교수" 탭 에서 allowed_rooms 변경 후 재계산 → 변경된 후보 안에서 묶이는지

---

## 참고: 정책 의사결정 이력

- **D2-a (과목 우선)**: 과목 `fixed_rooms` 명시되면 교수 `allowed_rooms` 와 충돌해도 과목이 이김. 이전엔 HC-21 이 hard 충돌로 INFEASIBLE 됐는데 (TC-06), 이젠 HC-21 이 `fixed_rooms` 명시 과목 자동 제외.
- **HC-13 분리**: 이전엔 시간+방 모두 박았으나 사용자 의견 반영해 시간만 박음. 강의실은 HC-14/HC-21 이 결정.
- **HC-21 강화**: 단순 "allowed_rooms 외 금지" → "교수 단위 한 방으로 묶음" (시드 무작위 분산 방지).
