using CommunityToolkit.Mvvm.ComponentModel;

namespace TimetableScheduler.ViewModel.Editors;

public sealed partial class CheckListItem : ObservableObject
{
    public string Id { get; }
    public string Display { get; }

    [ObservableProperty]
    private bool isChecked;

    public CheckListItem(string id, string display, bool initial)
    {
        Id = id;
        Display = display;
        isChecked = initial;
    }
}

public static class CheckListBinder
{
    /// <summary>
    /// Build a list of CheckListItem from <paramref name="source"/> with the IsChecked state derived from <paramref name="selected"/>.
    /// Each item's IsChecked change syncs back to <paramref name="selected"/> in-place.
    /// </summary>
    public static List<CheckListItem> Bind<T>(
        IReadOnlyList<T> source,
        Func<T, string> idSelector,
        Func<T, string> displaySelector,
        IList<string> selected)
    {
        var items = new List<CheckListItem>();
        foreach (var src in source)
        {
            var id = idSelector(src);
            var item = new CheckListItem(id, displaySelector(src), selected.Contains(id));
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(CheckListItem.IsChecked)) return;
                if (item.IsChecked)
                {
                    if (!selected.Contains(item.Id)) selected.Add(item.Id);
                }
                else
                {
                    selected.Remove(item.Id);
                }
            };
            items.Add(item);
        }
        return items;
    }
}
