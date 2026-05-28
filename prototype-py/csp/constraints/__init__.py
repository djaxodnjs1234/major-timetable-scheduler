"""CSP constraint functions, organized by concern.

Re-export everything used by ``csp.solver`` so registration stays in one
place there. ``_common`` keeps the shared constants/helpers.
"""
from ._common import (
    DAYS, VALID_PERIODS, PERIODS, LUNCH_PERIOD,
    course_prof_ids,
    _base_id,  # legacy alias
)
from .basic import (
    add_hc01_room_single,
    add_hc02_prof_single,
    add_hc03_prof_unavailable,
    add_hc04_hours,
    add_hc08_section_no_overlap,
    add_hc11_grade_no_overlap,
    add_hc12_lunch,
    add_hc13_fixed,
)
from .blocks import (
    add_hc06_block_split,
    add_hc14_fixed_rooms,
    add_hc15_section_backtoback,
    add_hc18_block_day_gap,
    add_hc19_len2_start_periods,
    add_hc20_block_days_distinct,
    add_hc21_prof_room_consistent,
)
from .grouping import (
    add_hc16_cross,
    add_hc17_retake,
)

__all__ = [
    "DAYS", "VALID_PERIODS", "PERIODS", "LUNCH_PERIOD",
    "course_prof_ids", "_base_id",
    "add_hc01_room_single", "add_hc02_prof_single",
    "add_hc03_prof_unavailable", "add_hc04_hours",
    "add_hc06_block_split",
    "add_hc08_section_no_overlap", "add_hc11_grade_no_overlap",
    "add_hc12_lunch", "add_hc13_fixed",
    "add_hc14_fixed_rooms", "add_hc15_section_backtoback",
    "add_hc16_cross", "add_hc17_retake",
    "add_hc18_block_day_gap", "add_hc19_len2_start_periods",
    "add_hc20_block_days_distinct",
    "add_hc21_prof_room_consistent",
]
