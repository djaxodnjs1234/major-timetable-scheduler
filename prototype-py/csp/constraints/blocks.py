"""Block-related HCs.

- HC-06  : 블록 연속 배치 (분할 + 연속 교시 + 같은 강의실)
- HC-14  : 강의실 고정 (fixed_rooms — 다중방 동시 점유 지원)
- HC-21  : 교수 allowed_rooms 교집합 외 강의실 금지
- HC-15  : 분반 인접 (Section back-to-back)
- HC-18  : 같은 과목 블록 페어 요일 차 ≤ 2
- HC-19  : 길이 2 블록의 시작 교시 ∈ {1, 3, 6, 8}
- HC-20  : 블록 간 요일 분산 (AddAllDifferent)
"""
from ._common import DAYS, VALID_PERIODS, _base_id, course_prof_ids

# 길이 2 블록의 시작 교시는 {1,3,6,8} 만 허용 → 1-2, 3-4, 6-7, 8-9 슬롯에 정렬.
# HC-19 가 사용.
LEN2_START_PERIODS = (1, 3, 6, 8)


def add_hc06_block_split(model, x, courses, rooms):
    """HC-06: 블록 연속 배치.

    block_structure=[b1,b2,...] 각 블록은 연속 교시로 배정.
    fixed_rooms 길이 K(≥1)이면 그 K개 방을 블록 내내 동시 점유.
    빈 리스트면 솔버가 1개 방 자동 선택. 블록 간 요일 분산은 HC-20 담당.

    Returns:
        start_vars_by_block: {(course_id, block_idx): {(d, sp): BoolVar}}
        — HC-15(Section-BackToBack), HC-19 등이 재사용.
        day_vars_by_course: {course_id: [day_var_block0, day_var_block1, ...]}
        — HC-18(block_day_gap), HC-20(days_distinct), SC-03 등이 재사용.
    """
    room_ids = [r.id for r in rooms]
    start_vars_by_block = {}
    day_vars_by_course = {}

    for c in courses:
        if c.is_fixed:
            continue  # HC-13
        blocks = c.block_structure or [c.hours_per_week]
        fixed_rids = list(c.fixed_rooms or [])

        day_vars = []
        for i, b in enumerate(blocks):
            day_var = model.NewIntVar(0, DAYS - 1, f"day_{c.id}_b{i}")
            day_vars.append(day_var)

            valid_starts = []
            for d in range(DAYS):
                for sp in VALID_PERIODS:
                    block_periods = list(range(sp, sp + b))
                    if not all(bp in VALID_PERIODS for bp in block_periods):
                        continue
                    valid_starts.append((d, sp))

            if not valid_starts:
                model.Add(1 == 0)
                continue

            start_vars = {}
            for (d, sp) in valid_starts:
                start_vars[(d, sp)] = model.NewBoolVar(
                    f"start_{c.id}_b{i}_{d}_{sp}")

            model.Add(sum(start_vars.values()) == 1)

            for (d, sp), svar in start_vars.items():
                if fixed_rids:
                    # 명시된 모든 방을 블록 전체에 동시 점유
                    for rid in fixed_rids:
                        for k in range(b):
                            model.Add(x[(c.id, d, sp + k, rid)] == 1
                                      ).OnlyEnforceIf(svar)
                else:
                    # 자동 1개 방 선택
                    room_choice = {}
                    for rid in room_ids:
                        room_choice[rid] = model.NewBoolVar(
                            f"room_{c.id}_b{i}_{d}_{sp}_{rid}")
                    model.Add(sum(room_choice.values()) == 1
                              ).OnlyEnforceIf(svar)
                    model.Add(sum(room_choice.values()) == 0
                              ).OnlyEnforceIf(svar.Not())
                    for rid, rc in room_choice.items():
                        for k in range(b):
                            model.Add(x[(c.id, d, sp + k, rid)] == 1
                                      ).OnlyEnforceIf(rc)

                model.Add(day_var == d).OnlyEnforceIf(svar)

            start_vars_by_block[(c.id, i)] = start_vars

        if day_vars:
            day_vars_by_course[c.id] = day_vars

    return start_vars_by_block, day_vars_by_course


def add_hc14_fixed_rooms(model, x, courses, rooms):
    """HC-14: fixed_rooms 지정 시 해당 방 집합 외 사용 금지.

    빈 리스트면 제약 없음(자동 선택 모드).
    """
    for c in courses:
        if not c.fixed_rooms:
            continue
        allowed = set(c.fixed_rooms)
        for d in range(DAYS):
            for p in VALID_PERIODS:
                for r in rooms:
                    if r.id not in allowed:
                        model.Add(x[(c.id, d, p, r.id)] == 0)


