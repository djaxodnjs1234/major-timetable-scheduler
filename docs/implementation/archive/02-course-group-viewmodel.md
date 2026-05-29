# Step 2 — CourseGroupItem Model + CourseGroups Property in DataInputViewModel

## Goal

Replace the flat `Workspace.Courses` binding in the DataInput view with a grouped
representation that mirrors the Python prototype's sidebar behaviour:

- **Non-fixed courses** (`IsFixed=false`): one row per logical course (BaseId),
  showing all sections as a group.  
  _Example_: `GA1004-01` and `GA1004-02` → one row `GA1004  자료구조  (A·B)`
- **Fixed courses** (`IsFixed=true`): one row per individual section, because each
  section has its own fixed time slots.  
  _Example_: `GA1005-01 ★`, `GA1005-02 ★` — two separate rows

## New Type: CourseGroupItem

Added to `DataInputViewModel.cs` (or a small companion file in the same namespace).

```csharp
public sealed class CourseGroupItem
{
    public string BaseId { get; init; }          // e.g. "GA1004"
    public string DisplayLabel { get; init; }    // e.g. "GA1004  자료구조  2학년  4h  (A·B)"
    public List<Course> Sections { get; init; }  // 1 item if fixed, N items if grouped
    public bool IsFixedGroup => Sections.Count == 1 && Sections[0].IsFixed;
}
```

## DataInputViewModel Changes

1. Add `public ObservableCollection<CourseGroupItem> CourseGroups { get; } = new();`
2. Subscribe to `_workspace.Changed` → call `RebuildCourseGroups()`
3. `RebuildCourseGroups()` logic:
   - Group `Workspace.Courses` by `DomainHelpers.BaseId(c.Id)`
   - For each group:
     - If **any section** has `IsFixed=true` → emit one `CourseGroupItem` per section
       (individual rows, `Sections.Count == 1`)
     - Otherwise → emit one `CourseGroupItem` for the whole group
       (`Sections` = all sections sorted by `Section` number)
   - Sort result by `(Grade, CourseType, BaseId)`
4. Call `RebuildCourseGroups()` in the constructor after the workspace subscription.

## Display Label Format

| Scenario | Label |
|---|---|
| Non-fixed, 1 section | `GA1004  자료구조  2학년  4h` |
| Non-fixed, N sections | `GA1004  자료구조  2학년  4h  (A·B)` |
| Fixed individual | `GA1004-01  자료구조  2학년  ★` |

Section letters: A=1, B=2, C=3, D=4, E=5, F=6 (matching Python prototype).

## Test Checklist

- [ ] 2-section non-fixed course → `CourseGroups` has 1 item, `Sections.Count == 2`
- [ ] 1-section non-fixed course → `CourseGroups` has 1 item, `Sections.Count == 1`
- [ ] Fixed course with 2 sections → `CourseGroups` has 2 items, each `Sections.Count == 1`
- [ ] Mixed (some fixed, some not) → correct split
- [ ] `CourseGroups` updates when `Workspace.Changed` fires
