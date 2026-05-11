"""data_source.py 자동 생성 — 현재 메모리 상태를 Python 리터럴로 직렬화."""
import os
from tkinter import messagebox


def _data_source_path():
    """data_source.py 는 프로젝트 루트(= main.py 위치)에 둔다."""
    import __main__
    main_file = getattr(__main__, "__file__", None)
    if main_file:
        root = os.path.dirname(os.path.abspath(main_file))
    else:
        root = os.getcwd()
    return os.path.join(root, "data_source.py")


def export_to_data_source(state, parent=None, silent=False):
    """현재 state 의 courses/professors/rooms/crosses/manual_retakes 를
    data_source.py 에 Python 리터럴로 덮어쓴다.

    silent=True: 다이얼로그 없이 조용히 저장 (강의실/교수 변경 시 자동 호출).
    """
    path = _data_source_path()

    def q(v):
        return repr(v)

    lines = [
        '"""자동 생성된 교과목/교수/강의실 데이터 — 직접 편집 가능.',
        '',
        'COURSES가 비어있지 않으면 main.py가 xlsx 대신 이 파일을 로드합니다.',
        'GUI의 \'📤 코드 내보내기\' 버튼이 이 파일을 덮어씁니다.',
        '"""',
        'from domain.models import (',
        '    Course, Professor, Room, CrossGroup, RetakeScenario,',
        ')',
        '',
        'PROFESSORS = [',
    ]
    for p in state.professors:
        lines.append(
            f"    Professor(id={q(p.id)}, name={q(p.name)}, "
            f"unavailable_slots={q(list(p.unavailable_slots))}, "
            f"allowed_rooms={q(list(getattr(p, 'allowed_rooms', []) or []))}),"
        )
    lines += [']', '', 'ROOMS = [']
    for r in state.rooms:
        lines.append(f"    Room(id={q(r.id)}, name={q(r.name)}),")
    lines += [']', '', 'COURSES = [']
    for c in state.courses:
        lines.append(
            "    Course("
            f"id={q(c.id)}, name={q(c.name)}, grade={c.grade}, "
            f"hours_per_week={c.hours_per_week}, "
            f"course_type={q(c.course_type)}, "
            f"professor_id={q(c.professor_id)}, section={c.section}, "
            f"department={q(c.department)}, "
            f"fixed_rooms={q(list(c.fixed_rooms))}, "
            f"block_structure={q(list(c.block_structure))}, "
            f"is_fixed={c.is_fixed}, "
            f"fixed_slots={q(list(c.fixed_slots))}, "
            f"coteach_profs={q(list(c.coteach_profs))}),"
        )
    lines += [']', '', 'CROSSES = [']
    for g in state.crosses:
        lines.append(
            f"    CrossGroup(id={q(g.id)}, "
            f"base_ids={q(list(g.base_ids))}),"
        )
    lines += [']', '', 'RETAKES = [']
    for r in state.manual_retakes:
        lines.append(
            f"    RetakeScenario(current_grade={r.current_grade}, "
            f"retake_base_id={q(r.retake_base_id)}),"
        )
    lines.append(']')
    content = '\n'.join(lines) + '\n'

    try:
        with open(path, 'w', encoding='utf-8') as f:
            f.write(content)
    except Exception as e:
        if not silent:
            messagebox.showerror("내보내기 실패", str(e), parent=parent)
        return False

    if not silent:
        messagebox.showinfo(
            "내보내기 완료",
            f"{path}\n에 저장되었습니다.\n\n"
            f"이 파일을 편집 후 재실행하면 xlsx 대신 여기서 로드됩니다.\n"
            f"다시 xlsx 로 돌리려면 COURSES = [] 로 비우세요.",
            parent=parent,
        )
    return True
