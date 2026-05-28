"""과목 추가/수정 다이얼로그.

state(AppState) 를 받아 ``state.replace_course_group(...)`` 로 결과를 반영.
고정 시간표 위젯은 ``_fixed_schedule.FixedScheduleEditor`` 가 담당.
"""
import tkinter as tk
from tkinter import ttk, messagebox

from domain.models import Course, base_id as _base_id
from ..theme import GRADES, COURSE_TYPES
from ..widgets import make_scrollable
from ._fixed_schedule import FixedScheduleEditor, compute_blocks


def _build_form_fields(frm, state, is_edit, rep, clist):
    """좌측 라벨 + 우측 위젯의 단순 필드들을 생성하고 핸들 dict 반환."""
    rowi = [0]

    def add_field(label, widget, sticky="ew"):
        ttk.Label(frm, text=label).grid(
            row=rowi[0], column=0, sticky="w", padx=(0, 10), pady=5)
        widget.grid(row=rowi[0], column=1, sticky=sticky, pady=5)
        rowi[0] += 1

    e_name = ttk.Entry(frm)
    if is_edit:
        e_name.insert(0, rep.name)
    add_field("교과목명", e_name)

    c_grade = ttk.Combobox(frm, state="readonly", width=10,
                           values=[str(g) for g in GRADES])
    c_grade.set(str(rep.grade) if is_edit else "2")
    add_field("학년", c_grade, sticky="w")

    s_hours = tk.Spinbox(frm, from_=1, to=6, width=8)
    s_hours.delete(0, "end")
    s_hours.insert(0, str(rep.hours_per_week) if is_edit else "3")
    add_field("학점(시수)", s_hours, sticky="w")

    c_type = ttk.Combobox(frm, state="readonly", width=10,
                          values=COURSE_TYPES)
    c_type.set(rep.course_type
               if is_edit and rep.course_type in COURSE_TYPES
               else "전필")
    add_field("이수구분", c_type, sticky="w")

    prof_items = [f"{p.id} - {p.name}" for p in state.professors]
    c_prof = ttk.Combobox(frm, state="readonly", values=prof_items)
    cur_idx = 0
    if is_edit:
        for i, p in enumerate(state.professors):
            if p.id == rep.professor_id:
                cur_idx = i
                break
    if prof_items:
        c_prof.current(cur_idx)
    add_field("담당교수", c_prof)

    ttk.Label(frm, text="강의실").grid(
        row=rowi[0], column=0, sticky="nw", padx=(0, 10), pady=5)
    room_wrap = ttk.Frame(frm)
    room_wrap.grid(row=rowi[0], column=1, sticky="ew", pady=5)
    rsb = ttk.Scrollbar(room_wrap, orient=tk.VERTICAL)
    rsb.pack(side=tk.RIGHT, fill=tk.Y)
    c_room = tk.Listbox(
        room_wrap, selectmode="multiple", height=5,
        exportselection=False, yscrollcommand=rsb.set)
    c_room.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
    rsb.config(command=c_room.yview)
    for r in state.rooms:
        c_room.insert(tk.END, f"{r.id} - {r.name}")
    if is_edit:
        existing_rooms = set(rep.fixed_rooms or [])
        for i, r in enumerate(state.rooms):
            if r.id in existing_rooms:
                c_room.selection_set(i)
    rowi[0] += 1
    ttk.Label(frm, foreground="#666", wraplength=420, justify="left",
              text="※ 미선택 = 솔버 자동(1개). 다중 선택 = 모두 동시 점유 "
                   "(예: 캡스톤 6개 방)."
              ).grid(row=rowi[0], column=1, sticky="w", pady=(0, 4))
    rowi[0] += 1

    default_nsec = len(clist) if is_edit else 1
    s_sec = tk.Spinbox(frm, from_=1, to=6, width=8)
    s_sec.delete(0, "end")
    s_sec.insert(0, str(default_nsec))
    add_field("분반수", s_sec, sticky="w")

    existing_single = (
        is_edit and len(rep.block_structure or []) == 1
        and rep.block_structure[0] == rep.hours_per_week
    )
    force_cont = tk.BooleanVar(value=existing_single)
    ttk.Checkbutton(
        frm, variable=force_cont,
        text="연속 할당 (전체 시수를 한 블록으로 배치)"
    ).grid(row=rowi[0], column=0, columnspan=2, sticky="w", pady=(8, 4))
    rowi[0] += 1

    ttk.Label(frm, text="팀티칭 추가 교수").grid(
        row=rowi[0], column=0, sticky="nw", padx=(0, 10), pady=5)
    ct_wrap = ttk.Frame(frm)
    ct_wrap.grid(row=rowi[0], column=1, sticky="ew", pady=5)
    csb = ttk.Scrollbar(ct_wrap, orient=tk.VERTICAL)
    csb.pack(side=tk.RIGHT, fill=tk.Y)
    coteach_lb = tk.Listbox(
        ct_wrap, selectmode="multiple", height=5,
        exportselection=False, yscrollcommand=csb.set)
    coteach_lb.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
    csb.config(command=coteach_lb.yview)
    for p in state.professors:
        coteach_lb.insert(tk.END, f"{p.id} - {p.name}")
    if is_edit:
        exset = set(rep.coteach_profs or [])
        for i, p in enumerate(state.professors):
            if p.id in exset:
                coteach_lb.selection_set(i)
    rowi[0] += 1

    return {
        "rowi": rowi, "e_name": e_name, "c_grade": c_grade,
        "s_hours": s_hours, "c_type": c_type, "c_prof": c_prof,
        "c_room": c_room, "s_sec": s_sec, "force_cont": force_cont,
        "coteach_lb": coteach_lb,
    }


