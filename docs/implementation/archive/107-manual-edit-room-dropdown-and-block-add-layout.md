# Manual Edit Room Dropdown and Block Add Layout

## Goal

Adjust the manual edit room dropdown display and staged block add form layout.

## Assumptions

- Room dropdown rows should show room name, lab/classroom type, and capacity in that order.
- Tab-separated display text is acceptable for text alignment in dropdown rows.
- New staged blocks should use the selected grade and class-hour length at creation time.
- The add form should use two columns except course name, which spans the full width.

## Steps

1. Update room option display text to room / lab status / capacity.
   - Verify: dropdown display binding uses the updated text.
2. Update simple staged block creation to use selected grade and row span.
   - Verify: created staged block has selected grade and hour count.
3. Update XAML layout for block add form: course name full width, professor/grade and hours/room in two columns.
   - Verify: WPF project builds.
4. Update focused ViewModel test for simple block creation.
   - Verify: manual edit focused tests pass.

