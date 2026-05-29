# Step 3 — DataInputView XAML: Group Header + Per-Section Fixed Rows

## Goal

Replace the flat `ItemsSource="{Binding Workspace.Courses}"` course list with
`ItemsSource="{Binding CourseGroups}"`, using two distinct row templates:

1. **GroupRowTemplate** — for non-fixed groups (`IsFixedIndividual=false`)  
   Header: `BaseId  Name  Grade  Hours  (A·B분반)`  
   Expander body: shared fields only (Name, Grade, Hours, CourseType, Professor,
   FixedRooms, CoteachProfs, section count). Saving applies to **all sections**.

2. **FixedRowTemplate** — for individual fixed sections (`IsFixedIndividual=true`)  
   Header: `CourseId  Name  Grade  ★`  
   Expander body: current single-course form + fixed time slot editor.

## XAML Changes

### ItemsControl binding change
```xml
<!-- Before -->
<ItemsControl ItemsSource="{Binding Workspace.Courses}"
              ItemTemplate="{StaticResource CourseRowTemplate}" ... />

<!-- After -->
<ItemsControl ItemsSource="{Binding CourseGroups}"
              ItemTemplate="{StaticResource CourseGroupRowTemplate}" ... />
```

### CourseGroupRowTemplate (DataType: CourseGroupItem)

Header columns: `BaseId` | `Sections[0].Name` | `Grade학년` | `Hours h` | section letters

Expander body (when `IsFixedIndividual=false`):
- Editable fields: Name, Grade, HoursPerWeek, CourseType, ProfessorId, FixedRooms,
  CoteachProfs, section count (SpinBox — changing adds/removes sections)
- Save button → `SaveGroupCommand` with the `CourseGroupItem` as parameter
- Delete button → `DeleteGroupCommand`

Expander body (when `IsFixedIndividual=true`):
- Same as current `CourseRowTemplate` expander, plus `TimeSlotPickerControl` for
  fixed slots
- Save button → `SaveSectionCommand`
- Delete button → `DeleteSectionCommand`

A `DataTemplateSelector` chooses between the two sub-templates based on
`IsFixedIndividual`.

## Commands Added to DataInputViewModel

| Command | Behaviour |
|---|---|
| `SaveGroupCommand(CourseGroupItem)` | Apply shared fields to all sections via `UpdateCourse` loop |
| `DeleteGroupCommand(CourseGroupItem)` | Delete all sections (confirm dialog) |
| `SaveSectionCommand(CourseGroupItem)` | `UpdateCourse` on the single section |
| `DeleteSectionCommand(CourseGroupItem)` | `DeleteCourse` on the single section |

The existing `SaveSelectedCommand` / `DeleteSelectedCommand` (which operated on a
raw `Course`) can be removed once all callers are migrated.

## Test Checklist

- [ ] Non-fixed group row shows correct header (BaseId, Name, section letters)
- [ ] Fixed individual row header shows ★ and CourseId
- [ ] SaveGroup updates all sections (verify via WorkspaceService.Courses count)
- [ ] DeleteGroup removes all sections
- [ ] Section count spinbox: increase → new section added; decrease → last section removed
- [ ] Fixed row save persists fixed slots
