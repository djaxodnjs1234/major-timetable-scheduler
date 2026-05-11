"""Load course data from 개설강좌 편람.xlsx (no openpyxl needed).

xlsx is a zip of XML. We extract the sheet + shared strings with stdlib only.
"""
import re
import zipfile
import xml.etree.ElementTree as ET
from typing import Tuple, List

from domain.models import Course, Professor, Room

NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
_ns = {"s": NS}

DAY_MAP = {"월": 0, "화": 1, "수": 2, "목": 3, "금": 4}


def _col_letter(ref: str) -> str:
    m = re.match(r"([A-Z]+)", ref)
    return m.group(1) if m else ""


def _read_cells(path: str):
    """Yield dicts {col_letter: value} per data row."""
    z = zipfile.ZipFile(path)

    ss_root = ET.fromstring(z.read("xl/sharedStrings.xml"))
    strings = [
        "".join(t.text or "" for t in si.iter(f"{{{NS}}}t"))
        for si in ss_root.findall("s:si", _ns)
    ]

    sheet = ET.fromstring(z.read("xl/worksheets/sheet1.xml"))
    rows = sheet.find("s:sheetData", _ns).findall("s:row", _ns)

    for row in rows:
        d = {}
        for c in row.findall("s:c", _ns):
            ref = c.attrib.get("r", "")
            col = _col_letter(ref)
            t = c.attrib.get("t", "")
            v = c.find("s:v", _ns)
            val = v.text if v is not None else ""
            if t == "s":
                val = strings[int(val)]
            d[col] = val
        yield d


def _parse_schedule(s: str):
    """Parse `월67/D327,목89/D327` → list[(day, [periods], room)]."""
    if not s:
        return []
    parts = re.split(r"[,\s]+", s.strip())
    result = []
    for part in parts:
        if not part or "/" not in part:
            continue
        time_part, room = part.split("/", 1)
        m = re.match(r"^([월화수목금])(\d+)$", time_part)
        if not m:
            continue
        day = DAY_MAP[m.group(1)]
        digits = m.group(2)
        periods = [int(ch) for ch in digits]
        result.append((day, periods, room.strip()))
    return result


def _block_sizes(schedule):
    return [len(periods) for (_, periods, _) in schedule]


def load_from_xlsx(path: str) -> Tuple[List[Course], List[Professor], List[Room]]:
    courses: List[Course] = []
    prof_names = set()
    room_ids = set()

    data_rows = list(_read_cells(path))
    for d in data_rows:
        grade_raw = d.get("E", "")
        name = d.get("G", "").strip()
        if not grade_raw or not name:
            continue
        try:
            grade = int(float(grade_raw))
        except ValueError:
            continue

        type_raw = d.get("F", "").strip()
        course_type = "전필" if type_raw == "필수" else "전선"

        credits_raw = d.get("H", "").strip()
        try:
            hours = int(float(credits_raw))
        except ValueError:
            continue

        code = d.get("I", "").strip()
        prof = d.get("Q", "").strip()
        dept = d.get("R", "").strip()
        sched_str = d.get("S", "").strip()

        section_m = re.search(r"-(\d+)$", code)
        section = int(section_m.group(1)) if section_m else 1

        sched = _parse_schedule(sched_str)
        fixed_room = sched[0][2] if sched else None
        xlsx_blocks = _block_sizes(sched)

        if hours == 3:
            block_structure = [2, 1]
        elif hours == 4:
            block_structure = [2, 2]
        else:
            if xlsx_blocks and sum(xlsx_blocks) == hours:
                block_structure = xlsx_blocks
            else:
                block_structure = [hours]

        courses.append(Course(
            id=code,
            name=name,
            grade=grade,
            hours_per_week=hours,
            course_type=course_type,
            professor_id=prof,
            section=section,
            department=dept,
            fixed_rooms=[fixed_room] if fixed_room else [],
            block_structure=block_structure,
        ))

        if prof:
            prof_names.add(prof)
        if fixed_room:
            room_ids.add(fixed_room)

    professors = [Professor(id=n, name=n) for n in sorted(prof_names)]
    rooms = [Room(id=rid, name=rid) for rid in sorted(room_ids)]

    return courses, professors, rooms
