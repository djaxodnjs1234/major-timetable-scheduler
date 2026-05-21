"""SQLite DB 로더 — WPF 앱이 생성한 .db 파일을 domain 객체로 변환."""
import json
import sqlite3

from domain.models import Course, Professor, Room, CrossGroup, RetakeScenario


def _parse_slots(raw):
    """JSON 슬롯 배열 파싱. [{Day,Period}] 또는 [[d,p]] 둘 다 처리."""
    items = json.loads(raw or "[]")
    result = []
    for item in items:
        if isinstance(item, dict):
            result.append((item["Day"], item["Period"]))
        else:
            result.append(tuple(item))
    return result


def load_from_db(path):
    """DB에서 (courses, professors, rooms, crosses, retakes, solutions) 반환.

    solutions: SavedTimetables 테이블의 각 행을 [(course_id, day, period, room_id), ...] 목록으로.
    """
    conn = sqlite3.connect(path)
    c = conn.cursor()

    c.execute("SELECT Id, Name, UnavailableSlotsJson, AllowedRoomsJson FROM Professors")
    professors = [
        Professor(
            id=row[0], name=row[1],
            unavailable_slots=_parse_slots(row[2]),
            allowed_rooms=json.loads(row[3] or "[]"),
        )
        for row in c.fetchall()
    ]

    c.execute("SELECT Id, Name FROM Rooms")
    rooms = [Room(id=row[0], name=row[1]) for row in c.fetchall()]

    c.execute(
        "SELECT Id, Name, Grade, HoursPerWeek, CourseType, ProfessorId, "
        "Section, Department, FixedRoomsJson, BlockStructureJson, "
        "IsFixed, FixedSlotsJson, CoteachProfsJson FROM Courses"
    )
    courses = []
    for row in c.fetchall():
        cid, name, grade, hours, ctype, profid, section, dept, frj, bsj, isfixed, fsj, cpj = row
        courses.append(Course(
            id=cid, name=name, grade=int(grade), hours_per_week=int(hours),
            course_type=ctype, professor_id=profid or "",
            section=int(section), department=dept or "",
            fixed_rooms=json.loads(frj or "[]"),
            block_structure=json.loads(bsj or "[]"),
            is_fixed=bool(isfixed),
            fixed_slots=_parse_slots(fsj),
            coteach_profs=json.loads(cpj or "[]"),
        ))

    c.execute("SELECT Id, BaseIdsJson FROM CrossGroups")
    crosses = [
        CrossGroup(id=row[0], base_ids=json.loads(row[1] or "[]"))
        for row in c.fetchall()
    ]

    c.execute("SELECT CurrentGrade, RetakeBaseId FROM RetakeScenarios")
    retakes = [
        RetakeScenario(current_grade=int(row[0]), retake_base_id=row[1])
        for row in c.fetchall()
    ]

    c.execute("SELECT AssignmentsJson FROM SavedTimetables ORDER BY CreatedAt")
    solutions = []
    for (aj,) in c.fetchall():
        assignments = json.loads(aj or "[]")
        sol = sorted(
            (a["CourseId"], int(a["Day"]), int(a["Period"]), a["RoomId"])
            for a in assignments
        )
        solutions.append(sol)

    conn.close()
    return courses, professors, rooms, crosses, retakes, solutions
