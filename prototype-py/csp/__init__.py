"""CSP layer — model construction + solving."""
from .objectives import (
    PENALIZED_SLOTS,
    sc01_penalty_term, sc02_penalty_term, sc03_penalty_term,
    SC01_SLACK_ABS, SC02_SLACK_ABS, SC03_SLACK_ABS,
    SC02_DAY_THRESHOLD, SC02_MIN_COURSES,
    SC02_EXCLUDE_FIXED, SC02_EXCLUDE_COTEACH,
)
from .solver import build_and_solve_diverse

__all__ = [
    "build_and_solve_diverse",
    "PENALIZED_SLOTS",
    "sc01_penalty_term", "sc02_penalty_term", "sc03_penalty_term",
    "SC01_SLACK_ABS", "SC02_SLACK_ABS", "SC03_SLACK_ABS",
    "SC02_DAY_THRESHOLD", "SC02_MIN_COURSES",
    "SC02_EXCLUDE_FIXED", "SC02_EXCLUDE_COTEACH",
]
