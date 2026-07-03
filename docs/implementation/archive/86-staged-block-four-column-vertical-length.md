# Staged Block Four Column Vertical Length

## Goal

Correct the staged block cabinet layout so blocks flow across four columns, while block duration is represented vertically. For example, a two-period block should be one column wide and two period slots tall.

## Plan

1. Replace horizontal duration column-span behavior with a four-column flow.
   - Verify: staged blocks are added left to right and wrap to the next row after four items.
2. Restore duration-as-height behavior for staged block cards.
   - Verify: `RowSpan` controls card height, not width.
3. Restore staged drag grabbed-period calculation to vertical position.
   - Verify: dragging from the upper/lower part of a tall staged block aligns with the matching period offset.
4. Build and run available checks.
   - Verify: ViewModel and WPF code compile; report any existing test blockers.
