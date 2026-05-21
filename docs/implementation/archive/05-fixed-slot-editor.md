# Step 5 — Fixed Slot Editor (Python-style per-section block picker)

## Goal

Replace the grid-style `TimeSlotPickerControl` with a per-section, per-block
day+start-period selector, matching the Python prototype's `FixedScheduleEditor`.

## UI Behaviour

- IsFixed unchecked → show "(고정 해제 상태 — 솔버가 자동 배정)" message
- IsFixed checked → for each section (A분반, B분반, ...):
    - For each block in BlockStructure (e.g. [2,1] → 블록1(2교시), 블록2(1교시)):
        - ComboBox: 요일 (월/화/수/목/금)
        - ComboBox: 시작교시 (1/2/3/4/6/7/8/9 — no lunch)
- Re-renders when IsFixed checkbox is toggled (handled in code-behind)
- On Save: expands (day, startPeriod, blockSize) → full `List<TimeSlot>`

## New Files

### `TimetableScheduler.ViewModel/Editors/FixedSlotEditorViewModel.cs`

- `BlockSlotEntry : ObservableObject` — one block row
  - `BlockLabel`, `BlockSize`, `SelectedDayIndex`, `SelectedPeriod`
  - `DayOptions = ["월","화","수","목","금"]`, `PeriodOptions = [1,2,3,4,6,7,8,9]`
  - `ToSlots() → IEnumerable<TimeSlot>`

- `SectionSlotEditor` — one section's blocks
  - `SectionLabel`, `BlockEntries : List<BlockSlotEntry>`
  - `ToFixedSlots() → List<TimeSlot>`

- `FixedSlotEditorViewModel : ObservableObject`
  - `IsFixed`, `BlockSummary`, `SectionEditors`
  - `static Build(CourseGroupItem, isFixed) → FixedSlotEditorViewModel`
  - `ApplyTo(CourseGroupItem)` — writes FixedSlots back to each section

### `TimetableScheduler.Wpf/Controls/FixedSlotEditorControl.xaml`

UserControl bound to `FixedSlotEditorViewModel`:
- Not-fixed message (visible when !IsFixed)
- BlockSummary header (visible when IsFixed)
- Outer ItemsControl → SectionEditors (GroupBox per section)
- Inner ItemsControl → BlockEntries (row per block with two ComboBoxes)

## Modified Files

### `DataInputView.xaml`

- Remove `GroupFixedSlotsPicker` (TimeSlotPickerControl)
- Add `<ctrl:FixedSlotEditorControl x:Name="FixedSlotEditor" />`
- Remove visibility gate — always shown (control handles its own state display)

### `DataInputView.xaml.cs`

- `OnCourseGroupExpanded`: replace slot picker init with
  `FixedSlotEditor.DataContext = FixedSlotEditorViewModel.Build(item, item.Sections[0].IsFixed)`
- Add `OnIsFixedCheckChanged` handler on the IsFixed CheckBox:
  rebuilds the editor when checkbox is toggled
- `OnCourseGroupSaveClick`: call `editor.ApplyTo(item)` before command

## Test Checklist

- [ ] IsFixed unchecked → shows "솔버가 자동 배정" message
- [ ] IsFixed checked → shows per-section block rows with ComboBoxes
- [ ] Toggling IsFixed checkbox updates editor immediately (no save required)
- [ ] Single-section (A분반): one GroupBox with correct block rows
- [ ] Multi-section (A·B분반): two GroupBoxes
- [ ] Save → FixedSlots persisted correctly (day/period expanded from block)
- [ ] Re-open expander: existing slots pre-fill the ComboBoxes
