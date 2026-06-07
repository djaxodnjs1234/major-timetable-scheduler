"""렌더 함수들 — assignment + state 를 받아 Tkinter widgets 를 그린다.

WPF 이식 시 이 파일은 통째 교체 (XAML DataTemplate + ItemsControl 패턴).
draw_grid / render_view / render_unified 는 모두 같은 grid 레이아웃 콘셉트 —
(요일 × 교시) 셀에 ``cell_fn(d, p) → list[(text, color, rowspan)]`` 매핑.
"""
import tkinter as tk
from tkinter import ttk

from .theme import (
    DAY_NAMES, DAYS, PERIODS, LUNCH, GRADES,
    GRADE_COLORS, EMPTY_BG, LUNCH_BG, HEADER_BG,
    COURSE_TYPES,
)
from .widgets import make_scrollable, time_label, section_letter


def _display_name(name_map, item_id):
    if not item_id:
        return item_id
    name = (name_map or {}).get(item_id)
    return name or item_id


def format_course_cell(course, section_count, rooms=None,
                       professor_names=None, room_names=None) -> str:
    """과목명·분반 / 교수명 / 강의실 — 컴팩트 3줄 포맷.

    rooms: 해당 슬롯에서 점유 중인 방 id 집합. 다중방이면 콤마 결합,
    4개 이상이면 `방1 외 N` 으로 축약.
    """
    sec_label = (f"·{section_letter(course.section)}"
                 if section_count > 1 else "")
    professor = _display_name(professor_names, course.professor_id)
    line = f"{course.name}{sec_label}\n{professor}"
    if rooms:
        rs = sorted(_display_name(room_names, rid) for rid in rooms)
        if len(rs) <= 3:
            room_str = ",".join(rs)
        else:
            room_str = f"{rs[0]} 외 {len(rs)-1}"
        line = f"{line}\n{room_str}"
    return line


def draw_grid(parent, cell_fn):
    """공용 grid 렌더러. cell_fn(d, p) → list[(text, color, rowspan)]."""
    for w in parent.winfo_children():
        w.destroy()

    container = make_scrollable(parent)
    grid = ttk.Frame(container)
    grid.pack(fill=tk.BOTH, expand=True, padx=4, pady=4)

    tk.Label(grid, text="교시", width=4, relief="solid", bd=1,
             bg=HEADER_BG, font=("맑은 고딕", 8, "bold")
             ).grid(row=0, column=0, sticky="nsew")
    tk.Label(grid, text="시간", width=6, relief="solid", bd=1,
             bg=HEADER_BG, font=("맑은 고딕", 8, "bold")
             ).grid(row=0, column=1, sticky="nsew")
    for i, dn in enumerate(DAY_NAMES):
        tk.Label(grid, text=dn, relief="solid", bd=1,
                 bg=HEADER_BG, font=("맑은 고딕", 9, "bold")
                 ).grid(row=0, column=2 + i, sticky="nsew")
        grid.columnconfigure(2 + i, weight=1, minsize=64)

    covered = set()
    for ri, period in enumerate(PERIODS, start=1):
        label = "점심" if period == LUNCH else f"{period}"
        tk.Label(grid, text=label, relief="solid", bd=1,
                 bg="#F8F8F8", font=("맑은 고딕", 8)
                 ).grid(row=ri, column=0, sticky="nsew")
        tk.Label(grid, text=time_label(period), relief="solid", bd=1,
                 bg="#F8F8F8", font=("맑은 고딕", 8)
                 ).grid(row=ri, column=1, sticky="nsew")

        for d in range(DAYS):
            if (ri, 2 + d) in covered:
                continue
            if period == LUNCH:
                tk.Label(grid, text="점심", bg=LUNCH_BG, relief="solid",
                         bd=1, font=("맑은 고딕", 8)
                         ).grid(row=ri, column=2 + d, sticky="nsew",
                                ipady=4)
                continue
            entries = cell_fn(d, period)
            rs = max((e[2] for e in entries), default=1)
            cell = tk.Frame(grid, bg=EMPTY_BG, relief="solid", bd=1,
                            highlightthickness=0)
            cell.grid(row=ri, column=2 + d, rowspan=rs, sticky="nsew")
            for k in range(1, rs):
                covered.add((ri + k, 2 + d))
            if not entries:
                tk.Label(cell, text="", bg=EMPTY_BG
                         ).pack(fill=tk.BOTH, expand=True, ipady=6)
            else:
                for text, color, _r in entries:
                    tk.Label(cell, text=text, bg=color,
                             font=("맑은 고딕", 7),
                             justify="center", wraplength=60,
                             relief="flat", bd=0, padx=1, pady=1
                             ).pack(fill=tk.BOTH, expand=True)
        grid.rowconfigure(ri, weight=1)


