"""Domain dataclasses + pure helpers."""
from dataclasses import dataclass, field
from typing import List


@dataclass
class Course:
    id: str
    name: str
    grade: int
    hours_per_week: int
    course_type: str = "전필"   # "전필" | "전선" | "교양"
    professor_id: str = ""
    section: int = 1
    department: str = ""
    # 빈 리스트 → 솔버가 1개 자동 선택. 길이 1 → 그 방 고정.
    # 길이 N>1 → 그 N개 방을 모든 점유 슬롯에 동시 배치 (예: 캡스톤 6개 방).
    fixed_rooms: List[str] = field(default_factory=list)
    block_structure: List[int] = field(default_factory=list)
    is_fixed: bool = False
    # is_fixed=True 일 때 점유 강제할 시간 슬롯들. 형식: [(day, period), ...]
    # 강의실은 fixed_rooms / 교수 prof_room (HC-21) 가 결정.
    fixed_slots: list = field(default_factory=list)
    coteach_profs: List[str] = field(default_factory=list)


@dataclass
class Professor:
    id: str
    name: str
    unavailable_slots: list = field(default_factory=list)  # [(day, period)]
    # 빈 리스트 → 모든 강의실 허용. 비어있지 않으면 그 방들만 사용 가능.
    # 팀티칭 시 모든 교수의 allowed_rooms 교집합이 적용됨 (교집합이 비면 infeasible).
    allowed_rooms: List[str] = field(default_factory=list)


@dataclass
class Room:
    id: str
    name: str


@dataclass
class CrossGroup:
    """Cross 수강 그룹.

    같은 시간대에 대해 여러 과목의 분반이 서로 교차되도록 묶는다.
    예: [A, B] with K=2 sections each
         → A.sec1 ↔ B.sec2 같은 슬롯, A.sec2 ↔ B.sec1 같은 슬롯
    """
    id: str
    base_ids: List[str] = field(default_factory=list)


@dataclass
class RetakeScenario:
    """재수강 시나리오 (옵션 B — 학생 단위 X, 패턴만).

    "current_grade 학년 학생이 retake_base_id 를 재수강하는 경우가 있다."

    재수강 과목의 분반 중 적어도 하나는 current_grade 학년의 모든 전공필수와
    시간이 겹치지 않아야 한다.
    """
    current_grade: int
    retake_base_id: str


def base_id(cid: str) -> str:
    """GA1004-01 → GA1004."""
    return cid.rsplit("-", 1)[0] if "-" in cid else cid


def expand_sections(courses):
    """section > 1 인 과목을 개별 분반으로 전개.

    Course(id="GA1004", section=2) →
        [Course(id="GA1004-01", section=1), Course(id="GA1004-02", section=2)]

    ID에 이미 분반 접미사가 있으면(base_id(c.id) != c.id) 이미 전개된 것이므로 그대로 유지.
    """
    import dataclasses
    result = []
    for c in courses:
        n = c.section
        already_expanded = base_id(c.id) != c.id
        if n <= 1 or already_expanded:
            result.append(dataclasses.replace(c))
        else:
            for s in range(1, n + 1):
                result.append(dataclasses.replace(c, id=f"{c.id}-{s:02d}", section=s))
    return result


def derive_auto_retakes(courses, grades=(1, 2, 3, 4)):
    """모든 (상위학년, 하위학년 전필 base_id) 쌍을 RetakeScenario 로 생성."""
    majors_by_grade = {}
    for c in courses:
        if c.course_type == "전필":
            majors_by_grade.setdefault(c.grade, set()).add(base_id(c.id))
    out = []
    for g_lower, bids in majors_by_grade.items():
        for bid in bids:
            for g_higher in grades:
                if g_higher > g_lower:
                    out.append(RetakeScenario(
                        current_grade=g_higher, retake_base_id=bid))
    return out
