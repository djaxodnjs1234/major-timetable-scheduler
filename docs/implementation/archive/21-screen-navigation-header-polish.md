# Screen navigation header polish

## Goal

Polish the WPF page headers and solution preview strip to match the latest UI request.

## Steps

1. Add page-level back navigation commands/events in the ViewModel layer.
   - Verify: `MainWindowViewModel` can route each page's back request without changing existing forward flows.
2. Update the four WPF page headers.
   - Verify: each header shows only the numeric badge (`1`, `2`, `3`, `4`) and page title; pages with a previous screen show `뒤로가기` on the top-right.
3. Update the solution preview card strip.
   - Verify: each card displays `해 N` and the score on the same horizontal row.
4. Remove the workflow label in the information input sidebar.
   - Verify: the `수동 편집으로 →` button remains on the left without the `워크플로우` text above it.
5. Build and run the WPF app for a manual UI smoke test.
   - Verify: build succeeds and the visible UI surface reflects the requested layout changes.
