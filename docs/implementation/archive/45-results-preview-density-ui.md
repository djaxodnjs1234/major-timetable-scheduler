## Goal

Update the WPF results preview cards so their mini timetable is visual-only density, and kept solutions hide the keep button instead of showing kept text.

## Steps

1. Inspect results preview rendering and card state.
   - Verify: Identify `ResultsView.xaml`, `ResultsViewModel`, `SolutionCardViewModel`, and any converters used by the mini preview.

2. Change mini preview data and XAML.
   - Verify: Day labels are removed, lunch renders as one white horizontal row, timetable cells contain no text, and occupied cells use density from class counts.

3. Change keep-card state rendering.
   - Verify: After `IsKept` becomes true, the `보관` button is hidden and no green kept text is shown.

4. Run available validation.
   - Verify: XAML parses, targeted source checks pass, diagnostics/build run where available.

5. Archive this plan after verification.
   - Verify: This file is moved to `docs/implementation/archive/`.
