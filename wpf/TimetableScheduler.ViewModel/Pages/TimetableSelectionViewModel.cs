using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimetableScheduler.Data;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.ViewModel.Pages;

public sealed partial class TimetableSelectionViewModel : PageViewModelBase
{
    private readonly WorkspaceService _workspace;

    public override string Title => "시간표 선택";

    public ObservableCollection<SavedTimetableRecord> SavedTimetables => _workspace.SavedTimetables;

    public UnifiedTimetableViewModel Preview { get; } = new();

    [ObservableProperty]
    private SavedTimetableRecord? selectedTimetable;

    public bool HasSelection => SelectedTimetable != null;
    public bool HasNoSelection => !HasSelection;
    public bool HasNoTimetable => SavedTimetables.Count == 0;

    public TimetableSelectionViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
        _workspace.SavedTimetables.CollectionChanged += OnSavedTimetablesChanged;
    }

    private void OnSavedTimetablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasNoTimetable));
    }

    partial void OnSelectedTimetableChanged(SavedTimetableRecord? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        ExportXlsxCommand.NotifyCanExecuteChanged();

        if (value == null)
        {
            Preview.Render(Array.Empty<SolutionAssignment>(), Array.Empty<Domain.Course>());
            return;
        }

        var assignments = value.Assignments
            .Select(r => new SolutionAssignment(r.CourseId, r.Day, r.Period, r.RoomId))
            .ToList();
        Preview.Render(assignments, _workspace.Courses);
    }

    [RelayCommand]
    private void DeleteTimetable(SavedTimetableRecord record)
    {
        _workspace.DeleteSavedTimetable(record.Id);
        if (SelectedTimetable?.Id == record.Id)
            SelectedTimetable = SavedTimetables.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportXlsx(string path)
    {
        if (SelectedTimetable == null) return;
        FormattedTimetableExporter.Export(
            SelectedTimetable.Name,
            SelectedTimetable.Assignments,
            _workspace.Courses,
            _workspace.Professors,
            path);
    }

    private bool CanExport() => SelectedTimetable != null;
}
