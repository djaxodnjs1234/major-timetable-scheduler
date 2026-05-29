"""TimetableApp — Tkinter 셸. AppState 를 보관·구독·렌더.

모든 비즈니스 로직은 ``AppState`` 가 담당, 이 클래스는 위젯 생성/이벤트 와이어/
state 변경 시 다시 그리기에 집중. WPF 이식 시 이 모듈은 통째로 교체된다.
"""
import os
import tkinter as tk
from tkinter import ttk, messagebox, filedialog

from .theme import GRADES, GRADE_COLORS, EMPTY_BG, FIXED_LIST_BG
from .widgets import compute_runs
from .viewmodel import AppState
from .views import render_unified, render_view
from .sidebar import Sidebar
from .editors import open_cross_manager, export_to_data_source


class TimetableApp:
    def __init__(self, root, state: AppState,
                 n_solutions: int = 20, m_top: int = 20,
                 time_limit_sec: int = 60,
                 per_solve_time_sec: int = 15):
        self.root = root
        self.state = state
        self._n_solutions = n_solutions
        self._m_top = m_top
        self._time_limit_sec = time_limit_sec
        self._per_solve_time_sec = per_solve_time_sec

        root.title("전공 시간표 자동 생성 시스템")
        root.geometry("1400x820")

        self._build_top_bar()
        self._build_body()
        self.render()

        self.state.subscribe(self._on_state_change)

    # ------------------------------------------------------------------
    # 상단 바
    # ------------------------------------------------------------------
    def _build_top_bar(self):
        top = ttk.Frame(self.root, padding=8)
        top.pack(fill=tk.X)

        ttk.Button(top, text="◀ 이전 해",
                   command=self.state.prev_solution).pack(side=tk.LEFT)
        self.label = ttk.Label(top, text="",
                               font=("맑은 고딕", 12, "bold"))
        self.label.pack(side=tk.LEFT, padx=16)
        ttk.Button(top, text="다음 해 ▶",
                   command=self.state.next_solution).pack(side=tk.LEFT)

        ttk.Label(top, text="   해 선택:").pack(side=tk.LEFT, padx=(24, 4))
        combo_width = 22 if self.state.scores else 10
        self.combo = ttk.Combobox(top, state="readonly", width=combo_width,
                                  values=self._combo_values())
        if self.state.solutions:
            self.combo.current(0)
        self.combo.bind("<<ComboboxSelected>>",
                        lambda _e: self.state.set_idx(self.combo.current()))
        self.combo.pack(side=tk.LEFT)

        self.score_label = ttk.Label(top, text="",
                                     font=("맑은 고딕", 9),
                                     foreground="#555")
        self.score_label.pack(side=tk.LEFT, padx=(16, 0))

        self._recalc_text = tk.StringVar(value="🔄 재계산 (변경 없음)")
        self._recalc_btn = ttk.Button(top, textvariable=self._recalc_text,
                                      command=self._on_recalc,
                                      state="disabled")
        self._recalc_btn.pack(side=tk.LEFT, padx=(24, 0))

        ttk.Button(top, text="🔗 Cross 설정",
                   command=lambda: open_cross_manager(self.root, self.state)
                   ).pack(side=tk.LEFT, padx=(8, 0))

        self.auto_retakes_var = tk.BooleanVar(value=self.state.auto_retakes)
        ttk.Checkbutton(
            top, text="🔁 재수강 자동 고려",
            variable=self.auto_retakes_var,
            command=lambda: self.state.set_auto_retakes(
                self.auto_retakes_var.get()),
        ).pack(side=tk.LEFT, padx=(8, 0))

        ttk.Button(top, text="📂 불러오기",
                   command=self._on_load_file
                   ).pack(side=tk.LEFT, padx=(8, 0))

        ttk.Button(top, text="📤 코드 내보내기",
                   command=lambda: export_to_data_source(self.state,
                                                         self.root)
                   ).pack(side=tk.LEFT, padx=(8, 0))

        legend = ttk.Frame(top)
        legend.pack(side=tk.RIGHT)

        self._expand_var = tk.BooleanVar(value=self.state.expand_all_grades)
        ttk.Checkbutton(
            legend, text="모든 학년 펼침",
            variable=self._expand_var,
            command=lambda: self.state.set_expand_all_grades(
                self._expand_var.get()),
        ).pack(side=tk.LEFT, padx=(0, 8))

        for g in GRADES:
            tk.Label(legend, text=f" {g}학년 ", bg=GRADE_COLORS[g],
                     font=("맑은 고딕", 9), relief="solid", bd=1
                     ).pack(side=tk.LEFT, padx=2)
        tk.Label(legend, text=" ★ 고정 ", bg=FIXED_LIST_BG,
                 font=("맑은 고딕", 9), relief="solid", bd=1
                 ).pack(side=tk.LEFT, padx=(6, 2))

    # ------------------------------------------------------------------
    # 본문 (사이드바 + 노트북)
    # ------------------------------------------------------------------
    def _build_body(self):
        paned = ttk.PanedWindow(self.root, orient=tk.HORIZONTAL)
        paned.pack(fill=tk.BOTH, expand=True, padx=8, pady=8)

        left = ttk.Frame(paned, width=280)
        paned.add(left, weight=0)
        self.sidebar = Sidebar(left, self.root, self.state)

        self._right_frame = ttk.Frame(paned)
        paned.add(self._right_frame, weight=1)

        self._build_notebook()

    def _build_notebook(self):
        self.notebook = ttk.Notebook(self._right_frame)
        self.notebook.pack(fill=tk.BOTH, expand=True)

        self.tabs = {}
        self.unified_frame = ttk.Frame(self.notebook)
        self.notebook.add(self.unified_frame, text="통합 시간표")

        self._add_category("학년별", [(f"{g}학년", g) for g in GRADES])
        self._add_category("강의실별",
                           [(r.name, r.id) for r in self.state.rooms])
        self._add_category("교수별",
                           [(p.name, p.id) for p in self.state.professors])

    def _rebuild_notebook(self):
        """rooms/professors 변경 후 탭 재구성."""
        self.notebook.destroy()
        self._build_notebook()
        self.render()

    def _add_category(self, name, items):
        outer = ttk.Frame(self.notebook)
        self.notebook.add(outer, text=name)
        inner = ttk.Notebook(outer)
        inner.pack(fill=tk.BOTH, expand=True)
        entries = {}
        for label, key in items:
            frame = ttk.Frame(inner)
            inner.add(frame, text=label)
            entries[key] = frame
        self.tabs[name] = entries

    # ------------------------------------------------------------------
    # 파일 불러오기
    # ------------------------------------------------------------------
    def _on_load_file(self):
        import __main__
        main_dir = os.path.dirname(os.path.abspath(
            getattr(__main__, "__file__", os.getcwd())))
        data_dir = os.path.join(main_dir, "data_files")
        if not os.path.isdir(data_dir):
            data_dir = main_dir

        path = filedialog.askopenfilename(
            title="파일 불러오기",
            initialdir=data_dir,
            filetypes=[
                ("지원 형식", "*.xlsx *.db"),
                ("Excel 파일 (개설강좌)", "*.xlsx"),
                ("SQLite DB", "*.db"),
            ],
            parent=self.root,
        )
        if not path:
            return

        self.label.config(text="불러오는 중…")
        self.root.config(cursor="watch")
        self.root.update_idletasks()

        try:
            if path.lower().endswith(".db"):
                from data import load_from_db
                courses, professors, rooms, crosses, retakes, solutions = \
                    load_from_db(path)
            else:
                from data import load_from_xlsx
                courses, professors, rooms = load_from_xlsx(path)
                crosses, retakes, solutions = [], [], []

            self.state.reload_data(
                courses, professors, rooms,
                crosses=crosses, retakes=retakes,
                solutions=solutions or None,
            )
            self._rebuild_notebook()
            self.sidebar.refresh()

            if not solutions:
                self.root.config(cursor="")
                messagebox.showinfo(
                    "데이터 로드 완료",
                    f"{os.path.basename(path)} 로드됨.\n"
                    f"과목 {len(self.state.courses)}개 — '재계산' 버튼으로 시간표를 생성하세요.",
                    parent=self.root,
                )
            else:
                n_sol = len(solutions)
                self.combo["values"] = self._combo_values()
                if self.state.solutions:
                    self.combo.current(0)
                messagebox.showinfo(
                    "DB 불러오기 완료",
                    f"{os.path.basename(path)}\n"
                    f"과목 {len(self.state.courses)}개 · "
                    f"저장된 시간표 {n_sol}개 로드 완료",
                    parent=self.root,
                )
        except Exception as e:
            messagebox.showerror("불러오기 실패", str(e), parent=self.root)
        finally:
            self.root.config(cursor="")

    # ------------------------------------------------------------------
    # 재계산
    # ------------------------------------------------------------------
    def _combo_values(self):
        if self.state.scores:
            return [f"해 {i+1}  (점수 {self.state.scores[i]['total']:.2f})"
                    for i in range(len(self.state.solutions))]
        return [f"해 {i+1}" for i in range(len(self.state.solutions))]

    def _on_recalc(self):
        if self.state.dirty_count == 0:
            return
        self.label.config(text=f"재계산 중… (최대 {self._time_limit_sec}초)")
        self.root.config(cursor="watch")
        self.root.update_idletasks()
        success, status, msg, applied = self.state.commit_recalc(
            time_limit_sec=self._time_limit_sec,
            per_solve_time_sec=self._per_solve_time_sec,
            total_solutions=self._n_solutions,
            top_m=self._m_top)
        self.root.config(cursor="")
        if not success:
            if status == "ERROR":
                messagebox.showerror("오류", msg)
            else:
                messagebox.showwarning("재계산 실패", msg)
            return
        self.combo["values"] = self._combo_values()
        if self.state.solutions:
            self.combo.current(0)
        messagebox.showinfo(
            "완료",
            f"{applied}건 반영 · {len(self.state.solutions)}개의 해를 "
            f"수집했습니다.")

    # ------------------------------------------------------------------
    # 렌더 + 상태 변경 응답
    # ------------------------------------------------------------------
    def _update_recalc_ui(self):
        n = self.state.dirty_count
        if n == 0:
            self._recalc_text.set("🔄 재계산 (변경 없음)")
            self._recalc_btn.config(state="disabled")
        else:
            self._recalc_text.set(f"🔄 재계산 ({n}건 변경)")
            self._recalc_btn.config(state="normal")

    def render(self):
        if not self.state.solutions:
            self.label.config(text="수집된 해 없음")
            self.score_label.config(text="")
            return
        self.label.config(
            text=f"해 {self.state.idx + 1} / {len(self.state.solutions)}")
        if self.state.scores and self.state.idx < len(self.state.scores):
            from scoring import SC_KEYS
            sd = self.state.scores[self.state.idx]
            parts = "  ".join(f"{k} {sd[k]:.2f}" for k in SC_KEYS)
            self.score_label.config(
                text=f"total {sd['total']:.2f}   {parts}")
        else:
            self.score_label.config(text="")

        a = self.state.current_assignment()
        run_len, inside = compute_runs(a)
        render_unified(self.unified_frame, self.state, run_len, inside)
        render_view(self.tabs["학년별"], self.state, run_len, inside,
                    accept_fn=lambda g, c, _: c.grade == g,
                    color_fn=lambda g, _: GRADE_COLORS[g])
        render_view(self.tabs["강의실별"], self.state, run_len, inside,
                    accept_fn=lambda rid, _, r: r == rid,
                    color_fn=lambda _, c: GRADE_COLORS.get(c.grade, EMPTY_BG))
        render_view(self.tabs["교수별"], self.state, run_len, inside,
                    accept_fn=lambda pid, c, _: c.professor_id == pid,
                    color_fn=lambda _, c: GRADE_COLORS.get(c.grade, EMPTY_BG))

    def _on_state_change(self, kind):
        # 단순 전략: 어떤 변경이든 사이드바 + 그리드 + 재계산 UI 동기화.
        # WPF 이식 시 이 부분이 ViewModel 의 INotifyPropertyChanged 자리.
        if kind in ("idx", "recalc") and self.state.solutions:
            self.combo.current(self.state.idx)
        if kind in ("rollback", "auto_retakes"):
            self.auto_retakes_var.set(self.state.auto_retakes)
        if kind in ("courses", "crosses", "professors", "rooms",
                    "rollback", "recalc"):
            self.sidebar.refresh()
        if kind == "expand_all_grades":
            self._expand_var.set(self.state.expand_all_grades)
        self._update_recalc_ui()
        self.render()


def launch(solutions, courses, professors, rooms, crosses=None,
           retakes=None, auto_retakes=True, scores=None,
           n_solutions: int = 20, m_top: int = 20,
           time_limit_sec: int = 60,
           per_solve_time_sec: int = 15):
    """Entry point — main.py 가 호출.

    ``n_solutions``, ``m_top``, ``time_limit_sec``, ``per_solve_time_sec`` 은
    GUI 의 🔄 재계산 버튼이 호출하는 ``commit_recalc`` 에 그대로 전달 —
    main.py 의 시간/해 노브가 재계산까지 일관되게 적용되도록.
    """
    state = AppState(
        solutions=solutions, courses=courses, professors=professors,
        rooms=rooms, crosses=crosses, retakes=retakes,
        auto_retakes=auto_retakes, scores=scores,
    )
    # 강의실/교수 변경 시 즉시 data_source.py 에 persist.
    state.set_persist_callback(
        lambda: export_to_data_source(state, silent=True))
    root = tk.Tk()
    TimetableApp(root, state, n_solutions=n_solutions, m_top=m_top,
                 time_limit_sec=time_limit_sec,
                 per_solve_time_sec=per_solve_time_sec)
    root.mainloop()
