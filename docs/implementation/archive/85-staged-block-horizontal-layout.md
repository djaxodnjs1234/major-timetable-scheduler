# Staged Block Horizontal Layout

## Goal

Arrange manual edit staged blocks horizontally instead of as a vertical list. A block should use horizontal width based on its time length, each row should contain at most four period slots, and additional rows should scroll vertically.

## Plan

1. Replace the staged block vertical list with a row container suitable for programmatic four-slot rows.
   - Verify: the scroll area still supports vertical scrolling and the empty-state binding is unchanged.
2. Build staged block rows in code-behind with four equal columns per row.
   - Verify: each staged card uses `RowSpan` as its column span, capped to the four-slot row width.
3. Keep existing selection, drag/drop, and drag preview handlers attached to each card.
   - Verify: staged cards still select, deselect, drag, and show the cursor-following preview.
4. Build and run available checks.
   - Verify: WPF compiles through an unlocked output folder if the app is currently running; report any pre-existing test blockers.
