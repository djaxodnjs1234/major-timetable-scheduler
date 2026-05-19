"""Domain layer — pure data + helpers, no IO/UI/CSP dependencies."""
from .models import (
    Course, Professor, Room, CrossGroup, RetakeScenario,
    derive_auto_retakes, base_id, expand_sections,
)

__all__ = [
    "Course", "Professor", "Room", "CrossGroup", "RetakeScenario",
    "derive_auto_retakes", "base_id", "expand_sections",
]
