"""좌측 사이드바 — 3탭 (교과목 / 교수 / 강의실) Notebook.

각 패널은 리스트 + 추가/수정/삭제. state(AppState) 를 구독해 자동 갱신.
교수/강의실 변경은 state.add_room/add_professor 등이 즉시 파일에 persist.
"""
import tkinter as tk
from tkinter import ttk, messagebox

from .theme import FIXED_LIST_BG
from .widgets import section_letter
from .editors import open_course_editor, open_prof_editor


# --------------------------------------------------------------------------
# 교과목 패널 (기존 사이드바 본체)
# --------------------------------------------------------------------------
class CourseSidebar:
    """과목 리스트 + 추가/수정/삭제."""

    def __init__(self, parent, root, state):
        self.root = root
        self.state = state
        self._list_entries = []  # [(base_id, [Course, ...]), ...]
        self._build(parent)
        self.refresh()

    def _build(self, parent):
        header = ttk.Frame(parent)
        header.pack(fill=tk.X, padx=6, pady=(6, 2))
        ttk.Label(header, text="교과목 목록",
                  font=("맑은 고딕", 11, "bold")).pack(side=tk.LEFT)
        self._count_label = ttk.Label(header, text="", foreground="#666")
        self._count_label.pack(side=tk.RIGHT)

        list_frame = ttk.Frame(parent)
        list_frame.pack(fill=tk.BOTH, expand=True, padx=6, pady=(0, 4))
        sb = ttk.Scrollbar(list_frame, orient=tk.VERTICAL)
        sb.pack(side=tk.RIGHT, fill=tk.Y)
        self.listbox = tk.Listbox(
            list_frame, yscrollcommand=sb.set, exportselection=False,
            font=("Consolas", 9), activestyle="dotbox")
        self.listbox.pack(fill=tk.BOTH, expand=True)
        sb.config(command=self.listbox.yview)
        self.listbox.bind("<Double-Button-1>",
                          lambda _e: self._edit_selected())

        btns = ttk.Frame(parent)
        btns.pack(fill=tk.X, padx=6, pady=6)
        ttk.Button(btns, text="+ 추가",
                   command=lambda: open_course_editor(
                       self.root, self.state, None)
                   ).pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)
        ttk.Button(btns, text="수정", command=self._edit_selected
                   ).pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)
        ttk.Button(btns, text="삭제", command=self._delete_selected
                   ).pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)

    def refresh(self):
        groups = self.state.base_groups()
        self._list_entries = list(groups.items())
        self._list_entries.sort(
            key=lambda e: (e[1][0].grade, e[1][0].course_type, e[0]))

        self.listbox.delete(0, tk.END)
        for bid, clist in self._list_entries:
            rep = clist[0]
            letters = ",".join(section_letter(c.section) for c in clist)
            fixed_any = any(c.is_fixed for c in clist)
            star = "★" if fixed_any else " "
            sec_part = f" ({letters})" if len(clist) > 1 else ""
            label = (f"{star} [{rep.grade}] {rep.course_type:2s} "
                     f"{bid:10s} {rep.name}{sec_part}")
            self.listbox.insert(tk.END, label)
            if fixed_any:
                self.listbox.itemconfig(tk.END, bg=FIXED_LIST_BG)

        self._count_label.config(
            text=f"{len(self._list_entries)}과목 · "
                 f"{len(self.state.courses)}분반")

    def _selected_entry(self):
        sel = self.listbox.curselection()
        if not sel:
            return None
        return self._list_entries[sel[0]]

    def _edit_selected(self):
        entry = self._selected_entry()
        if not entry:
            messagebox.showinfo("안내", "수정할 과목을 선택하세요.")
            return
        _bid, clist = entry
        open_course_editor(self.root, self.state, clist)

    def _delete_selected(self):
        entry = self._selected_entry()
        if not entry:
            messagebox.showinfo("안내", "삭제할 과목을 선택하세요.")
            return
        _bid, clist = entry
        if len(clist) == 1:
            self._confirm_delete([clist[0]])
            return
        self._pick_section(clist)

    def _pick_section(self, clist):
        dlg = tk.Toplevel(self.root)
        dlg.title("분반 선택")
        dlg.geometry("280x200")
        dlg.transient(self.root)
        dlg.grab_set()

        ttk.Label(dlg, text=f"{clist[0].name}",
                  font=("맑은 고딕", 11, "bold")).pack(pady=(12, 4))
        ttk.Label(dlg, text="삭제할 대상을 선택하세요",
                  foreground="#666").pack(pady=(0, 8))

        btn_area = ttk.Frame(dlg)
        btn_area.pack(fill=tk.X, padx=20, pady=4)

        def on_pick(c):
            dlg.destroy()
            self._confirm_delete([c])

        for c in clist:
            letter = section_letter(c.section)
            fixed_tag = " ★" if c.is_fixed else ""
            ttk.Button(btn_area, text=f"{letter}분반{fixed_tag}",
                       command=lambda c=c: on_pick(c)
                       ).pack(fill=tk.X, pady=2)

        ttk.Button(btn_area, text="전체 분반 삭제",
                   command=lambda: (dlg.destroy(),
                                    self._confirm_delete(clist))
                   ).pack(fill=tk.X, pady=(8, 2))
        ttk.Button(dlg, text="취소", command=dlg.destroy).pack(pady=8)

    def _confirm_delete(self, target_list):
        names = ", ".join(
            f"{c.id} {c.name}({section_letter(c.section)})"
            for c in target_list)
        if not messagebox.askyesno(
                "삭제 확인",
                f"다음 항목을 삭제하시겠습니까?\n{names}\n"
                f"(삭제 후 '재계산'을 눌러야 반영됩니다)"):
            return
        self.state.delete_courses(c.id for c in target_list)


