"""Basic single-slot HCs: room exclusivity, prof exclusivity, hours,
prof unavailability, lunch ban, fixed slots, section overlap, grade overlap."""
from itertools import combinations

from ._common import (
    DAYS, VALID_PERIODS, LUNCH_PERIOD,
    course_prof_ids, _base_id,
)


def add_hc01_room_single(model, x, courses, rooms):
    """HC-01: 한 시간대에 한 강의실엔 하나의 수업만."""
    for d in range(DAYS):
        for p in VALID_PERIODS:
            for r in rooms:
                model.Add(sum(x[(c.id, d, p, r.id)] for c in courses) <= 1)


def add_hc02_prof_single(model, y, courses, prof_map):
    """HC-02: 한 시간대에 한 교수는 하나의 수업만 (팀티칭 포함).

    y[(cid, d, p)] = 슬롯 점유 indicator (다중방 K개여도 1로 카운트).
    """
    prof_courses = {}
    for c in courses:
        for pid in course_prof_ids(c):
            prof_courses.setdefault(pid, []).append(c)

    for _, pcourses in prof_courses.items():
        for d in range(DAYS):
            for p in VALID_PERIODS:
                model.Add(
                    sum(y[(c.id, d, p)] for c in pcourses) <= 1
                )


def add_hc03_prof_unavailable(model, x, courses, rooms, prof_map):
    """HC-03: 교수 불가능 시간 금지 (팀티칭 포함)."""
    for c in courses:
        for pid in course_prof_ids(c):
            prof = prof_map.get(pid)
            if not prof:
                continue
            for (d, p) in prof.unavailable_slots:
                if p == LUNCH_PERIOD:
                    continue
                for r in rooms:
                    model.Add(x[(c.id, d, p, r.id)] == 0)


def add_hc04_hours(model, x, courses, rooms):
    """HC-04: 과목별 요구 시수 정확 충족.

    fixed_rooms 길이 K (≥1) → K개 방을 동시 점유. 빈 리스트면 K=1 (자동 1개).
    총 점유 = hours_per_week × K.
    """
    for c in courses:
        K = max(len(c.fixed_rooms or []), 1)
        model.Add(
            sum(x[(c.id, d, p, r.id)]
                for d in range(DAYS)
                for p in VALID_PERIODS
                for r in rooms) == c.hours_per_week * K
        )


def add_hc08_section_no_overlap(model, y, courses):
    """HC-08: 동일 과목 분반 간 시간 중복 금지 (slot indicator y 사용)."""
    base_groups = {}
    for c in courses:
        base_groups.setdefault(_base_id(c.id), []).append(c)

    for _, group in base_groups.items():
        if len(group) < 2:
            continue
        for c1, c2 in combinations(group, 2):
            for d in range(DAYS):
                for p in VALID_PERIODS:
                    model.Add(y[(c1.id, d, p)] + y[(c2.id, d, p)] <= 1)


def add_hc11_grade_no_overlap(model, y, courses, crosses=None):
    """HC-11: 같은 학년 내 시간 중복 금지 (분반 쌍 + Cross 쌍 제외)."""
    grade_courses = {}
    for c in courses:
        grade_courses.setdefault(c.grade, []).append(c)

    def _same_cross_group(b1, b2):
        for g in (crosses or []):
            if b1 in g.base_ids and b2 in g.base_ids:
                return True
        return False

    for _, gcourses in grade_courses.items():
        for c1, c2 in combinations(gcourses, 2):
            b1, b2 = _base_id(c1.id), _base_id(c2.id)
            if b1 == b2:
                continue  # HC-08
            if _same_cross_group(b1, b2):
                continue  # HC-Cross
            for d in range(DAYS):
                for p in VALID_PERIODS:
                    model.Add(y[(c1.id, d, p)] + y[(c2.id, d, p)] <= 1)


def add_hc12_lunch(model, x, courses, rooms):
    """HC-12: 점심시간(5교시) 배정 금지."""
    for c in courses:
        for d in range(DAYS):
            for r in rooms:
                model.Add(x[(c.id, d, LUNCH_PERIOD, r.id)] == 0)


def add_hc13_fixed(model, y, courses):
    """HC-13: is_fixed 과목의 시간 슬롯 점유 강제 (강의실은 무관).

    fixed_slots = [(day, period), ...] 의 각 (d, p) 에서 점유 indicator y=1.
    강의실은 HC-14(fixed_rooms) 또는 HC-21(prof_room) 이 결정.
    """
    for c in courses:
        if not c.is_fixed:
            continue
        for (d, p) in c.fixed_slots:
            model.Add(y[(c.id, d, p)] == 1)
