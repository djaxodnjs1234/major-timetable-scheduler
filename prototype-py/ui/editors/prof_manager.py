"""교수 편집 다이얼로그 — allowed_rooms 체크박스 + unavailable_slots 그리드.

사이드바에서 호출. state.update_professor 가 즉시 파일에 persist 한다.
"""
import tkinter as tk
from tkinter import ttk

from ..theme import DAY_NAMES, DAYS, PERIODS, LUNCH


def open_prof_editor(parent, state, idx):
    """state.professors[idx] 한 명을 편집."""
    if not (0 <= idx < len(state.professors)):
        return
    prof = state.professors[idx]

    dlg = tk.Toplevel(parent)
    dlg.title(f"교수 편집 — {prof.name}")
    dlg.geometry("560x600")
    dlg.transient(parent)
    dlg.grab_set()

    ttk.Label(
        dlg, padding=10,
        text=(f"{prof.name} ({prof.id})\n"
              "허용 강의실: 미선택 = 모든 방 허용. 선택 시 그 방들만 사용.\n"
              "팀티칭 시 모든 참여 교수의 교집합이 적용됩니다."),
        foreground="#555", justify="left", wraplength=520,
        font=("맑은 고딕", 10)
    ).pack(fill=tk.X)

    body = ttk.Frame(dlg)
    body.pack(fill=tk.BOTH, expand=True, padx=10, pady=4)

    rooms_frame = ttk.LabelFrame(body, text="허용 강의실 (다중 선택)",
                                 padding=6)
    rooms_frame.pack(fill=tk.X, pady=(0, 6))

    room_vars = {}
    cols = 4
    cur_allowed = set(getattr(prof, "allowed_rooms", []) or [])
    for i, r in enumerate(state.rooms):
        v = tk.BooleanVar(value=(r.id in cur_allowed))
        room_vars[r.id] = v
        ttk.Checkbutton(rooms_frame, text=r.id, variable=v
                        ).grid(row=i // cols, column=i % cols,
                               sticky="w", padx=4, pady=2)

    slots_frame = ttk.LabelFrame(body, text="불가능 시간 (체크 = 불가)",
                                 padding=6)
    slots_frame.pack(fill=tk.BOTH, expand=True)

    slot_vars = {}
    cur_un = set(tuple(s) for s in (prof.unavailable_slots or []))

    ttk.Label(slots_frame, text="").grid(row=0, column=0)
    for di, dn in enumerate(DAY_NAMES):
        ttk.Label(slots_frame, text=dn,
                  font=("맑은 고딕", 9, "bold")
                  ).grid(row=0, column=1 + di, padx=4)
    for pi, p in enumerate(PERIODS):
        ttk.Label(slots_frame, text=f"{p}교시"
                  ).grid(row=1 + pi, column=0, sticky="w", padx=4)
        for d in range(DAYS):
            if p == LUNCH:
                ttk.Label(slots_frame, text="-",
                          foreground="#aaa"
                          ).grid(row=1 + pi, column=1 + d)
                continue
            v = tk.BooleanVar(value=((d, p) in cur_un))
            slot_vars[(d, p)] = v
            ttk.Checkbutton(slots_frame, variable=v
                            ).grid(row=1 + pi, column=1 + d)

    btn_row = ttk.Frame(dlg)
    btn_row.pack(fill=tk.X, padx=10, pady=(4, 10))

    def on_save():
        ar = [rid for rid, v in room_vars.items() if v.get()]
        us = sorted((d, p) for (d, p), v in slot_vars.items() if v.get())
        state.update_professor(idx, allowed_rooms=ar, unavailable_slots=us)
        dlg.destroy()

    ttk.Button(btn_row, text="저장",
               command=on_save).pack(side=tk.RIGHT, padx=4)
    ttk.Button(btn_row, text="취소",
               command=dlg.destroy).pack(side=tk.RIGHT, padx=4)
