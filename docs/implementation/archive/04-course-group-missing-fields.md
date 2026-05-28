# Step 4 — Restore Missing Fields in CourseGroupRowTemplate

## Problem

Step 3 replaced `CourseRowTemplate` (DataType `Course`) with `CourseGroupRowTemplate`
(DataType `CourseGroupItem`) but omitted three fields present in the original:

| Missing | Original behaviour |
|---|---|
| `IsFixed` checkbox | Toggle fixed-time mode per course |
| `FixedSlotsPicker` | Always visible; HC-13 slots for fixed courses |
| Section count +/- | Add / remove sections from a group |

## Changes

### DataInputView.xaml (CourseGroupRowTemplate)

1. Add `IsFixed` CheckBox bound to `{Binding IsFixed, Mode=TwoWay}` (DataContext = Sections[0])
2. Remove `IsFixedIndividual` visibility gate on fixed-slots panel → always show picker
3. Add section-count row: label showing `Sections.Count` + `+` / `-` buttons

### DataInputViewModel.cs

1. `SaveGroup`: propagate `IsFixed` to all sections (FixedSlots intentionally NOT copied — each section has different slots)
2. Add `AddSectionCommand(CourseGroupItem)`: appends next-numbered section copying shared fields
3. Add `RemoveSectionCommand(CourseGroupItem)`: deletes last section (no-op if only 1 remains)

### DataInputView.xaml.cs

1. `OnCourseGroupExpanded`: always bind `GroupFixedSlotsPicker` (drop `IsFixedIndividual` guard)
2. Add `OnAddSectionClick` / `OnRemoveSectionClick` handlers (reuse `FindCourseGroupItem`)

## Behaviour Simulation

| Action | Result |
|---|---|
| Check `IsFixed` on single-section group → Save | Marks fixed, row shows ★, slots picker was already open |
| Check `IsFixed` on multi-section group → Save | All sections get `IsFixed=true`; group splits into individual rows; slots configured per row |
| Uncheck `IsFixed` → Save | Clears fixed flag; if no sibling is fixed, sections re-merge into group row |
| `+` button | New section added (next letter), shared fields copied from Sections[0] |
| `-` button | Last section removed; no-op if only 1 section |

## Test Checklist

- [ ] IsFixed checkbox appears in group expander
- [ ] FixedSlotsPicker always visible when expander is open
- [ ] Checking IsFixed on single section → save → row shows ★
- [ ] Checking IsFixed on multi-section group → save → splits into individual rows
- [ ] `+` button adds section, list rebuilds
- [ ] `-` button removes last section; disabled behaviour when count=1