def add_hc21_prof_room_consistent(model, x, courses, rooms, prof_map):
    """HC-21: 교수 단위 강의실 일관성 (자동 배정 과목 한정).

    같은 main 교수(`c.professor_id`)가 가르치는 **자동 배정 과목**
    (`fixed_rooms=[] AND not is_fixed`)들은 동일한 방을 사용한다. 솔버가
    후보 방 중 1개를 자동 선택해 그 교수의 모든 자동 배정 강의를 그 방으로
    묶음 — 시드별 무작위 분산 방지.

    후보 방:
    - 교수 `allowed_rooms = []`  → 전체 방 중 1개 선택
    - 교수 `allowed_rooms = [N개]` → 그 N개 중 1개 선택

    팀티칭은 main 교수(첫 번째 = `c.professor_id`)만 기준으로 묶음.
    협동 교수의 일관성은 협동 교수가 main 인 다른 과목 쪽에서 별도 적용.

    **과목 우선 정책 (D2-a)**: 과목 레벨에 방이 명시된 경우 — `is_fixed=True`
    (HC-13) 또는 `fixed_rooms` 명시(HC-14) — 그 과목은 HC-21 적용 안 함.
    """
    # main 교수별 자동 배정 과목 그룹화
    auto_courses_by_pid = {}
    for c in courses:
        if c.fixed_rooms:
            continue  # 과목에 방 명시되면 HC-14 우선 (is_fixed 단독은 시간만 박음)
        pid = c.professor_id
        if not pid:
            continue
        auto_courses_by_pid.setdefault(pid, []).append(c)

    # 교수당 prof_room 변수 dict (sum=1 — 후보 중 정확히 1방 선택)
    prof_room = {}
    for pid in auto_courses_by_pid:
        prof = prof_map.get(pid)
        ar = list(getattr(prof, "allowed_rooms", []) or []) if prof else []
        candidates = ar if ar else [r.id for r in rooms]
        if not candidates:
            continue
        rdict = {rid: model.NewBoolVar(f"prof_room_{pid}_{rid}")
                 for rid in candidates}
        model.Add(sum(rdict.values()) == 1)
        prof_room[pid] = rdict

    # 자동 배정 과목의 x[c, d, p, r] 를 prof_room 으로 묶음
    for pid, clist in auto_courses_by_pid.items():
        rdict = prof_room.get(pid)
        if not rdict:
            continue
        for c in clist:
            for d in range(DAYS):
                for p in VALID_PERIODS:
                    for r in rooms:
                        if r.id in rdict:
                            # 그 방이 선택될 때만 허용 (x ≤ prof_room[r])
                            model.Add(
                                x[(c.id, d, p, r.id)] <= rdict[r.id])
                        else:
                            # 후보 외 방은 항상 0
                            model.Add(x[(c.id, d, p, r.id)] == 0)


def add_hc18_block_day_gap(model, day_vars_by_course):
    """HC-18: 같은 과목의 모든 블록 페어 day 차 ≤ 2.

    HC-20 의 ``AddAllDifferent(day_vars)`` 가 abs_diff ≥ 1 을 자동 보장하므로
    여기선 ``abs_diff ≤ 2`` 만 추가. 결과적으로 |diff| ∈ {1, 2}:
    - 월화/화수/수목/목금 (diff=1) — 허용 (SC-03 가 비선호)
    - 월수/화목/수금 (diff=2) — 허용 (SC-03 선호)
    - 월목/화금 (diff=3), 월금 (diff=4) — 금지
    """
    for cid, day_vars in day_vars_by_course.items():
        if len(day_vars) < 2:
            continue
        for i in range(len(day_vars)):
            for j in range(i + 1, len(day_vars)):
                diff = model.NewIntVar(
                    -(DAYS - 1), DAYS - 1, f"hc18_diff_{cid}_{i}_{j}")
                abs_diff = model.NewIntVar(
                    0, DAYS - 1, f"hc18_abs_{cid}_{i}_{j}")
                model.Add(diff == day_vars[i] - day_vars[j])
                model.AddAbsEquality(abs_diff, diff)
                model.Add(abs_diff <= 2)


