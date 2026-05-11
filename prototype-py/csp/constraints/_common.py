"""Shared constants + helpers for HC functions."""
from domain.models import base_id as _base_id

LUNCH_PERIOD = 5  # 12:00~13:00
DAYS = 5          # Mon~Fri
PERIODS = range(1, 10)  # 1~9
VALID_PERIODS = [p for p in PERIODS if p != LUNCH_PERIOD]


def course_prof_ids(c):
    """주담당 + 팀티칭 교수 ID 집합 (빈 값 제외)."""
    ids = {c.professor_id} | set(getattr(c, "coteach_profs", []) or [])
    return {pid for pid in ids if pid}


# Re-export for callers that want the helper directly from constraints._common
__all__ = ["LUNCH_PERIOD", "DAYS", "PERIODS", "VALID_PERIODS",
           "course_prof_ids", "_base_id"]