def _gather_basic(fields, state, dlg):
    """기본 필드 검증 후 dict 반환 (고정 시간표 제외). 실패 시 None."""
    name = fields["e_name"].get().strip()
    if not name:
        messagebox.showerror("오류", "교과목명을 입력하세요.", parent=dlg)
        return None
    try:
        grade = int(fields["c_grade"].get())
        hours = int(fields["s_hours"].get())
        nsec = int(fields["s_sec"].get())
    except ValueError:
        messagebox.showerror("오류", "숫자 필드를 확인하세요.", parent=dlg)
        return None
    if nsec < 1:
        messagebox.showerror("오류", "분반수는 1 이상이어야 합니다.",
                             parent=dlg)
        return None
    ctype = fields["c_type"].get()
    prof_id = (fields["c_prof"].get().split(" - ", 1)[0]
               if fields["c_prof"].get() else "")
    if not prof_id:
        messagebox.showerror("오류", "담당교수를 선택하세요.", parent=dlg)
        return None
    room_ids = [
        state.rooms[i].id
        for i in fields["c_room"].curselection()
    ]
    coteach = [
        state.professors[i].id
        for i in fields["coteach_lb"].curselection()
        if state.professors[i].id != prof_id
    ]
    return {
        "name": name, "grade": grade, "hours": hours,
        "ctype": ctype, "prof_id": prof_id, "room_ids": room_ids,
        "force_cont": bool(fields["force_cont"].get()),
        "coteach": coteach, "nsec": nsec,
    }


def _build_new_group(state, clist, is_edit, v):
    block = compute_blocks(v["hours"], v["force_cont"])
    nsec = v["nsec"]
    is_fixed = v["is_fixed"]
    if is_edit:
        base = _base_id(clist[0].id)
        original_map = {c.section: c for c in clist}
    else:
        base = state.next_course_id()
        original_map = {}

    new_group = []
    for s in range(1, nsec + 1):
        cid = f"{base}-{s:02d}" if nsec > 1 else base
        slots = (list(v["fixed_per_section"][s - 1])
                 if is_fixed else [])
        frs = [] if is_fixed else list(v["room_ids"])
        existing = original_map.get(s)
        new_group.append(Course(
            id=cid, name=v["name"], grade=v["grade"],
            hours_per_week=v["hours"], course_type=v["ctype"],
            professor_id=v["prof_id"], section=s,
            department=existing.department if existing else "",
            fixed_rooms=frs, block_structure=block,
            is_fixed=is_fixed, fixed_slots=slots,
            coteach_profs=list(v["coteach"]),
        ))
    return new_group