def render_view(tab_dict, state, run_len, inside, accept_fn, color_fn):
    """탭별 시간표 (학년별/강의실별/교수별).

    accept_fn(key, course, rid) → 이 슬롯을 해당 탭에 포함할지.
    color_fn(key, course)       → bg color.
    """
    assignments = state.current_assignment()
    course_map = state.course_map
    professor_names = {p.id: p.name for p in state.professors}
    room_names = {r.id: r.name for r in state.rooms}
    for key, frame in tab_dict.items():
        # cells[(d, p)] = {cid: set(rid)} — 다중방 정보 보존
        cells = {(d, p): {} for d in range(DAYS) for p in PERIODS}
        for (cid, d, p, rid) in assignments:
            if accept_fn(key, course_map[cid], rid):
                cells[(d, p)].setdefault(cid, set()).add(rid)

        def cell_fn(d, p, cells=cells, key=key):
            result = []
            for cid, rids in cells[(d, p)].items():
                if (cid, d, p) in inside:
                    continue
                c = course_map[cid]
                rs = run_len.get((cid, d, p), 1)
                result.append((
                    format_course_cell(
                        c, state.section_count(cid), rids,
                        professor_names, room_names),
                    color_fn(key, c), rs))
            return result

        draw_grid(frame, cell_fn)