# --------------------------------------------------------------------------
# 교수 패널
# --------------------------------------------------------------------------
class ProfPanel:
    def __init__(self, parent, root, state):
        self.root = root
        self.state = state
        self._build(parent)
        self.refresh()

    def _build(self, parent):
        header = ttk.Frame(parent)
        header.pack(fill=tk.X, padx=6, pady=(6, 2))
        ttk.Label(header, text="교수 목록",
                  font=("맑은 고딕", 11, "bold")).pack(side=tk.LEFT)
        self._count_label = ttk.Label(header, text="", foreground="#666")
        self._count_label.pack(side=tk.RIGHT)

        ttk.Label(parent,
                  text="(편집 내용은 즉시 data_source.py 에 저장됩니다)",
                  foreground="#666"
                  ).pack(fill=tk.X, padx=6)

        list_frame = ttk.Frame(parent)
        list_frame.pack(fill=tk.BOTH, expand=True, padx=6, pady=(2, 4))
        sb = ttk.Scrollbar(list_frame, orient=tk.VERTICAL)
        sb.pack(side=tk.RIGHT, fill=tk.Y)
        self.listbox = tk.Listbox(
            list_frame, yscrollcommand=sb.set, exportselection=False,
            font=("Consolas", 9), activestyle="dotbox")
        self.listbox.pack(fill=tk.BOTH, expand=True)
        sb.config(command=self.listbox.yview)
        self.listbox.bind("<Double-Button-1>",
                          lambda _e: self._edit_selected())

        # 인라인 추가 입력란 — 다이얼로그 없이 바로 추가.
        add_frame = ttk.Frame(parent)
        add_frame.pack(fill=tk.X, padx=6, pady=(0, 4))
        self._add_entry = tk.Entry(add_frame)
        self._add_entry.pack(side=tk.LEFT, fill=tk.X, expand=True)
        self._add_entry.bind("<Return>", lambda _e: self._on_add())
        ttk.Button(add_frame, text="+ 추가", command=self._on_add
                   ).pack(side=tk.LEFT, padx=(4, 0))

        btns = ttk.Frame(parent)
        btns.pack(fill=tk.X, padx=6, pady=6)
        ttk.Button(btns, text="수정", command=self._edit_selected
                   ).pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)
        ttk.Button(btns, text="삭제", command=self._delete_selected
                   ).pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)

    def refresh(self):
        self.listbox.delete(0, tk.END)
        for p in self.state.professors:
            ar = getattr(p, "allowed_rooms", []) or []
            us = p.unavailable_slots or []
            tag = []
            if ar:
                tag.append(f"방{len(ar)}")
            if us:
                tag.append(f"불가{len(us)}")
            suffix = f"  [{' '.join(tag)}]" if tag else ""
            self.listbox.insert(tk.END, f"{p.name}{suffix}")
        self._count_label.config(text=f"{len(self.state.professors)}명")

    def _on_add(self):
        name = self._add_entry.get().strip()
        if not name:
            return
        ok, msg = self.state.add_professor(name)
        if not ok:
            messagebox.showerror("오류", msg)
            return
        self._add_entry.delete(0, tk.END)

    def _edit_selected(self):
        sel = self.listbox.curselection()
        if not sel:
            messagebox.showinfo("안내", "편집할 교수를 선택하세요.")
            return
        open_prof_editor(self.root, self.state, sel[0])

    def _delete_selected(self):
        sel = self.listbox.curselection()
        if not sel:
            messagebox.showinfo("안내", "삭제할 교수를 선택하세요.")
            return
        idx = sel[0]
        prof = self.state.professors[idx]
        if not messagebox.askyesno(
                "삭제 확인",
                f"{prof.name} 을(를) 삭제하시겠습니까?\n"
                f"(즉시 data_source.py 에 반영됩니다)"):
            return
        ok, used = self.state.delete_professor(idx)
        if not ok:
            messagebox.showerror(
                "삭제 불가",
                f"이 교수는 다음 과목에서 사용 중입니다:\n"
                f"{', '.join(used[:10])}"
                f"{' ...' if len(used) > 10 else ''}\n"
                f"먼저 해당 과목의 담당교수를 변경하세요.")


