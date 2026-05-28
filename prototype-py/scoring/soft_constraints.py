"""Soft Constraints — feasibility 만족된 해들에 점수를 매겨 상위 M개 선별.

각 SC 함수는 단일 assignment 를 받아 0.0~1.0 사이 정규화 점수 반환 (1.0 = 최적).
total = Σ SC_WEIGHTS[k] · raw_score[k] ** SC_PENALTY_POWER[k]

ID 는 lex 우선순위 순서 (SC-01 → SC-02 → SC-03):
  - SC-01 : 월오전/금오후 회피
  - SC-02 : 교수별 강의 요일 수 ≤ 임계
  - SC-03 : 다중 블록 과목의 블록 페어 요일 간격 ≥ 2 (HC-18 가 |diff|≤2 보장)
"""
from collections import defaultdict

from csp.constraints import course_prof_ids, DAYS
from csp.objectives import (
    PENALIZED_SLOTS,
    SC02_DAY_THRESHOLD, SC02_MIN_COURSES,
    SC02_EXCLUDE_FIXED, SC02_EXCLUDE_COTEACH,
)

# SC-01 raw 점수 분모. raw = max(0, 1 - bad_count / SC01_RAW_DENOMINATOR).
SC01_RAW_DENOMINATOR = 20

SC_KEYS = ("SC01", "SC02", "SC03")
SC_WEIGHTS = {
    "SC01": 1,
    "SC02": 1,
    "SC03": 1,
}
SC_PENALTY_POWER = {
    "SC01": 1,
    "SC02": 1,
    "SC03": 1,
}


# --------------------------------------------------------------------
# SC-01: 월/금 회피 슬롯 점유 비율 최소화
# --------------------------------------------------------------------
def _sc01_special_slots(assignment):
    if not assignment:
        return 1.0
    bad = sum(1 for (_, d, p, _) in assignment if (d, p) in PENALIZED_SLOTS)
    return max(0.0, 1.0 - bad / SC01_RAW_DENOMINATOR)


# --------------------------------------------------------------------
# SC-02: 교수별 강의 요일 수가 임계 초과한 양의 합 (정규화)
#   정책: csp/objectives.py 의 SC02_* 상수 (솔버·ranking 공유).
# --------------------------------------------------------------------
def _sc02_prof_concentration(assignment, professors, courses):
    if not professors:
        return 1.0

    def _pids_of(c):
        if SC02_EXCLUDE_COTEACH:
            return (c.professor_id,)
        return course_prof_ids(c)

    courses_by_prof = defaultdict(list)
    for c in courses:
        if SC02_EXCLUDE_FIXED and c.is_fixed:
            continue
        for pid in _pids_of(c):
            courses_by_prof[pid].append(c)

    eligible = {pid for pid, lst in courses_by_prof.items()
                if len(lst) >= SC02_MIN_COURSES}
    if not eligible:
        return 1.0

    course_map = {c.id: c for c in courses}
    days_by_prof = defaultdict(set)
    for (cid, d, _, _) in assignment:
        c = course_map.get(cid)
        if c is None:
            continue
        if SC02_EXCLUDE_FIXED and c.is_fixed:
            continue
        for pid in _pids_of(c):
            if pid in eligible:
                days_by_prof[pid].add(d)

    over_cap = DAYS - SC02_DAY_THRESHOLD
    if over_cap <= 0:
        return 1.0

    total_excess = sum(max(0, len(ds) - SC02_DAY_THRESHOLD)
                       for ds in days_by_prof.values())
    max_possible = len(eligible) * over_cap
    return 1.0 - total_excess / max(1, max_possible)


# --------------------------------------------------------------------
# SC-03: 다중 블록 과목의 요일 간격 ≥ 2 비율
# --------------------------------------------------------------------
def _sc03_min_gap(assignment, courses):
    multi = [c for c in courses
             if c.block_structure and len(c.block_structure) >= 2]
    if not multi:
        return 1.0

    days_by_cid = defaultdict(set)
    for (cid, d, _, _) in assignment:
        days_by_cid[cid].add(d)

    good = 0
    for c in multi:
        ds = sorted(days_by_cid.get(c.id, ()))
        if len(ds) < 2:
            continue
        diffs = [ds[i + 1] - ds[i] for i in range(len(ds) - 1)]
        if min(diffs) >= 2:
            good += 1
    return good / len(multi)


# --------------------------------------------------------------------
# 종합 점수 + 정렬
# --------------------------------------------------------------------
def score_solution(assignment, courses, professors):
    parts = {
        "SC01": _sc01_special_slots(assignment),
        "SC02": _sc02_prof_concentration(assignment, professors, courses),
        "SC03": _sc03_min_gap(assignment, courses),
    }
    parts["total"] = sum(SC_WEIGHTS[k] * (parts[k] ** SC_PENALTY_POWER[k])
                         for k in SC_KEYS)
    return parts


def rank_solutions(solutions, courses, professors, top_m=10):
    """[(assignment, score_dict), ...] 내림차순 정렬, 상위 top_m 만 반환."""
    scored = [(a, score_solution(a, courses, professors)) for a in solutions]
    scored.sort(key=lambda x: x[1]["total"], reverse=True)
    return scored[:top_m]
