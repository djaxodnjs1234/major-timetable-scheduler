"""Cross 관리 다이얼로그."""
import tkinter as tk
from tkinter import ttk, messagebox


def open_cross_manager(parent, state):
    dlg = tk.Toplevel(parent)
    dlg.title("Cross 설정")
    dlg.geometry("560x560")
    dlg.transient(parent)
    dlg.grab_set()

    ttk.Label(
        dlg, padding=10,
        text=("Cross: 묶인 과목들의 분반 시간을 서로 교차로 맞춥니다.\n"
              "예) [A, B] 2분반씩 → (A-A, B-B) 같은 시간,"
              " (A-B, B-A) 같은 시간."),
        foreground="#555", justify="left", wraplength=520
    ).pack(fill=tk.X)

    list_wrap = ttk.LabelFrame(dlg, text="등록된 Cross", padding=8)
    list_wrap.pack(fill=tk.BOTH, expand=True, padx=10, pady=6)

    sb = ttk.Scrollbar(list_wrap, orient=tk.VERTICAL)
    sb.pack(side=tk.RIGHT, fill=tk.Y)
    cross_lb = tk.Listbox(list_wrap, yscrollcommand=sb.set,
                          font=("Consolas", 10), height=8)
    cross_lb.pack(fill=tk.BOTH, expand=True)
    sb.config(command=cross_lb.yview)

    def refresh_cross_list():
        cross_lb.delete(0, tk.END)
        groups = state.base_groups()
        for g in state.crosses:
            names = []
            for bid in g.base_ids:
                grp = groups.get(bid, [])
                nm = grp[0].name if grp else "?"
                names.append(f"{bid}({nm})")
            cross_lb.insert(tk.END, f"{g.id}: " + " ↔ ".join(names))

    refresh_cross_list()

    btns = ttk.Frame(dlg)
    btns.pack(fill=tk.X, padx=10, pady=(0, 6))

    def on_delete():
        sel = cross_lb.curselection()
        if not sel:
            return
        state.delete_cross(sel[0])
        refresh_cross_list()

    ttk.Button(btns, text="선택 Cross 삭제",
               command=on_delete).pack(side=tk.LEFT)

    add_wrap = ttk.LabelFrame(dlg, text="새 Cross 추가", padding=8)
    add_wrap.pack(fill=tk.BOTH, expand=True, padx=10, pady=6)

    ttk.Label(add_wrap,
              text="묶을 과목(base)을 여러 개 선택하세요 (Ctrl/Shift 클릭):",
              foreground="#555"
              ).pack(anchor="w", pady=(0, 4))

    cand_wrap = ttk.Frame(add_wrap)
    cand_wrap.pack(fill=tk.BOTH, expand=True)
    csb = ttk.Scrollbar(cand_wrap, orient=tk.VERTICAL)
    csb.pack(side=tk.RIGHT, fill=tk.Y)
    cand_lb = tk.Listbox(cand_wrap, selectmode="multiple",
                         yscrollcommand=csb.set,
                         font=("Consolas", 9), height=10,
                         exportselection=False)
    cand_lb.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
    csb.config(command=cand_lb.yview)

    groups = state.base_groups()
    base_ids_sorted = sorted(groups.keys(),
                             key=lambda b: (groups[b][0].grade, b))
    for bid in base_ids_sorted:
        g = groups[bid]
        rep = g[0]
        label = (f"[{rep.grade}] {rep.course_type:2s} {bid:10s} "
                 f"{rep.name}  (분반 {len(g)}개, {rep.hours_per_week}h)")
        cand_lb.insert(tk.END, label)

    def on_add():
        sel = cand_lb.curselection()
        if len(sel) < 2:
            messagebox.showwarning("안내", "2개 이상의 과목을 선택하세요.",
                                   parent=dlg)
            return
        chosen = [base_ids_sorted[i] for i in sel]
        sec_counts = [len(groups[b]) for b in chosen]
        hours = [groups[b][0].hours_per_week for b in chosen]
        if len(set(sec_counts)) > 1:
            messagebox.showwarning(
                "검증 실패",
                f"분반 수가 서로 다릅니다: {sec_counts}.\n"
                "Cross 로 묶으려면 각 과목의 분반 수가 같아야 합니다.",
                parent=dlg)
            return
        if len(set(hours)) > 1:
            messagebox.showwarning(
                "검증 실패",
                f"총 시수가 서로 다릅니다: {hours}.\n"
                "Cross 로 묶으려면 총 시수가 동일해야 합니다.",
                parent=dlg)
            return
        already = set()
        for g in state.crosses:
            already.update(g.base_ids)
        dup = [b for b in chosen if b in already]
        if dup:
            if not messagebox.askyesno(
                    "중복",
                    f"다음 과목은 이미 다른 Cross 에 속해 있습니다: "
                    f"{dup}\n그래도 추가하시겠습니까?", parent=dlg):
                return
        state.add_cross(chosen)
        refresh_cross_list()
        cand_lb.selection_clear(0, tk.END)

    ttk.Button(add_wrap, text="+ 선택 과목으로 Cross 추가",
               command=on_add).pack(anchor="e", pady=(6, 0))

    ttk.Button(dlg, text="닫기",
               command=dlg.destroy).pack(pady=(0, 10))
