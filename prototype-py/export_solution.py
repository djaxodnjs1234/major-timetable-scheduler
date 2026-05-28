"""RFP 인포그래픽용 — 솔버 1회 실행 후 결과를 JSON 으로 export."""
import json
from data import load_from_xlsx
from csp import build_and_solve_diverse
from domain import derive_auto_retakes


def main():
    try:
        import data_source
        if getattr(data_source, "COURSES", None):
            courses = list(data_source.COURSES)
            professors = list(data_source.PROFESSORS)
            rooms = list(data_source.ROOMS)
            crosses = list(getattr(data_source, "CROSSES", []) or [])
            manual_retakes = list(getattr(data_source, "RETAKES", []) or [])
            print("[export] data_source.py 에서 로드")
        else:
            raise RuntimeError("empty")
    except Exception:
        courses, professors, rooms = load_from_xlsx("개설강좌 편람.xlsx")
        crosses, manual_retakes = [], []
        print("[export] xlsx 에서 로드")

    auto_retakes = derive_auto_retakes(courses)
    retakes = auto_retakes + manual_retakes

    print(f"[export] 과목 {len(courses)}, 교수 {len(professors)}, "
          f"강의실 {len(rooms)}, Cross {len(crosses)}, 재수강 {len(retakes)}")

    status, solutions = build_and_solve_diverse(
        courses, professors, rooms,
        total_solutions=1,
        time_limit_sec=120,
        per_solve_time_sec=30,
        crosses=crosses, retakes=retakes,
        sc01_weight=1, sc02_weight=1, sc03_weight=1,
    )

    if not solutions:
        print(f"[export] FAILED status={status}")
        return

    assignment = solutions[0]

    course_meta = {
        c.id: {
            "name": c.name,
            "grade": c.grade,
            "professor_id": c.professor_id,
            "course_type": c.course_type,
            "section": c.section,
            "department": c.department,
            "hours_per_week": c.hours_per_week,
            "block_structure": list(c.block_structure),
            "fixed_rooms": list(c.fixed_rooms or []),
            "is_fixed": bool(c.is_fixed),
            "coteach_profs": list(c.coteach_profs or []),
        } for c in courses
    }

    out = {
        "status": status,
        "assignment": [
            {"course_id": cid, "day": d, "period": p, "room_id": rid}
            for (cid, d, p, rid) in assignment
        ],
        "courses": course_meta,
        "rooms": [r.id for r in rooms],
        "professors": [p.id for p in professors],
        "stats": {
            "n_courses": len(courses),
            "n_professors": len(professors),
            "n_rooms": len(rooms),
            "n_crosses": len(crosses),
            "n_retakes": len(retakes),
        },
    }

    with open("solution_for_rfp.json", "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    print(f"[export] solution_for_rfp.json 저장 ({len(assignment)} entries)")


if __name__ == "__main__":
    main()