def open_course_editor(parent, state, target=None):
    """target=None → 추가 모드, [Course,...] → 해당 base 그룹 전체 수정."""
    is_edit = target is not None
    if is_edit:
        clist = sorted(target, key=lambda c: c.section)
        rep = clist[0]
    else:
        clist = []
        rep = None

    dlg = tk.Toplevel(parent)
    dlg.title("과목 수정" if is_edit else "과목 추가")
    dlg.geometry("560x800")
    dlg.transient(parent)
    dlg.grab_set()

    outer = ttk.Frame(dlg)
    outer.pack(fill=tk.BOTH, expand=True)
    frm = make_scrollable(outer, horizontal=False)
    frm.configure(padding=16)
    frm.columnconfigure(1, weight=1)

    fields = _build_form_fields(frm, state, is_edit, rep, clist)
    rowi = fields["rowi"]

    # --- 고정 시간표 섹션 ---
    ttk.Separator(frm, orient="horizontal").grid(
        row=rowi[0], column=0, columnspan=2, sticky="ew", pady=(12, 6))
    rowi[0] += 1

    any_fixed = bool(is_edit and any(c.is_fixed for c in clist))
    is_fixed_var = tk.BooleanVar(value=any_fixed)
    ttk.Checkbutton(
        frm, variable=is_fixed_var,
        text="★ 고정 시간표 사용 (분반별로 요일/교시/강의실 지정)"
    ).grid(row=rowi[0], column=0, columnspan=2, sticky="w", pady=(4, 4))
    rowi[0] += 1

    ttk.Label(
        frm, foreground="#666", wraplength=520, justify="left",
        text=("※ 체크 시 각 분반(A/B/…)마다 블록별 요일/시작교시/강의실을 "
              "지정합니다. 미체크 시 솔버가 자동 배정.")
    ).grid(row=rowi[0], column=0, columnspan=2, sticky="w", pady=(0, 4))
    rowi[0] += 1

    fixed_frame = ttk.Frame(frm, padding=6, relief="groove", borderwidth=1)
    fixed_frame.grid(row=rowi[0], column=0, columnspan=2,
                     sticky="ew", pady=4)
    rowi[0] += 1

    fixed_editor = FixedScheduleEditor(
        fixed_frame, state,
        fields["s_hours"], fields["s_sec"],
        fields["force_cont"], is_fixed_var)
    fixed_editor.set_existing(clist, rep)
    fixed_editor.bind_triggers()
    fixed_editor.rebuild()

    ttk.Label(
        frm, foreground="#666", wraplength=520, justify="left",
        text=("※ 블록: 연속 할당 미체크 시 학점 3→[2,1], 4→[2,2], "
              "그 외는 단일 블록.\n"
              "※ 팀티칭: 선택한 추가 교수들도 그 시간대에 함께 배정.")
    ).grid(row=rowi[0], column=0, columnspan=2,
           sticky="w", pady=(10, 0))
    rowi[0] += 1

    btn_row = ttk.Frame(frm)
    btn_row.grid(row=rowi[0], column=0, columnspan=2,
                 sticky="e", pady=(14, 0))

    def on_submit():
        v = _gather_basic(fields, state, dlg)
        if v is None:
            return
        is_fixed = bool(is_fixed_var.get())
        fixed_per_section = fixed_editor.gather(
            dlg, v["hours"], v["nsec"], v["force_cont"], is_fixed)
        if fixed_per_section is None:
            return
        v["is_fixed"] = is_fixed
        v["fixed_per_section"] = fixed_per_section
        new_group = _build_new_group(state, clist, is_edit, v)
        old_ids = {c.id for c in clist} if is_edit else set()
        state.replace_course_group(old_ids, new_group)
        dlg.destroy()

    ttk.Button(btn_row, text="취소",
               command=dlg.destroy).pack(side=tk.RIGHT, padx=4)
    ttk.Button(btn_row, text="저장" if is_edit else "추가",
               command=on_submit).pack(side=tk.RIGHT, padx=4)
