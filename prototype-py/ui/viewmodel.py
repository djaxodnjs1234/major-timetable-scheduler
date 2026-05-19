"""AppState — UI-framework-agnostic application state + commands.

이 모듈은 Tkinter 에 의존하지 않는다. 향후 WPF 이식 시 그대로 ViewModel
역할(또는 C# 포팅의 청사진)로 재사용 가능하다.

설계 원칙:
- 상태 변경은 모두 메서드(=command)로. 외부 코드는 필드를 직접 쓰지 않는다.
- 변경 후 ``_emit(kind)`` 로 구독자에게 알림. UI 는 이를 받아 다시 그린다.
- 솔버 호출은 ``commit_recalc`` 가 담당하고, 결과를 ``(success, status, msg, n)``
  튜플로 반환 — UI 는 메시지박스/상태바 등 표시 책임만 진다.
"""
import time

from domain.models import (
    CrossGroup, Professor, Room,
    derive_auto_retakes, base_id as _base_id, expand_sections as _expand_sections,
)


GRADES = (1, 2, 3, 4)


class AppState:
    """애플리케이션 상태 + 변경 명령. Tkinter 비의존."""

    def __init__(self, solutions, courses, professors, rooms,
                 crosses=None, retakes=None,
                 auto_retakes=True, scores=None):
        self.solutions = list(solutions or [])
        self.courses = list(_expand_sections(courses))
        self.professors = list(professors)
        self.rooms = list(rooms)
        self.crosses = list(crosses or [])
        # 수동 등록된 재수강 시나리오 (자동 토글과 합쳐짐)
        self.manual_retakes = list(retakes or [])
        self.auto_retakes = bool(auto_retakes)
        # 외부에서 받은 SC 점수 (재계산 후엔 무효화)
        self.scores = list(scores) if scores else None
        self.idx = 0
        # 통합 시간표 — 모든 학년 컬럼 강제 표시 여부.
        # False(default): 그 요일에 교과목 있는 학년만. True: 빈 학년도 1칸.
        self.expand_all_grades = False
        # 강의실/교수 변경 시 즉시 파일에 persist 하기 위한 콜백.
        # launcher 가 set_persist_callback 으로 주입.
        self._persist = None

        # 변경 버퍼 — 편집은 즉시 솔버를 돌리지 않고 dirty 카운트로 누적,
        # commit_recalc 에서 일괄 반영. 실패 시 rollback 으로 스냅샷 복원.
        self._dirty = 0
        self._clean_courses_backup = None
        self._clean_crosses_backup = None
        self._clean_auto_retakes_backup = None

        self._listeners = []

    # ------------------------------------------------------------------
    # 구독 (UI 가 상태 변경 시점을 받아 re-render)
    # ------------------------------------------------------------------
    def subscribe(self, callback):
        """callback(kind: str) 형태. kind 종류: idx, courses, crosses,
        auto_retakes, dirty, recalc, rollback."""
        self._listeners.append(callback)

    def _emit(self, kind):
        for cb in self._listeners:
            cb(kind)

    # ------------------------------------------------------------------
    # 파생 프로퍼티
    # ------------------------------------------------------------------
    @property
    def course_map(self):
        return {c.id: c for c in self.courses}

    @property
    def dirty_count(self):
        return self._dirty

    def base_groups(self):
        """base_id → sorted list of courses by section."""
        groups = {}
        for c in self.courses:
            groups.setdefault(_base_id(c.id), []).append(c)
        for k in groups:
            groups[k].sort(key=lambda c: c.section)
        return groups

    def section_count(self, cid):
        return len(self.base_groups().get(_base_id(cid), []))

    def current_assignment(self):
        if not self.solutions:
            return []
        return self.solutions[self.idx]

    def current_retakes(self):
        """솔버에 넘길 재수강 시나리오 = 토글 ON 시 자동 + 수동."""
        if self.auto_retakes:
            return (derive_auto_retakes(self.courses, GRADES)
                    + list(self.manual_retakes))
        return list(self.manual_retakes)

    # ------------------------------------------------------------------
    # 해 탐색
    # ------------------------------------------------------------------
    def set_idx(self, idx):
        if 0 <= idx < len(self.solutions) and idx != self.idx:
            self.idx = idx
            self._emit("idx")

    def prev_solution(self):
        if self.idx > 0:
            self.idx -= 1
            self._emit("idx")

    def next_solution(self):
        if self.idx < len(self.solutions) - 1:
            self.idx += 1
            self._emit("idx")

    # ------------------------------------------------------------------
    # 학년 펼침 토글 (통합 시간표 모든 학년 강제 표시)
    # ------------------------------------------------------------------
    def set_expand_all_grades(self, on):
        on = bool(on)
        if self.expand_all_grades == on:
            return
        self.expand_all_grades = on
        self._emit("expand_all_grades")

    # ------------------------------------------------------------------
    # Persist 콜백 (강의실/교수 변경 시 즉시 파일 저장)
    # ------------------------------------------------------------------
    def set_persist_callback(self, fn):
        self._persist = fn

    def _persist_now(self):
        if self._persist:
            try:
                self._persist()
            except Exception as e:
                print(f"[viewmodel] persist 실패: {e}")

    def _bump_dirty(self):
        """백업 없이 dirty 카운트만 증가 — 즉시 persist 되는 항목용."""
        self._dirty += 1
        self._emit("dirty")

    # ------------------------------------------------------------------
    # 교수 명령 (즉시 파일 저장)
    # ------------------------------------------------------------------
    def add_professor(self, name):
        name = (name or "").strip()
        if not name:
            return False, "이름이 비어있습니다."
        if any(p.id == name for p in self.professors):
            return False, f"이미 존재하는 교수: {name}"
        self.professors.append(Professor(id=name, name=name))
        self._bump_dirty()
        self._emit("professors")
        self._persist_now()
        return True, ""

    def update_professor(self, idx, allowed_rooms=None,
                         unavailable_slots=None):
        if not (0 <= idx < len(self.professors)):
            return
        p = self.professors[idx]
        if allowed_rooms is not None:
            p.allowed_rooms = list(allowed_rooms)
        if unavailable_slots is not None:
            p.unavailable_slots = list(unavailable_slots)
        self._bump_dirty()
        self._emit("professors")
        self._persist_now()

    def delete_professor(self, idx):
        if not (0 <= idx < len(self.professors)):
            return False, []
        pid = self.professors[idx].id
        used_by = [c.id for c in self.courses
                   if c.professor_id == pid
                   or pid in (c.coteach_profs or [])]
        if used_by:
            return False, used_by
        del self.professors[idx]
        self._bump_dirty()
        self._emit("professors")
        self._persist_now()
        return True, []

    # ------------------------------------------------------------------
    # 강의실 명령 (즉시 파일 저장)
    # ------------------------------------------------------------------
    def add_room(self, room_id):
        room_id = (room_id or "").strip()
        if not room_id:
            return False, "강의실 ID가 비어있습니다."
        if any(r.id == room_id for r in self.rooms):
            return False, f"이미 존재하는 강의실: {room_id}"
        self.rooms.append(Room(id=room_id, name=room_id))
        self._bump_dirty()
        self._emit("rooms")
        self._persist_now()
        return True, ""

    def delete_room(self, idx):
        if not (0 <= idx < len(self.rooms)):
            return False, []
        rid = self.rooms[idx].id
        used_by = []
        for c in self.courses:
            if rid in (c.fixed_rooms or []):
                used_by.append(c.id)
        for prof in self.professors:
            if rid in (getattr(prof, "allowed_rooms", []) or []):
                used_by.append(f"교수:{prof.id}")
        if used_by:
            return False, used_by
        del self.rooms[idx]
        self._bump_dirty()
        self._emit("rooms")
        self._persist_now()
        return True, []

    # ------------------------------------------------------------------
    # 과목 변경 명령
    # ------------------------------------------------------------------
    def replace_course_group(self, old_ids, new_courses):
        pre = list(self.courses)
        pre_crosses = self._copy_crosses()
        old_ids = set(old_ids)
        self.courses = [c for c in self.courses if c.id not in old_ids]
        self.courses.extend(new_courses)
        self._prune_crosses()
        self._mark_dirty(pre, pre_crosses)
        self._emit("courses")

    def delete_courses(self, ids):
        pre = list(self.courses)
        pre_crosses = self._copy_crosses()
        ids = set(ids)
        self.courses = [c for c in self.courses if c.id not in ids]
        self._prune_crosses()
        self._mark_dirty(pre, pre_crosses)
        self._emit("courses")

    def next_course_id(self):
        n = 1
        existing = {c.id for c in self.courses}
        while True:
            base = f"USR{n:03d}"
            if not any(cid == base or cid.startswith(base + "-")
                       for cid in existing):
                return base
            n += 1

    # ------------------------------------------------------------------
    # Cross 명령
    # ------------------------------------------------------------------
    def next_cross_id(self):
        n = 1
        ids = {g.id for g in self.crosses}
        while f"X{n:03d}" in ids:
            n += 1
        return f"X{n:03d}"

    def add_cross(self, base_ids):
        pre_crosses = self._copy_crosses()
        self.crosses.append(CrossGroup(
            id=self.next_cross_id(), base_ids=list(base_ids)))
        self._mark_dirty(list(self.courses), pre_crosses)
        self._emit("crosses")

    def delete_cross(self, idx):
        if 0 <= idx < len(self.crosses):
            pre_crosses = self._copy_crosses()
            del self.crosses[idx]
            self._mark_dirty(list(self.courses), pre_crosses)
            self._emit("crosses")

    def _copy_crosses(self):
        return [CrossGroup(id=g.id, base_ids=list(g.base_ids))
                for g in self.crosses]

    def _prune_crosses(self):
        existing_bids = {_base_id(c.id) for c in self.courses}
        new_list = []
        for g in self.crosses:
            bids = [b for b in g.base_ids if b in existing_bids]
            if len(bids) >= 2:
                new_list.append(CrossGroup(id=g.id, base_ids=bids))
        self.crosses = new_list

    # ------------------------------------------------------------------
    # 자동 재수강 토글
    # ------------------------------------------------------------------
    def set_auto_retakes(self, enabled):
        if self.auto_retakes == bool(enabled):
            return
        # 첫 변경이면 OLD 값 백업해야 rollback 시 토글 복원 가능.
        if self._dirty == 0 and self._clean_courses_backup is None:
            self._clean_courses_backup = list(self.courses)
            self._clean_crosses_backup = self._copy_crosses()
            self._clean_auto_retakes_backup = self.auto_retakes
        self.auto_retakes = bool(enabled)
        self._dirty += 1
        self._emit("auto_retakes")

    # ------------------------------------------------------------------
    # Dirty 버퍼 + 재계산
    # ------------------------------------------------------------------
    def _mark_dirty(self, pre_courses, pre_crosses):
        if self._dirty == 0 and self._clean_courses_backup is None:
            self._clean_courses_backup = pre_courses
            self._clean_crosses_backup = pre_crosses
            self._clean_auto_retakes_backup = self.auto_retakes
        self._dirty += 1
        self._emit("dirty")

    def commit_recalc(self, time_limit_sec=60, per_solve_time_sec=15,
                      total_solutions=20, top_m=20):
        """솔버 재실행. (success, status, message, applied_count) 반환.

        실패 시 자동 rollback. 성공 시 dirty 리셋 + solutions/scores 갱신.
        초기 실행(main.py)과 동일하게 build_and_solve_diverse → rank_solutions
        파이프라인을 거친다.
        """
        from csp import build_and_solve_diverse
        from scoring import rank_solutions, SC_WEIGHTS
        if self._dirty == 0:
            return True, "OK", "변경 없음", 0
        try:
            status, solutions = build_and_solve_diverse(
                self.courses, self.professors, self.rooms,
                total_solutions=total_solutions,
                time_limit_sec=time_limit_sec,
                per_solve_time_sec=per_solve_time_sec,
                crosses=self.crosses,
                retakes=self.current_retakes(),
                base_seed=int(time.time()) & 0xFFFF,
                sc01_weight=SC_WEIGHTS.get("SC01", 0),
                sc02_weight=SC_WEIGHTS.get("SC02", 0),
                sc03_weight=SC_WEIGHTS.get("SC03", 0),
            )
        except Exception as e:
            self.rollback()
            return False, "ERROR", f"솔버 실행 실패: {e}", 0
        if not solutions:
            applied = self._dirty
            self.rollback()
            return (False, status,
                    f"해를 찾지 못했습니다 (status={status}). "
                    f"변경 {applied}건을 모두 취소합니다.",
                    0)
        ranked = rank_solutions(solutions, self.courses, self.professors,
                                top_m=top_m)
        applied = self._dirty
        self.solutions = [a for a, _ in ranked]
        self.scores = [s for _, s in ranked]
        self.idx = 0
        self._dirty = 0
        self._clean_courses_backup = None
        self._clean_crosses_backup = None
        self._clean_auto_retakes_backup = None
        self._emit("recalc")
        return True, status, "성공", applied

    def rollback(self):
        if self._clean_courses_backup is not None:
            self.courses = self._clean_courses_backup
        if self._clean_crosses_backup is not None:
            self.crosses = self._clean_crosses_backup
        if self._clean_auto_retakes_backup is not None:
            self.auto_retakes = self._clean_auto_retakes_backup
        self._clean_courses_backup = None
        self._clean_crosses_backup = None
        self._clean_auto_retakes_backup = None
        self._dirty = 0
        self._emit("rollback")
