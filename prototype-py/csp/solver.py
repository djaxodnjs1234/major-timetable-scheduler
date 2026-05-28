"""CP-SAT solver: build model, apply constraints, solve."""
from ortools.sat.python import cp_model

from .constraints import (
    DAYS, VALID_PERIODS,
    add_hc01_room_single,
    add_hc02_prof_single,
    add_hc03_prof_unavailable,
    add_hc04_hours,
    add_hc06_block_split,
    add_hc08_section_no_overlap,
    add_hc11_grade_no_overlap,
    add_hc12_lunch,
    add_hc13_fixed,
    add_hc14_fixed_rooms,
    add_hc15_section_backtoback,
    add_hc16_cross,
    add_hc17_retake,
    add_hc18_block_day_gap,
    add_hc19_len2_start_periods,
    add_hc20_block_days_distinct,
    add_hc21_prof_room_consistent,
)
from .objectives import (
    sc01_penalty_term, sc02_penalty_term, sc03_penalty_term,
    SC01_SLACK_ABS, SC02_SLACK_ABS, SC03_SLACK_ABS,
)


_STATUS_NAMES = {
    cp_model.OPTIMAL: "OPTIMAL",
    cp_model.FEASIBLE: "FEASIBLE",
    cp_model.INFEASIBLE: "INFEASIBLE",
    cp_model.MODEL_INVALID: "MODEL_INVALID",
    cp_model.UNKNOWN: "UNKNOWN",
}


def _status_name(status):
    return _STATUS_NAMES.get(status, "UNKNOWN")


def _build_model(courses, professors, rooms, crosses=None, retakes=None):
    model = cp_model.CpModel()
    prof_map = {p.id: p for p in professors}

    x = {}
    for c in courses:
        for d in range(DAYS):
            for p in range(1, 10):
                for r in rooms:
                    x[(c.id, d, p, r.id)] = model.NewBoolVar(
                        f"x_{c.id}_{d}_{p}_{r.id}")

    # y[(c.id, d, p)] = 1 ⟺ 과목 c 가 (d,p) 슬롯을 점유. 다중방(K>1) 시
    # 모든 K 개 방이 함께 켜져야 y=1 — sum_r x = K * y 로 묶는다.
    # 다중방 케이스에서 HC-02/08/11/16/17 가 K 배 잘못 카운트되는 걸 방지.
    y = {}
    for c in courses:
        K = max(len(c.fixed_rooms or []), 1)
        for d in range(DAYS):
            for p in range(1, 10):
                yv = model.NewBoolVar(f"y_{c.id}_{d}_{p}")
                y[(c.id, d, p)] = yv
                model.Add(
                    sum(x[(c.id, d, p, r.id)] for r in rooms) == K * yv
                )

    add_hc01_room_single(model, x, courses, rooms)
    add_hc02_prof_single(model, y, courses, prof_map)
    add_hc03_prof_unavailable(model, x, courses, rooms, prof_map)
    add_hc04_hours(model, x, courses, rooms)
    start_vars_by_block, day_vars_by_course = add_hc06_block_split(
        model, x, courses, rooms)
    add_hc08_section_no_overlap(model, y, courses)
    add_hc11_grade_no_overlap(model, y, courses, crosses)
    add_hc12_lunch(model, x, courses, rooms)
    add_hc13_fixed(model, y, courses)
    add_hc14_fixed_rooms(model, x, courses, rooms)
    add_hc15_section_backtoback(model, start_vars_by_block, courses)
    add_hc16_cross(model, y, courses, crosses or [])
    add_hc17_retake(model, y, courses, retakes or [])
    add_hc18_block_day_gap(model, day_vars_by_course)
    add_hc19_len2_start_periods(model, start_vars_by_block, courses,
                                crosses or [])
    add_hc20_block_days_distinct(model, day_vars_by_course)
    add_hc21_prof_room_consistent(model, x, courses, rooms, prof_map)

    return model, x, day_vars_by_course


