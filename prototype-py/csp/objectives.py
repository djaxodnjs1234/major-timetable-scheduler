"""SC objective expressions over CP-SAT decision variables.

Phase 1 의 ``model.Minimize(...)`` 인자로 들어가는 식들을 모은 모듈.

SC ID 는 lex 우선순위 순서:
  - SC-01 : 월오전/금오후 회피  (term 작을수록 좋음)
  - SC-02 : 교수별 강의 요일 수 ≤ 임계 (term = excess 합)
  - SC-03 : 블록 페어 요일 차 ≥ 2 (term = 위반 과목 수)

레이어링: ``PENALIZED_SLOTS`` 와 SC-02 정책 상수가 csp 에 있는 건 솔버
(objective)와 scoring (raw 점수) 둘 다 참조하기 때문. 솔버가 scoring 을
import 하면 단방향 의존 (``domain ← csp ← scoring``) 이 깨진다.
"""
from collections import defaultdict

from csp.constraints import DAYS, VALID_PERIODS, course_prof_ids

# ════════════════════════════════════════════════════════════════════
#  SC 솔버 단계 튜닝 노브 (Phase 2 가 강제하는 slack: opt + max(0, slack))
#    SC-01:  PENALIZED_SLOTS, SC01_SLACK_ABS
#    SC-02:  SC02_*, SC02_SLACK_ABS
#    SC-03:  SC03_SLACK_ABS
#
#  Lex 우선순위 (solver.py): SC-01 → SC-02 → SC-03 → Phase 2 enumerate.
# ════════════════════════════════════════════════════════════════════

# SC-01: 월요일 1~4교시(오전), 금요일 6~9교시(오후) 회피.
PENALIZED_SLOTS = (
    {(0, p) for p in (1, 2, 3, 4)}
    | {(4, p) for p in (6, 7, 8, 9)}
)

# Phase 2 가 강제하는 SC-01 slack: ``sc01 <= opt + max(0, SC01_SLACK_ABS)``.
#   0  = SC-01 최적 그대로 강제 (opt 슬롯만 허용)
#   3  = 3슬롯 여유
SC01_SLACK_ABS = 10

# SC-02 정책 상수 (솔버·ranking 양쪽이 공유).
SC02_DAY_THRESHOLD = 3       # 강의 요일 수가 이 값을 초과하면 패널티.
SC02_MIN_COURSES = 2         # 강의 과목이 이 개수 미만인 교수는 분모에서 제외.
SC02_EXCLUDE_FIXED = True    # is_fixed 과목은 강의 분포 산정에서 제외.
SC02_EXCLUDE_COTEACH = True  # coteach_profs 는 산정 제외 (메인 교수만 카운트).

# Phase 2 가 강제하는 SC-02 slack: ``sc02 <= opt + max(0, SC02_SLACK_ABS)``.
#   0 = SC-02 최적 그대로 강제 (excess 합 정확히 opt)
#   1 = 1 명 추가 위반 허용
SC02_SLACK_ABS = 1

# Phase 2 가 강제하는 SC-03 slack: ``sc03 <= opt + max(0, SC03_SLACK_ABS)``.
# 단위는 "SC-03 위반 과목 수" (블록 페어 요일 차 < 2 인 과목).
#   0 = SC-03 최적 그대로 강제
SC03_SLACK_ABS = 0


def sc01_penalty_term(x, courses, rooms):
    """월/금 회피 슬롯에 들어간 (course, room) 변수의 합 — 작을수록 좋음."""
    return sum(
        x[(c.id, d, p, r.id)]
        for c in courses
        for r in rooms
        for (d, p) in PENALIZED_SLOTS
    )


def sc02_penalty_term(model, x, courses, rooms):
    """교수별 강의 요일 수가 SC02_DAY_THRESHOLD 초과한 양의 합 — 작을수록 좋음.

    SC02_EXCLUDE_FIXED, SC02_EXCLUDE_COTEACH, SC02_MIN_COURSES 정책은
    ranking 단계와 동일하게 적용.
    """

    def _pids_of(c):
        if SC02_EXCLUDE_COTEACH:
            return (c.professor_id,)
        return tuple(course_prof_ids(c))

    courses_by_prof = defaultdict(list)
    for c in courses:
        if SC02_EXCLUDE_FIXED and c.is_fixed:
            continue
        for pid in _pids_of(c):
            courses_by_prof[pid].append(c)

    eligible = [pid for pid, lst in courses_by_prof.items()
                if len(lst) >= SC02_MIN_COURSES]
    if not eligible:
        return 0

    over_cap = DAYS - SC02_DAY_THRESHOLD
    if over_cap <= 0:
        return 0

    excess_terms = []
    for pid in eligible:
        prof_courses = courses_by_prof[pid]

        # day_used[d] = 교수 pid 가 day d 에 한 슬롯이라도 강의하면 1.
        day_used = []
        for d in range(DAYS):
            cells = [
                x[(c.id, d, p, r.id)]
                for c in prof_courses
                for p in VALID_PERIODS
                for r in rooms
            ]
            used = model.NewBoolVar(f"sc02_used_{pid}_{d}")
            if cells:
                model.AddMaxEquality(used, cells)
            else:
                model.Add(used == 0)
            day_used.append(used)

        excess = model.NewIntVar(0, over_cap, f"sc02_excess_{pid}")
        model.Add(excess >= sum(day_used) - SC02_DAY_THRESHOLD)
        excess_terms.append(excess)

    return sum(excess_terms) if excess_terms else 0


def sc03_penalty_term(model, day_vars_by_course, courses):
    """다중 블록 과목 중 "블록 페어 요일 차 < 2" 인 과목 수 — 작을수록 좋음.

    HC-06 의 day_vars 를 재활용. is_fixed 과목은 day_vars 가 없어 자동 제외.
    """
    bad_indicators = []
    for c in courses:
        if not c.block_structure or len(c.block_structure) < 2:
            continue
        day_vars = day_vars_by_course.get(c.id)
        if not day_vars or len(day_vars) < 2:
            continue

        pair_bads = []
        for i in range(len(day_vars) - 1):
            for j in range(i + 1, len(day_vars)):
                diff = model.NewIntVar(
                    -DAYS, DAYS, f"sc03_diff_{c.id}_{i}_{j}")
                abs_diff = model.NewIntVar(
                    0, DAYS, f"sc03_absdiff_{c.id}_{i}_{j}")
                model.Add(diff == day_vars[i] - day_vars[j])
                model.AddAbsEquality(abs_diff, diff)

                pair_bad = model.NewBoolVar(f"sc03_pairbad_{c.id}_{i}_{j}")
                model.Add(abs_diff <= 1).OnlyEnforceIf(pair_bad)
                model.Add(abs_diff >= 2).OnlyEnforceIf(pair_bad.Not())
                pair_bads.append(pair_bad)

        if not pair_bads:
            continue

        course_bad = model.NewBoolVar(f"sc03_coursebad_{c.id}")
        model.AddBoolOr(pair_bads).OnlyEnforceIf(course_bad)
        model.AddBoolAnd([pb.Not() for pb in pair_bads]
                         ).OnlyEnforceIf(course_bad.Not())
        bad_indicators.append(course_bad)

    return sum(bad_indicators) if bad_indicators else 0
