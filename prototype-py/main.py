#!/usr/bin/env python3
"""Timetable scheduler prototype - entry point.

Layered: domain → csp → scoring → data → ui.
"""
from data import load_from_xlsx
from csp import build_and_solve_diverse
from scoring import rank_solutions, SC_WEIGHTS
from ui import launch
from domain import derive_auto_retakes
from domain.models import expand_sections

def _find_xlsx():
    """xlsx 검색 순서: data_files/ → ../wpf/data/ → 현재 디렉터리."""
    import os
    candidates = [
        "data_files/개설강좌 편람.xlsx",
        "../wpf/data/개설강좌 편람.xlsx",
        "개설강좌 편람.xlsx",
    ]
    for p in candidates:
        if os.path.isfile(p):
            return p
    return candidates[-1]  # 없어도 마지막 경로 반환 (로더가 오류 처리)

XLSX_PATH = _find_xlsx()
N_SOLUTIONS = 1             # 다양성 모드로 모을 후보 해 수
M_TOP = 1                    # GUI 에 노출할 상위 해
TIME_LIMIT_SEC = 600        # 솔버 전체 wall-clock 제한
PER_SOLVE_TIME_SEC = 30     # 각 phase / 시드별 단위 시간 (Phase 1 은 ×2 사용)
AUTO_RETAKES_DEFAULT = True  # 모든 (상위학년, 하위학년 전필) 쌍 자동 적용


def _load_data():
    """data_source.py 의 COURSES 가 비어있지 않으면 거기서, 아니면 xlsx 에서.

    Returns (courses, professors, rooms, crosses, retakes).
    """
    try:
        import data_source
        if getattr(data_source, "COURSES", None):
            print("[loader] data_source.py 에서 로드")
            return (list(data_source.COURSES),
                    list(data_source.PROFESSORS),
                    list(data_source.ROOMS),
                    list(getattr(data_source, "CROSSES", []) or []),
                    list(getattr(data_source, "RETAKES", []) or []))
    except Exception as e:
        print(f"[loader] data_source.py 로드 실패, xlsx 로 전환: {e}")
    print(f"[loader] {XLSX_PATH} 에서 로드")
    courses, professors, rooms = load_from_xlsx(XLSX_PATH)
    return courses, professors, rooms, [], []


def main():
    print("=" * 60)
    print("  전공 시간표 자동 생성 시스템 - 프로토타입")
    print("=" * 60)

    raw_courses, professors, rooms, crosses, manual_retakes = _load_data()
    courses = expand_sections(raw_courses)
    auto_retakes = (derive_auto_retakes(courses)
                    if AUTO_RETAKES_DEFAULT else [])
    retakes = auto_retakes + manual_retakes
    print(f"[loader] 과목 {len(courses)}개(분반 전개), 교수 {len(professors)}명, "
          f"강의실 {len(rooms)}개, Cross {len(crosses)}개, "
          f"재수강 시나리오 {len(retakes)}개 "
          f"(자동 {len(auto_retakes)} + 수동 {len(manual_retakes)})")

    for c in courses:
        block_str = "+".join(str(b) for b in c.block_structure)
        print(f"  {c.id:12s} {c.name:15s} {c.grade}학년 "
              f"{c.hours_per_week}h({block_str}) "
              f"{c.course_type} {c.professor_id:6s} "
              f"{','.join(c.fixed_rooms) if c.fixed_rooms else '-'}")

    status, solutions = build_and_solve_diverse(
        courses, professors, rooms,
        total_solutions=N_SOLUTIONS,
        time_limit_sec=TIME_LIMIT_SEC,
        per_solve_time_sec=PER_SOLVE_TIME_SEC,
        crosses=crosses,
        retakes=retakes,
        sc01_weight=SC_WEIGHTS.get("SC01", 0),
        sc02_weight=SC_WEIGHTS.get("SC02", 0),
        sc03_weight=SC_WEIGHTS.get("SC03", 0),
    )

    if not solutions:
        print(f"\n[오류] 해를 찾지 못했습니다. (status={status})")
        return

    ranked = rank_solutions(solutions, courses, professors, top_m=M_TOP)
    top_solutions = [a for a, _ in ranked]
    top_scores = [s for _, s in ranked]

    print(f"\n[ranking] 가중치: {SC_WEIGHTS}")
    print(f"[ranking] 후보 {len(solutions)}개 → 상위 {len(top_solutions)}개:")
    from scoring import SC_KEYS
    for i, sd in enumerate(top_scores):
        parts = "  ".join(f"{k}={sd[k]:.2f}" for k in SC_KEYS)
        print(f"  #{i+1}  total={sd['total']:.2f}  {parts}")

    print(f"\n상위 {len(top_solutions)}개의 해를 GUI에 노출합니다...")
    launch(top_solutions, courses, professors, rooms, crosses, manual_retakes,
           auto_retakes=AUTO_RETAKES_DEFAULT, scores=top_scores,
           n_solutions=N_SOLUTIONS, m_top=M_TOP,
           time_limit_sec=TIME_LIMIT_SEC,
           per_solve_time_sec=PER_SOLVE_TIME_SEC)


if __name__ == "__main__":
    main()
