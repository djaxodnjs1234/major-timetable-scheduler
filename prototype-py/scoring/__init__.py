"""Scoring layer — Soft Constraints."""
from .soft_constraints import (
    score_solution, rank_solutions,
    SC_WEIGHTS, SC_KEYS, SC_PENALTY_POWER, PENALIZED_SLOTS,
)

__all__ = [
    "score_solution", "rank_solutions",
    "SC_WEIGHTS", "SC_KEYS", "SC_PENALTY_POWER", "PENALIZED_SLOTS",
]