def add_hc19_len2_start_periods(model, start_vars_by_block, courses,
                                crosses=None):
    """HC-19: 시작 교시 정렬 ∈ {1, 3, 6, 8} (1-2, 3-4, 6-7, 8-9 슬롯).

    적용 대상:
      1) **길이 2 블록** — 어디에 있든 시작 ∈ {1, 3, 6, 8}.
      2) **Cross 멤버 + 분반 페어 + 길이 1 블록** — HC-15(분반 인접) 와
         HC-16(cross cyclic shift) 의 결합으로 두 분반의 길이 1 블록이
         같은 날 인접 슬롯에 들어가 사실상 2시간 연속 cross 묶음을 형성.
         그 페어의 **작은 sp 가 {1, 3, 6, 8}** 이어야 (즉 두 분반 점유가
         (1,2)/(3,4)/(6,7)/(8,9) 중 하나로 정렬).
    """
    # (1) 길이 2 블록
    for c in courses:
        if c.is_fixed:
            continue
        blocks = c.block_structure or [c.hours_per_week]
        for i, b in enumerate(blocks):
            if b != 2:
                continue
            sv = start_vars_by_block.get((c.id, i), {})
            for (_d, sp), var in sv.items():
                if sp not in LEN2_START_PERIODS:
                    model.Add(var == 0)

    # (2) cross 멤버 분반 페어의 길이 1 블록 sp 정렬
    cross_base_ids = set()
    for g in (crosses or []):
        cross_base_ids.update(g.base_ids)
    if not cross_base_ids:
        return

    base_groups = {}
    for c in courses:
        base_groups.setdefault(_base_id(c.id), []).append(c)

    for bid, group in base_groups.items():
        if bid not in cross_base_ids or len(group) != 2:
            continue
        c1, c2 = sorted(group, key=lambda c: c.section)
        if c1.is_fixed or c2.is_fixed:
            continue
        blocks_c1 = c1.block_structure or [c1.hours_per_week]
        blocks_c2 = c2.block_structure or [c2.hours_per_week]
        if blocks_c1 != blocks_c2:
            continue
        for i, b in enumerate(blocks_c1):
            if b != 1:
                continue
            sv1 = start_vars_by_block.get((c1.id, i), {})
            sv2 = start_vars_by_block.get((c2.id, i), {})
            if not sv1 or not sv2:
                continue
            for (d, sp), v1 in sv1.items():
                # c1.sp 별로 페어 작은 ∈ {1,3,6,8} 만족시키는 c2 partner sp 강제
                if sp in {1, 3, 6, 8}:
                    partner_sp = sp + 1     # 페어 = (sp, sp+1), 작은 = sp
                elif sp in {2, 4, 7, 9}:
                    partner_sp = sp - 1     # 페어 = (sp-1, sp), 작은 = sp-1
                else:
                    continue
                v2 = sv2.get((d, partner_sp))
                if v2 is None:
                    model.Add(v1 == 0)
                else:
                    model.Add(v2 == 1).OnlyEnforceIf(v1)


def add_hc20_block_days_distinct(model, day_vars_by_course):
    """HC-20: 같은 과목의 블록들은 서로 다른 요일.

    HC-06 에서 분리한 부분. block_structure 가 2 개 이상이면 day_vars 가 모두
    다른 값을 갖도록 ``AddAllDifferent`` 강제.
    """
    for _cid, day_vars in day_vars_by_course.items():
        if len(day_vars) >= 2:
            model.AddAllDifferent(day_vars)


def add_hc15_section_backtoback(model, start_vars_by_block, courses):
    """HC-15: 같은 base_id 의 분반 2개를 같은 교수가 가르칠 때
    각 블록의 두 분반을 같은 날 시간상 인접(연속) 슬롯에 배치."""
    base_groups = {}
    for c in courses:
        base_groups.setdefault(_base_id(c.id), []).append(c)

    for bid, group in base_groups.items():
        if len(group) != 2:
            continue
        c1, c2 = sorted(group, key=lambda c: c.section)
        if c1.is_fixed or c2.is_fixed:
            continue
        if course_prof_ids(c1) != course_prof_ids(c2):
            continue
        if not course_prof_ids(c1):
            continue
        blocks_c1 = c1.block_structure or [c1.hours_per_week]
        blocks_c2 = c2.block_structure or [c2.hours_per_week]
        if blocks_c1 != blocks_c2:
            continue

        for i, b in enumerate(blocks_c1):
            sv1 = start_vars_by_block.get((c1.id, i), {})
            sv2 = start_vars_by_block.get((c2.id, i), {})
            if not sv1 or not sv2:
                continue
            for (d, sp), v1 in sv1.items():
                candidates = []
                for delta in (b, -b):
                    sp2 = sp + delta
                    v2 = sv2.get((d, sp2))
                    if v2 is not None:
                        candidates.append(v2)
                if not candidates:
                    model.Add(v1 == 0)
                else:
                    model.Add(sum(candidates) >= 1).OnlyEnforceIf(v1)
