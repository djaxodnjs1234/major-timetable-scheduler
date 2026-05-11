"""Tkinter-specific UI helpers — scrollable canvas, time labels, run detection.

WPF 이식 시 통째 교체 대상. compute_runs 만 viewmodel 쪽으로 옮기면
나머지(scrollable canvas, time_label) 는 XAML/C# 에서 다시 쓰게 됨.
"""
import tkinter as tk
from tkinter import ttk

from .theme import EMPTY_BG


def time_label(period: int) -> str:
    """1교시=09:00, 2교시=10:00, ..."""
    return f"{8 + period:02d}:00"


def section_letter(section: int) -> str:
    """1 → A, 2 → B, ..."""
    if section is None or section < 1:
        return ""
    return chr(ord("A") + section - 1)


def compute_runs(assignments):
    """연속 교시 block 탐지: (cid, d, start_p) → 길이, inside 집합 반환.

    Returns:
        run_len: {(cid, day, start_period): length}
        inside:  {(cid, day, period)} — 시작 셀이 아닌 연속 점유 셀
    """
    by_course_day = {}
    for item in assignments:
        cid, d, p, _rid = item
        by_course_day.setdefault((cid, d), set()).add(p)
    run_len = {}
    inside = set()
    for (cid, d), ps in by_course_day.items():
        ps = sorted(ps)
        i = 0
        while i < len(ps):
            j = i
            while j + 1 < len(ps) and ps[j + 1] == ps[j] + 1:
                j += 1
            run_len[(cid, d, ps[i])] = j - i + 1
            for k in range(i + 1, j + 1):
                inside.add((cid, d, ps[k]))
            i = j + 1
    return run_len, inside


def make_scrollable(parent, horizontal=True, bg=EMPTY_BG):
    """parent 안에 스크롤 가능한 inner Frame 생성, inner 반환.

    - horizontal=True: 가로/세로 스크롤바 + Shift+휠 가로 스크롤.
                       inner 폭 = max(요구폭, canvas폭) — 가로 확장 허용.
    - horizontal=False: 세로 스크롤만. inner 폭을 canvas 폭에 강제 일치.
    """
    canvas = tk.Canvas(parent, highlightthickness=0, bg=bg)
    vbar = ttk.Scrollbar(parent, orient="vertical", command=canvas.yview)
    canvas.configure(yscrollcommand=vbar.set)
    vbar.pack(side=tk.RIGHT, fill=tk.Y)

    if horizontal:
        hbar = ttk.Scrollbar(parent, orient="horizontal",
                             command=canvas.xview)
        canvas.configure(xscrollcommand=hbar.set)
        hbar.pack(side=tk.BOTTOM, fill=tk.X)

    canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

    inner = ttk.Frame(canvas)
    win_id = canvas.create_window((0, 0), window=inner, anchor="nw")

    def _resize(_e=None):
        cw = canvas.winfo_width()
        if horizontal:
            req = inner.winfo_reqwidth()
            canvas.itemconfigure(win_id, width=max(req, cw))
        else:
            canvas.itemconfigure(win_id, width=cw)
        canvas.configure(scrollregion=canvas.bbox("all"))
    inner.bind("<Configure>", _resize)
    canvas.bind("<Configure>", _resize)

    def _on_wheel(e):
        canvas.yview_scroll(int(-1 * (e.delta / 120)), "units")

    def _on_shift_wheel(e):
        canvas.xview_scroll(int(-1 * (e.delta / 120)), "units")

    def _bind_wheel(_e):
        canvas.bind_all("<MouseWheel>", _on_wheel)
        if horizontal:
            canvas.bind_all("<Shift-MouseWheel>", _on_shift_wheel)

    def _unbind_wheel(_e):
        canvas.unbind_all("<MouseWheel>")
        if horizontal:
            canvas.unbind_all("<Shift-MouseWheel>")

    canvas.bind("<Enter>", _bind_wheel)
    canvas.bind("<Leave>", _unbind_wheel)

    return inner
