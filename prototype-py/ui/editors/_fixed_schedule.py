"""고정 시간표 입력 위젯 — 분반 × 블록 grid + 검증.

CourseEditor 내부에서만 쓰이는 보조 위젯. UI 의존이라 Tkinter import.
"""
import tkinter as tk
from tkinter import ttk, messagebox

from ..theme import DAY_NAMES, DAYS, PERIODS, LUNCH
from ..widgets import section_letter


def compute_blocks(hours, force_consecutive):
    """학점 기반 블록 구조: 3→[2,1], 4→[2,2], force 시 [hours]."""
    if force_consecutive:
        return [hours]
    return {3: [2, 1], 4: [2, 2]}.get(hours, [hours])


def split_fixed_slots_into_blocks(slots, blocks):
    """기존 fixed_slots [(d, p), ...] 를 블록 크기에 맞춰 [(d, sp), ...] 로 쪼갠다.

    같은 d 로 묶인 연속 p 범위를 첫 교시 = start period 로 기록.
    """
    if not slots:
        return [None] * len(blocks)
    by_run = []
    cur = []
    for s in slots:
        if not cur:
            cur = [s]
        else:
            pd, pp = cur[-1]
            d, p = s
            if d == pd and p == pp + 1:
                cur.append(s)
            else:
                by_run.append(cur)
                cur = [s]
    if cur:
        by_run.append(cur)
    out = [(run[0][0], run[0][1]) for run in by_run]
    while len(out) < len(blocks):
        out.append(None)
    return out[:len(blocks)]


class FixedScheduleEditor:
    """고정 시간표 입력 영역. 분반수/학점/연속할당/체크 변경에 따라 rebuild.

    사용법:
        ed = FixedScheduleEditor(frame, state, s_hours, s_sec, force_cont, is_fixed_var)
        ed.set_existing(clist, rep)   # 수정 모드 일 때
        ed.bind_triggers()            # spinbox/checkbox 변경 시 rebuild
        ed.rebuild()                  # 초기 렌더
        ...
        result = ed.gather(dlg, hours, nsec, force_cont, is_fixed)
        # result: List[List[(d, p, rid)]] 또는 None (검증 실패)
    """

    def __init__(self, frame, state, s_hours, s_sec, force_cont_var,
                 is_fixed_var):
        self.frame = frame
        self.state = state
        self._s_hours = s_hours
        self._s_sec = s_sec
        self._force_cont_var = force_cont_var
        self._is_fixed_var = is_fixed_var
        self.widgets = []  # [si][bi] = (d_cb, p_cb, r_cb)
        self._existing_clist = []
        self._existing_rep = None

    def set_existing(self, clist, rep):
        self._existing_clist = list(clist or [])
        self._existing_rep = rep

    def bind_triggers(self):
        self._is_fixed_var.trace_add("write", self._rebuild_event)
        self._force_cont_var.trace_add("write", self._rebuild_event)
        self._s_hours.config(command=self.rebuild)
        self._s_hours.bind("<KeyRelease>", self._rebuild_event)
        self._s_sec.config(command=self.rebuild)
        self._s_sec.bind("<KeyRelease>", self._rebuild_event)

    def _rebuild_event(self, *_):
        self.rebuild()

    def rebuild(self):
        for w in self.frame.winfo_children():
            w.destroy()
        self.widgets.clear()
        try:
            h = int(self._s_hours.get())
            nsec = int(self._s_sec.get())
        except ValueError:
            h, nsec = 0, 1
        blocks = compute_blocks(h, bool(self._force_cont_var.get()))

        if not self._is_fixed_var.get():
            ttk.Label(self.frame,
                      text="(고정 해제 상태 — 솔버가 자동 배정)",
                      foreground="#888").pack(anchor="w")
            return
        if h <= 0 or nsec < 1:
            ttk.Label(self.frame,
                      text="(학점/분반수를 먼저 입력하세요)",
                      foreground="#888").pack(anchor="w")
            return

        day_opts = DAY_NAMES
        period_opts = [str(p) for p in PERIODS if p != LUNCH]

        ttk.Label(
            self.frame,
            text=f"블록 구성: {'+'.join(str(b) for b in blocks)}  "
                 f"· 분반 {nsec}개  "
                 f"(강의실은 과목 또는 교수 설정을 따름)",
            font=("맑은 고딕", 9, "bold")
        ).pack(anchor="w", pady=(0, 6))

        for si in range(nsec):
            self._build_section_row(si, blocks, day_opts, period_opts)

    def _build_section_row(self, si, blocks, day_opts, period_opts):
        sec_letter = section_letter(si + 1)
        sec_wrap = ttk.LabelFrame(
            self.frame, text=f"{sec_letter} 분반", padding=4)
        sec_wrap.pack(fill=tk.X, pady=(2, 6))

        clist = self._existing_clist
        if clist and si < len(clist):
            existing_slots = list(clist[si].fixed_slots or [])
        else:
            existing_slots = []
        existing_starts = split_fixed_slots_into_blocks(
            existing_slots, blocks)

        sec_widgets = []
        for bi, b in enumerate(blocks):
            row = ttk.Frame(sec_wrap)
            row.pack(fill=tk.X, pady=1)
            ttk.Label(row, text=f"블록{bi+1} ({b}교시)  ",
                      width=14).pack(side=tk.LEFT)

            ttk.Label(row, text="요일").pack(side=tk.LEFT)
            d_cb = ttk.Combobox(row, state="readonly", width=4,
                                values=day_opts)
            d_cb.pack(side=tk.LEFT, padx=(2, 8))

            ttk.Label(row, text="시작교시").pack(side=tk.LEFT)
            p_cb = ttk.Combobox(row, state="readonly", width=4,
                                values=period_opts)
            p_cb.pack(side=tk.LEFT, padx=(2, 8))

            if bi < len(existing_starts) and existing_starts[bi]:
                d0, sp0 = existing_starts[bi]
                d_cb.set(day_opts[d0])
                p_cb.set(str(sp0))
            else:
                d_cb.set(day_opts[(si + bi) % DAYS])
                p_cb.set(period_opts[0])
            sec_widgets.append((d_cb, p_cb))
        self.widgets.append(sec_widgets)

    def gather(self, dlg, hours, nsec, force_cont, is_fixed):
        """현재 위젯 값 → fixed_per_section 리스트. 검증 실패 시 None."""
        if not is_fixed:
            return [[] for _ in range(nsec)]

        blocks = compute_blocks(hours, force_cont)
        if len(self.widgets) < nsec:
            messagebox.showerror(
                "오류",
                "고정 시간표 입력이 불완전합니다. (UI 새로고침 필요)",
                parent=dlg)
            return None

        result = []
        for si in range(nsec):
            sec_slots = []
            for bi, b in enumerate(blocks):
                d_cb, p_cb = self.widgets[si][bi]
                try:
                    d_idx = DAY_NAMES.index(d_cb.get())
                    sp = int(p_cb.get())
                except (ValueError, IndexError):
                    messagebox.showerror(
                        "오류",
                        f"{section_letter(si+1)}분반 블록{bi+1}: "
                        f"요일/시작교시를 선택하세요.", parent=dlg)
                    return None
                for k in range(b):
                    pp = sp + k
                    if pp == LUNCH or pp < 1 or pp > 9:
                        messagebox.showerror(
                            "오류",
                            f"{section_letter(si+1)}분반 "
                            f"블록{bi+1}: {sp}교시 + {b}교시가 "
                            f"점심/유효 범위를 벗어납니다.",
                            parent=dlg)
                        return None
                    sec_slots.append((d_idx, pp))
            result.append(sec_slots)
        return result
