using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.ViewModel.Pages;

public sealed partial class TimetableSelectionViewModel : PageViewModelBase
{
    private readonly WorkspaceService _workspace;

    public override string Title => "시간표 선택";

    public ObservableCollection<SavedTimetableRecord> SavedTimetables => _workspace.SavedTimetables;

    public UnifiedTimetableViewModel Preview { get; } = new();
    public ObservableCollection<NamedGridViewModel> GradeViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> RoomViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> ProfessorViews { get; } = new();

    [ObservableProperty]
    private SavedTimetableRecord? selectedTimetable;

    public bool HasSelection => SelectedTimetable != null;
    public bool HasNoSelection => !HasSelection;
    public bool HasNoTimetable => SavedTimetables.Count == 0;

    public event EventHandler? CreateNewRequested;
    public event EventHandler<SavedTimetableRecord>? EditRequested;

    [RelayCommand]
    private void CreateNew() => CreateNewRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Edit(SavedTimetableRecord record) => EditRequested?.Invoke(this, record);

    public TimetableSelectionViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
        _workspace.SavedTimetables.CollectionChanged += OnSavedTimetablesChanged;
        SelectedTimetable = SavedTimetables.FirstOrDefault();
    }

    private void OnSavedTimetablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasNoTimetable));

        if (SavedTimetables.Count == 0)
        {
            SelectedTimetable = null;
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Add
            && e.NewItems is { Count: > 0 }
            && e.NewItems[0] is SavedTimetableRecord added)
        {
            SelectedTimetable = added;
            return;
        }

        if (SelectedTimetable == null
            || !SavedTimetables.Any(t => t.Id == SelectedTimetable.Id))
        {
            SelectedTimetable = SavedTimetables.FirstOrDefault();
        }
    }

    partial void OnSelectedTimetableChanged(SavedTimetableRecord? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        ExportXlsxCommand.NotifyCanExecuteChanged();

        if (value == null)
        {
            RenderViews(
                Array.Empty<SolutionAssignment>(),
                Array.Empty<Course>(),
                Array.Empty<Professor>(),
                Array.Empty<Room>());
            return;
        }

        var assignments = value.Assignments
            .Select(r => new SolutionAssignment(r.CourseId, r.Day, r.Period, r.RoomId))
            .ToList();
        var snapshot = SavedTimetableSnapshotResolver.Resolve(value.SnapshotJson);
        RenderViews(
            assignments,
            snapshot.Courses,
            snapshot.Professors,
            snapshot.Rooms);
    }

    private void RenderViews(
        IReadOnlyList<SolutionAssignment> assignments,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms)
    {
        Preview.Render(assignments, courses, professors, rooms);

        GradeViews.Clear();
        foreach (var g in new[] { 1, 2, 3, 4 })
        {
            var vm = new TimetableGridViewModel();
            vm.Render(assignments, courses, (c, _) => c.Grade == g, professors, rooms);
            GradeViews.Add(new NamedGridViewModel(g.ToString(), $"{g}학년", vm));
        }

        RoomViews.Clear();
        foreach (var r in rooms)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(assignments, courses, (_, rid) => rid == r.Id, professors, rooms);
            RoomViews.Add(new NamedGridViewModel(r.Id, r.Name, vm));
        }

        ProfessorViews.Clear();
        foreach (var p in professors)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(assignments, courses, (c, _) => c.ProfessorId == p.Id, professors, rooms);
            ProfessorViews.Add(new NamedGridViewModel(p.Id, p.Name, vm));
        }
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
            _workspace.ExpandedCourses,
            _workspace.Professors,
            path);
    }

    private bool CanExport() => SelectedTimetable != null;
}