def render_unified(unified_frame, state, run_len, inside):
    """통합 시간표: 학년별 컬럼 분할 + 연속 블록 셀 병합.

    state.expand_all_grades:
      - False(default): 그 요일에 교과목이 있는 학년 컬럼만 표시.
      - True: 모든 학년 컬럼 강제 표시 (빈 학년도 1칸).
    """
    assignments = state.current_assignment()
    course_map = state.course_map
    professor_names = {p.id: p.name for p in state.professors}
    room_names = {r.id: r.name for r in state.rooms}
    expand_all = bool(getattr(state, "expand_all_grades", False))

    # slot_courses[(d, p)] = {cid: set(rid)} — 다중방 정보 보존
    slot_courses = {(d, p): {} for d in range(DAYS) for p in PERIODS}
    for (cid, d, p, rid) in assignments:
        slot_courses[(d, p)].setdefault(cid, set()).add(rid)

    max_width = {(d, g): 0 for d in range(DAYS) for g in GRADES}
    for d in range(DAYS):
        for p in PERIODS:
            if p == LUNCH:
                continue
            count_by_grade = {}
            for cid in slot_courses[(d, p)]:
                g = course_map[cid].grade
                count_by_grade[g] = count_by_grade.get(g, 0) + 1
            for g, cnt in count_by_grade.items():
                if cnt > max_width[(d, g)]:
                    max_width[(d, g)] = cnt

    day_groups = []
    for d in range(DAYS):
        if expand_all:
            entries = [(g, max(max_width[(d, g)], 1)) for g in GRADES]
        else:
            entries = [(g, max_width[(d, g)]) for g in GRADES
                       if max_width[(d, g)] > 0]
        if not entries:
            entries = [(None, 1)]
        day_groups.append((d, entries))

    for w in unified_frame.winfo_children():
        w.destroy()

    container = make_scrollable(unified_frame)
    grid = ttk.Frame(container)
    grid.pack(fill=tk.BOTH, expand=True, padx=4, pady=4)

    tk.Label(grid, text="교시", bg=HEADER_BG, relief="solid", bd=1,
             font=("맑은 고딕", 8, "bold"), width=4
             ).grid(row=0, column=0, rowspan=2, sticky="nsew")
    tk.Label(grid, text="시간", bg=HEADER_BG, relief="solid", bd=1,
             font=("맑은 고딕", 8, "bold"), width=5
             ).grid(row=0, column=1, rowspan=2, sticky="nsew")

    col = 2
    for d, entries in day_groups:
        day_total = sum(w for _, w in entries)
        tk.Label(grid, text=DAY_NAMES[d], bg=HEADER_BG, relief="solid",
                 bd=1, font=("맑은 고딕", 10, "bold")
                 ).grid(row=0, column=col, columnspan=day_total,
                        sticky="nsew")
        sub = col
        for g, w in entries:
            if g is None:
                tk.Label(grid, text="", bg=HEADER_BG, relief="solid", bd=1
                         ).grid(row=1, column=sub, columnspan=w,
                                sticky="nsew")
            else:
                tk.Label(grid, text="", bg=GRADE_COLORS[g],
                         relief="solid", bd=1, height=1
                         ).grid(row=1, column=sub, columnspan=w,
                                sticky="nsew")
            for k in range(w):
                grid.columnconfigure(sub + k, weight=0, minsize=56)
            sub += w
        col += day_total
    grid.columnconfigure(0, weight=0, minsize=36)
    grid.columnconfigure(1, weight=0, minsize=44)

    covered = set()
    for ri, period in enumerate(PERIODS, start=2):
        label = "점심" if period == LUNCH else f"{period}"
        tk.Label(grid, text=label, bg="#F8F8F8", relief="solid", bd=1,
                 font=("맑은 고딕", 8)
                 ).grid(row=ri, column=0, sticky="nsew")
        tk.Label(grid, text=time_label(period), bg="#F8F8F8",
                 relief="solid", bd=1, font=("맑은 고딕", 8)
                 ).grid(row=ri, column=1, sticky="nsew")

        col = 2
        for d, entries in day_groups:
            for g, w in entries:
                if period == LUNCH:
                    for k in range(w):
                        tk.Label(grid, text="점심", bg=LUNCH_BG,
                                 relief="solid", bd=1,
                                 font=("맑은 고딕", 8)
                                 ).grid(row=ri, column=col + k,
                                        sticky="nsew", ipady=4)
                    col += w
                    continue

                if g is None:
                    for k in range(w):
                        tk.Label(grid, text="", bg=EMPTY_BG,
                                 relief="solid", bd=1
                                 ).grid(row=ri, column=col + k,
                                        sticky="nsew", ipady=6)
                    col += w
                    continue

                courses_here = [course_map[cid]
                                for cid in slot_courses[(d, period)]
                                if course_map[cid].grade == g
                                and (cid, d, period) not in inside]
                courses_here.sort(key=lambda c: c.section)

                idx = 0
                for k in range(w):
                    if (ri, col + k) in covered:
                        continue
                    if idx < len(courses_here):
                        c = courses_here[idx]
                        idx += 1
                        rids = slot_courses[(d, period)].get(c.id, set())
                        rs = run_len.get((c.id, d, period), 1)
                        tk.Label(grid,
                                 text=format_course_cell(
                                      c, state.section_count(c.id), rids,
                                      professor_names, room_names),
                                 bg=GRADE_COLORS[g], relief="solid",
                                 bd=1, font=("맑은 고딕", 7),
                                 justify="center", wraplength=52
                                 ).grid(row=ri, column=col + k,
                                        rowspan=rs,
                                        sticky="nsew", ipady=1)
                        for k2 in range(1, rs):
                            covered.add((ri + k2, col + k))
                    else:
                        tk.Label(grid, text="", bg=EMPTY_BG,
                                 relief="solid", bd=1
                                 ).grid(row=ri, column=col + k,
                                        sticky="nsew", ipady=6)
                col += w
        grid.rowconfigure(ri, weight=1)