def build_and_solve_diverse(courses, professors, rooms,
                            total_solutions=500,
                            time_limit_sec=120,
                            per_solve_time_sec=5,
                            crosses=None, retakes=None, base_seed=0,
                            sc01_weight=0,
                            sc02_weight=0,
                            sc03_weight=0):
    """시드를 바꿔가며 N번 독립 solve — 매번 첫 feasible 1개만 수집.

    enumerate_all_solutions 가 한 트리 안 인접 leaf 만 모으는 문제를 회피.

    A2 lex (다단계 최적화) — 우선순위 SC-01 → SC-02 → SC-03:
      - ``sc01_weight > 0`` → Phase 1A 에서 SC-01 minimize → opt01 잠금
      - ``sc02_weight > 0`` → Phase 1B 에서 SC-02 minimize (sc01 제약 유지)
        → opt02 잠금
      - ``sc03_weight > 0`` → Phase 1C 에서 SC-03 minimize (sc01/02 제약 유지)
        → opt03 잠금
      - Phase 2: 활성화된 모든 제약 하에서 시드 loop 로 다양성 확보

    Phase 1 결과는 일회용 모델로 측정만 하고, Phase 2 용 모델을 새로 빌드하면서
    측정된 opt + slack 을 hard 제약으로 박는다 (ortools 신버전에서 objective 를
    깨끗이 비우는 API 가 일관되지 않아 모델 분리가 안전).
    """
    import time
    print(f"[solver] diverse 모드: 목표 {total_solutions}개 "
          f"(전체 {time_limit_sec}s, solve당 {per_solve_time_sec}s 제한)")
    t_start = time.time()

    sc01_bound = None  # opt + slack — Phase 2 에서 sc01 <= 이 값
    sc02_bound = None  # 같은 의미, SC-02 용
    sc03_bound = None  # 같은 의미, SC-03 용

    # ── Phase 1A: SC-01 최적값 구하기 (일회용 모델) ───────────────────
    if sc01_weight > 0:
        print("[solver] Phase 1A 모델 빌드 중...")
        p1_model, p1_x, _ = _build_model(
            courses, professors, rooms, crosses, retakes)
        p1_term = sc01_penalty_term(p1_x, courses, rooms)
        p1_model.Minimize(p1_term)
        s1 = cp_model.CpSolver()
        s1.parameters.max_time_in_seconds = per_solve_time_sec * 2
        st1 = s1.Solve(p1_model)
        print(f"[solver] Phase 1A (SC-01 minimize): {_status_name(st1)}, "
              f"{s1.WallTime():.2f}s")
        if st1 not in (cp_model.FEASIBLE, cp_model.OPTIMAL):
            print("[solver] Phase 1A 실패 — 빈 결과 반환")
            return _status_name(st1), []
        sc01_opt = int(s1.ObjectiveValue())
        sc01_slack = max(0, SC01_SLACK_ABS)
        sc01_bound = sc01_opt + sc01_slack
        print(f"[solver] Phase 1A 결과: SC-01 optimal={sc01_opt}, "
              f"Phase 2 enforces sc01 <= {sc01_bound} (slack={sc01_slack})")

    # ── Phase 1B: SC-02 최적값 구하기 (SC-01 제약 유지) ───────────────
    if sc02_weight > 0:
        print("[solver] Phase 1B 모델 빌드 중...")
        p2_model, p2_x, _p2_dvc = _build_model(
            courses, professors, rooms, crosses, retakes)
        if sc01_bound is not None:
            p2_model.Add(sc01_penalty_term(p2_x, courses, rooms) <= sc01_bound)
        p2_term = sc02_penalty_term(p2_model, p2_x, courses, rooms)
        p2_model.Minimize(p2_term)
        s2 = cp_model.CpSolver()
        s2.parameters.max_time_in_seconds = per_solve_time_sec * 2
        st2 = s2.Solve(p2_model)
        print(f"[solver] Phase 1B (SC-02 minimize): {_status_name(st2)}, "
              f"{s2.WallTime():.2f}s")
        if st2 not in (cp_model.FEASIBLE, cp_model.OPTIMAL):
            print("[solver] Phase 1B 실패 — 빈 결과 반환")
            return _status_name(st2), []
        sc02_opt = int(s2.ObjectiveValue())
        sc02_slack = max(0, SC02_SLACK_ABS)
        sc02_bound = sc02_opt + sc02_slack
        print(f"[solver] Phase 1B 결과: SC-02 optimal={sc02_opt}, "
              f"Phase 2 enforces sc02 <= {sc02_bound} (slack={sc02_slack})")

    # ── Phase 1C: SC-03 최적값 구하기 (SC-01/02 제약 유지) ────────────
    if sc03_weight > 0:
        print("[solver] Phase 1C 모델 빌드 중...")
        p3_model, p3_x, p3_dvc = _build_model(
            courses, professors, rooms, crosses, retakes)
        if sc01_bound is not None:
            p3_model.Add(sc01_penalty_term(p3_x, courses, rooms) <= sc01_bound)
        if sc02_bound is not None:
            p3_model.Add(
                sc02_penalty_term(p3_model, p3_x, courses, rooms) <= sc02_bound)
        p3_term = sc03_penalty_term(p3_model, p3_dvc, courses)
        p3_model.Minimize(p3_term)
        s3 = cp_model.CpSolver()
        s3.parameters.max_time_in_seconds = per_solve_time_sec * 2
        st3 = s3.Solve(p3_model)
        print(f"[solver] Phase 1C (SC-03 minimize): {_status_name(st3)}, "
              f"{s3.WallTime():.2f}s")
        if st3 not in (cp_model.FEASIBLE, cp_model.OPTIMAL):
            print("[solver] Phase 1C 실패 — 빈 결과 반환")
            return _status_name(st3), []
        sc03_opt = int(s3.ObjectiveValue())
        sc03_slack = max(0, SC03_SLACK_ABS)
        sc03_bound = sc03_opt + sc03_slack
        print(f"[solver] Phase 1C 결과: SC-03 optimal={sc03_opt}, "
              f"Phase 2 enforces sc03 <= {sc03_bound} (slack={sc03_slack})")

    # ── Phase 2: 본 모델 빌드 + slack 제약들 + 시드 loop ──────────────
    # Phase 2 모델 = HC 1~21 전부 + Phase 1A/B/C 에서 측정한 sc bound 들을
    # hard 제약(sc_term <= bound)으로 박은 본 모델. objective 없음(feasibility만).
    # 시드를 바꿔가며 N번 독립 solve → 다양한 feasible 해 수집.
    print("[solver] Phase 2 모델 빌드 중 (본 모델 + SC bound hard 제약)...")
    model, x, dvc = _build_model(courses, professors, rooms, crosses, retakes)
    print(f"[solver] 변수 {len(x)}개")
    if sc01_bound is not None:
        model.Add(sc01_penalty_term(x, courses, rooms) <= sc01_bound)
    if sc02_bound is not None:
        model.Add(sc02_penalty_term(model, x, courses, rooms) <= sc02_bound)
    if sc03_bound is not None:
        model.Add(sc03_penalty_term(model, dvc, courses) <= sc03_bound)

    # ── Phase 2: 시드 loop 로 다양한 feasible 해 수집 ────────────────
    seen = set()
    all_solutions = []
    last_status = "UNKNOWN"
    attempts = 0
    p2_t0 = time.time()

    for i in range(total_solutions):
        elapsed = time.time() - t_start
        if elapsed > time_limit_sec:
            print(f"[solver] 시간 초과 — 중단")
            break
        attempts += 1

        solver = cp_model.CpSolver()
        solver.parameters.random_seed = base_seed + i
        solver.parameters.randomize_search = True
        solver.parameters.max_time_in_seconds = min(
            per_solve_time_sec, time_limit_sec - elapsed)

        status = solver.Solve(model)
        last_status = _status_name(status)

        new_unique = False
        if status in (cp_model.FEASIBLE, cp_model.OPTIMAL):
            sol = sorted(k for k, v in x.items() if solver.Value(v) == 1)
            key = tuple(sol)
            if key not in seen:
                seen.add(key)
                all_solutions.append(sol)
                new_unique = True

        tag = "+신규" if new_unique else ("=중복" if status in (
            cp_model.FEASIBLE, cp_model.OPTIMAL) else "실패")
        print(f"[solver] Phase 2 시도 #{i+1}: {last_status}, "
              f"{solver.WallTime():.2f}s {tag} "
              f"(누적 고유 {len(all_solutions)}개)")

    print(f"[solver] Phase 2 종료: 시도 {attempts}회, "
          f"{time.time()-p2_t0:.1f}s")

    print(f"[solver] diverse 모드 종료: 시도 {attempts}회, "
          f"고유 해 {len(all_solutions)}개 "
          f"(총 {time.time()-t_start:.1f}s)")
    return last_status, all_solutions
