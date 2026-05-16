using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Pages;

public enum InputCategory { Professor, Course, Room, Solve }

public sealed partial class DataInputViewModel : PageViewModelBase
{
    private readonly WorkspaceService _workspace;
    private readonly SolverService _solver;
    private CancellationTokenSource? _cts;

    public override string Title => "정보 입력";

    public WorkspaceService Workspace => _workspace;

    [ObservableProperty]
    private InputCategory selectedCategory = InputCategory.Professor;

    [ObservableProperty]
    private object? selectedItem;

    [ObservableProperty]
    private string newId = "";

    [ObservableProperty]
    private string newName = "";

    public bool IsProfessorSelected => SelectedCategory == InputCategory.Professor;
    public bool IsCourseSelected => SelectedCategory == InputCategory.Course;
    public bool IsRoomSelected => SelectedCategory == InputCategory.Room;
    public bool IsSolveSelected => SelectedCategory == InputCategory.Solve;

    partial void OnSelectedCategoryChanged(InputCategory value)
    {
        OnPropertyChanged(nameof(IsProfessorSelected));
        OnPropertyChanged(nameof(IsCourseSelected));
        OnPropertyChanged(nameof(IsRoomSelected));
        OnPropertyChanged(nameof(IsSolveSelected));
        SelectedItem = null;
    }

    [RelayCommand]
    private void SelectProfessor() => SelectedCategory = InputCategory.Professor;

    [RelayCommand]
    private void SelectCourse() => SelectedCategory = InputCategory.Course;

    [RelayCommand]
    private void SelectRoom() => SelectedCategory = InputCategory.Room;

    [RelayCommand]
    private void SelectSolve() => SelectedCategory = InputCategory.Solve;

    [RelayCommand]
    private void AddNew()
    {
        if (string.IsNullOrWhiteSpace(NewId)) return;
        switch (SelectedCategory)
        {
            case InputCategory.Professor:
                if (_workspace.Professors.Any(p => p.Id == NewId)) return;
                _workspace.AddProfessor(new Professor { Id = NewId, Name = NewName });
                break;
            case InputCategory.Course:
                if (_workspace.Courses.Any(c => c.Id == NewId)) return;
                _workspace.AddCourse(new Course
                {
                    Id = NewId, Name = NewName,
                    Grade = 1, HoursPerWeek = 3,
                    BlockStructure = new List<int> { 2, 1 },
                    CourseType = "전선",
                });
                break;
            case InputCategory.Room:
                if (_workspace.Rooms.Any(r => r.Id == NewId)) return;
                _workspace.AddRoom(new Room { Id = NewId, Name = NewName });
                break;
        }
        NewId = "";
        NewName = "";
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        switch (SelectedItem)
        {
            case Professor p: _workspace.DeleteProfessor(p.Id); break;
            case Course c: _workspace.DeleteCourse(c.Id); break;
            case Room r: _workspace.DeleteRoom(r.Id); break;
        }
        SelectedItem = null;
    }

    [RelayCommand]
    private void SaveSelected()
    {
        switch (SelectedItem)
        {
            case Professor p: _workspace.UpdateProfessor(p); break;
            case Course c: _workspace.UpdateCourse(c); break;
            case Room r: _workspace.UpdateRoom(r); break;
        }
    }

    [RelayCommand]
    private void ImportXlsx(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        _workspace.ImportFromXlsx(path);
    }

    [ObservableProperty]
    private int totalSolutions = 3;

    [ObservableProperty]
    private int timeLimitSec = 120;

    [ObservableProperty]
    private bool useSc01 = true;

    [ObservableProperty]
    private bool useSc02 = true;

    [ObservableProperty]
    private bool useSc03 = true;

    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private int progressCurrent;

    [ObservableProperty]
    private int progressTotal;

    [ObservableProperty]
    private int uniqueSolutionsFound;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SolveCommand))]
    private bool isSolving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoToSelectionCommand))]
    private bool isSolveComplete;

    public ObservableCollection<RankedSolution> RankedResults { get; } = new();

    public event EventHandler<IReadOnlyList<RankedSolution>>? SolveCompleted;
    public event EventHandler? GoToSelectionRequested;

    [RelayCommand(CanExecute = nameof(CanGoToSelection))]
    private void GoToSelection() => GoToSelectionRequested?.Invoke(this, EventArgs.Empty);
    private bool CanGoToSelection() => IsSolveComplete;

    public DataInputViewModel(WorkspaceService workspace, SolverService solver)
    {
        _workspace = workspace;
        _solver = solver;
        _workspace.Changed += (_, _) => SolveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSolve))]
    private async Task SolveAsync()
    {
        IsSolving = true;
        IsSolveComplete = false;
        StatusMessage = "솔버 시작";
        ProgressCurrent = 0;
        ProgressTotal = TotalSolutions;
        UniqueSolutionsFound = 0;

        _cts = new CancellationTokenSource();
        var progress = new Progress<SolverProgress>(p =>
        {
            StatusMessage = $"[{p.Phase}] {p.Message}";
            ProgressCurrent = p.CurrentAttempt;
            ProgressTotal = p.TotalAttempts;
            UniqueSolutionsFound = p.UniqueSolutions;
        });

        try
        {
            var options = new DiverseSolverOptions
            {
                TotalSolutions = TotalSolutions,
                TimeLimitSec = TimeLimitSec,
                UseSc01 = UseSc01,
                UseSc02 = UseSc02,
                UseSc03 = UseSc03,
            };
            var result = await _solver.SolveAsync(_workspace, options, progress, _cts.Token);
            var ranked = _solver.Rank(_workspace, result, topM: 10);
            RankedResults.Clear();
            foreach (var r in ranked) RankedResults.Add(r);
            StatusMessage = $"완료: {result.Status}, {result.Solutions.Count}개 해, top {ranked.Count}";
            IsSolveComplete = true;
            SolveCompleted?.Invoke(this, ranked);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "취소됨";
        }
        catch (Exception ex)
        {
            StatusMessage = $"오류: {ex.Message}";
        }
        finally
        {
            IsSolving = false;
        }
    }

    private bool CanSolve() => !IsSolving && _workspace.Courses.Count > 0;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
