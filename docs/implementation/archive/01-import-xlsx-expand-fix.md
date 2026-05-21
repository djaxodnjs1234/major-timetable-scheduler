# Step 1 — Fix ImportFromXlsx Section Expansion Inconsistency

## Problem

After importing an xlsx file via `WorkspaceService.ImportFromXlsx`, the in-memory
`Courses` collection holds **unexpanded** courses (e.g., `GA1004` with `Section=2`).

`Persist()` then calls `SaveAll(Snapshot())`, where `Snapshot()` runs
`DomainHelpers.ExpandSections()` before saving — so the DB ends up with **expanded**
rows (`GA1004-01 Section=1`, `GA1004-02 Section=2`).

On the next app startup, `Reload()` reads the expanded rows back into `Courses`,
producing **2 rows** where the user saw only **1 row** during the previous session.

```
xlsx import   → Courses = [GA1004 (Section=2)]          ← 1 row
Persist()     → DB    = [GA1004-01, GA1004-02]          ← expanded
App restart   → Courses = [GA1004-01, GA1004-02]        ← 2 rows  ← BUG
```

## Fix

**File**: `wpf/TimetableScheduler.ViewModel/WorkspaceService.cs`  
**Method**: `ImportFromXlsx`

Expand sections immediately when loading from xlsx, so `Courses` always holds the
same expanded representation that the DB stores.

```csharp
// Before (bug)
foreach (var c in loaded.Courses) Courses.Add(c);

// After (fix)
foreach (var c in DomainHelpers.ExpandSections(loaded.Courses)) Courses.Add(c);
```

`DomainHelpers.ExpandSections` is idempotent — already-expanded courses pass through
unchanged — so `Persist()` and `Snapshot()` remain correct with no further changes.

## Invariant After Fix

`WorkspaceService.Courses` always holds **individually expanded sections**.  
Both import paths (xlsx → `ImportFromXlsx` and DB → `Reload`) produce the same
representation.

## Test Checklist

- [ ] Import xlsx → count of rows in course list equals total section count (not
      logical course count)
- [ ] Close and reopen app → same row count as before close
- [ ] Import xlsx twice in a row → no duplicate rows
- [ ] Auto-import on first startup (empty DB) → same row count after restart
- [ ] Edit a course field and save → row count unchanged after restart