# --------------------------------------------------------------------------
# 강의실 패널
# --------------------------------------------------------------------------
class RoomPanel:
    def __init__(self, parent, root, state):
        self.root = root
        self.state = state
        self._build(parent)
        self.refresh()

    def _build(self, parent):
        header = ttk.Frame(parent)
        header.pack(fill=tk.X, padx=6, pady=(6, 2))
        ttk.Label(header, text="강의실 목록",
                  font=("맑은 고딕", 11, "bold")).pack(side=tk.LEFT)
        self._count_label = ttk.Label(header, text="", foreground="#666")
        self._count_label.pack(side=tk.RIGHT)

        ttk.Label(parent,
                  text="(편집 내용은 즉시 data_source.py 에 저장됩니다)",
                  foreground="#666"
                  ).pack(fill=tk.X, padx=6)

        list_frame = ttk.Frame(parent)
        list_frame.pack(fill=tk.BOTH, expand=True, padx=6, pady=(2, 4))
        sb = ttk.Scrollbar(list_frame, orient=tk.VERTICAL)
        sb.pack(side=tk.RIGHT, fill=tk.Y)
        self.listbox = tk.Listbox(
            list_frame, yscrollcommand=sb.set, exportselection=False,
            font=("Consolas", 10), activestyle="dotbox")
        self.listbox.pack(fill=tk.BOTH, expand=True)
        sb.config(command=self.listbox.yview)

        # 인라인 추가 입력란
        add_frame = ttk.Frame(parent)
        add_frame.pack(fill=tk.X, padx=6, pady=(0, 4))
        self._add_entry = tk.Entry(add_frame)
        self._add_entry.pack(side=tk.LEFT, fill=tk.X, expand=True)
        self._add_entry.bind("<Return>", lambda _e: self._on_add())
        ttk.Button(add_frame, text="+ 추가", command=self._on_add
                   ).pack(side=tk.LEFT, padx=(4, 0))

        btns = ttk.Frame(parent)
        btns.pack(fill=tk.X, padx=6, pady=6)
        ttk.Button(btns, text="삭제", command=self._delete_selected
                   ).pack(side=tk.LEFT, fill=tk.X, expand=True, padx=2)

    def refresh(self):
        self.listbox.delete(0, tk.END)
        for r in self.state.rooms:
            self.listbox.insert(tk.END, r.id)
        self._count_label.config(text=f"{len(self.state.rooms)}개")

    def _on_add(self):
        rid = self._add_entry.get().strip()
        if not rid:
            return
        ok, msg = self.state.add_room(rid)
        if not ok:
            messagebox.showerror("오류", msg)
            return
        self._add_entry.delete(0, tk.END)

    def _delete_selected(self):
        sel = self.listbox.curselection()
        if not sel:
            messagebox.showinfo("안내", "삭제할 강의실을 선택하세요.")
            return
        idx = sel[0]
        rid = self.state.rooms[idx].id
        if not messagebox.askyesno(
                "삭제 확인",
                f"{rid} 을(를) 삭제하시겠습니까?\n"
                f"(즉시 data_source.py 에 반영됩니다)"):
            return
        ok, used = self.state.delete_room(idx)
        if not ok:
            messagebox.showerror(
                "삭제 불가",
                f"이 강의실은 다음에서 사용 중입니다:\n"
                f"{', '.join(used[:10])}"
                f"{' ...' if len(used) > 10 else ''}\n"
                f"먼저 해당 과목/교수의 강의실 설정을 변경하세요.")


# --------------------------------------------------------------------------
# 컨테이너
# --------------------------------------------------------------------------
class Sidebar:
    """3탭 컨테이너 — 교과목 / 교수 / 강의실."""

    def __init__(self, parent, root, state):
        self.state = state
        nb = ttk.Notebook(parent)
        nb.pack(fill=tk.BOTH, expand=True)

        f1 = ttk.Frame(nb)
        nb.add(f1, text="교과목")
        self.course_panel = CourseSidebar(f1, root, state)

        f2 = ttk.Frame(nb)
        nb.add(f2, text="교수")
        self.prof_panel = ProfPanel(f2, root, state)

        f3 = ttk.Frame(nb)
        nb.add(f3, text="강의실")
        self.room_panel = RoomPanel(f3, root, state)

    def refresh(self):
        self.course_panel.refresh()
        self.prof_panel.refresh()
        self.room_panel.refresh()
