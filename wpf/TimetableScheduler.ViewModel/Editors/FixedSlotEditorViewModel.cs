using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Domain;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.ViewModel.Editors;

public sealed partial class BlockSlotEntry : ObservableObject
{
    public string BlockLabel { get; init; } = "";
    public int BlockSize { get; init; }

    public string[] DayOptions { get; } = { "월", "화", "수", "목", "금" };
    public int[] PeriodOptions { get; } = { 1, 2, 3, 4, 6, 7, 8, 9 };

    [ObservableProperty] private int selectedDayIndex;
    [ObservableProperty] private int selectedPeriod = 1;

    public IEnumerable<TimeSlot> ToSlots() =>
        Enumerable.Range(0, BlockSize).Select(k => new TimeSlot(SelectedDayIndex, SelectedPeriod + k));
}

public sealed class SectionSlotEditor
{
    public string SectionLabel { get; init; } = "";
    public List<BlockSlotEntry> BlockEntries { get; init; } = new();

    public List<TimeSlot> ToFixedSlots() =>
        BlockEntries.SelectMany(b => b.ToSlots()).ToList();
}

public sealed partial class FixedSlotEditorViewModel : ObservableObject
{
    [ObservableProperty] private bool isFixed;
    public string BlockSummary { get; init; } = "";
    public List<SectionSlotEditor> SectionEditors { get; init; } = new();

    public static FixedSlotEditorViewModel Build(CourseGroupItem item, bool isFixed)
    {
        var rep = item.Sections[0];
        var blocks = rep.BlockStructure.Count > 0
            ? rep.BlockStructure
            : new List<int> { rep.HoursPerWeek };

        var summary = $"블록 구성: {string.Join("+", blocks)}  · 분반 {item.Sections.Count}개" +
                      "  (강의실은 과목 또는 교수 설정을 따름)";

        var editors = new List<SectionSlotEditor>();
        for (int si = 0; si < item.Sections.Count; si++)
        {
            var sec = item.Sections[si];
            var existingStarts = SplitIntoBlockStarts(sec.FixedSlots, blocks);
            var entries = new List<BlockSlotEntry>();
            for (int bi = 0; bi < blocks.Count; bi++)
            {
                var entry = new BlockSlotEntry
                {
                    BlockLabel = $"블록{bi + 1} ({blocks[bi]}교시)",
                    BlockSize = blocks[bi],
                    SelectedDayIndex = existingStarts.Count > bi && existingStarts[bi].HasValue
                        ? existingStarts[bi]!.Value.Day
                        : (si + bi) % 5,
                    SelectedPeriod = existingStarts.Count > bi && existingStarts[bi].HasValue
                        ? existingStarts[bi]!.Value.Period
                        : 1,
                };
                entries.Add(entry);
            }
            var label = item.Sections.Count > 1
                ? $"{SectionLetter(si + 1)}분반"
                : "고정 시간";
            editors.Add(new SectionSlotEditor { SectionLabel = label, BlockEntries = entries });
        }

        return new FixedSlotEditorViewModel
        {
            IsFixed = isFixed,
            BlockSummary = summary,
            SectionEditors = editors,
        };
    }

    public void ApplyTo(CourseGroupItem item)
    {
        for (int si = 0; si < Math.Min(item.Sections.Count, SectionEditors.Count); si++)
            item.Sections[si].FixedSlots = SectionEditors[si].ToFixedSlots();
    }

    // Split flat FixedSlots list into (day, startPeriod) per block run
    private static List<TimeSlot?> SplitIntoBlockStarts(List<TimeSlot> slots, List<int> blocks)
    {
        if (slots.Count == 0) return new List<TimeSlot?>(new TimeSlot?[blocks.Count]);

        // Group consecutive same-day periods into runs
        var runs = new List<(int Day, int Start)>();
        int i = 0;
        while (i < slots.Count)
        {
            int d = slots[i].Day, start = slots[i].Period;
            int j = i + 1;
            while (j < slots.Count && slots[j].Day == d && slots[j].Period == slots[j - 1].Period + 1)
                j++;
            runs.Add((d, start));
            i = j;
        }

        var result = new List<TimeSlot?>();
        for (int bi = 0; bi < blocks.Count; bi++)
            result.Add(bi < runs.Count ? new TimeSlot(runs[bi].Day, runs[bi].Start) : (TimeSlot?)null);
        return result;
    }

    private static readonly string[] SectionLetters = { "A", "B", "C", "D", "E", "F" };
    private static string SectionLetter(int sec) =>
        sec >= 1 && sec <= SectionLetters.Length ? SectionLetters[sec - 1] : sec.ToString();
}
