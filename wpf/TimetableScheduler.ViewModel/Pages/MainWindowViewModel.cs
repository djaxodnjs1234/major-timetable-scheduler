using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Data;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Pages;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly TimetableSelectionViewModel _selection;
    private readonly DataInputViewModel _input;
    private readonly ResultsViewModel _results;
    private readonly ManualEditViewModel _manual;
    private PageViewModelBase? _manualBackTarget;
    private PageViewModelBase? _inputBackTarget;
    private string? _manualBackBaselineKey;

    [ObservableProperty]
    private PageViewModelBase currentPage;

    public TimetableSelectionViewModel Selection => _selection;
    public DataInputViewModel Input => _input;
    public ResultsViewModel Results => _results;
    public ManualEditViewModel Manual => _manual;

    public event EventHandler<UnsavedBackNavigationEventArgs>? BackConfirmationRequested;

    public MainWindowViewModel(
        TimetableSelectionViewModel selection,
        DataInputViewModel input,
        ResultsViewModel results,
        ManualEditViewModel manual)
    {
        _selection = selection;
        _input = input;
        _results = results;
        _manual = manual;

        _input.SolveCompleted += (_, ranked) =>
        {
            _results.SetSolutions(
                ranked, _input.CurrentSnapshot());
        };
        _input.GoToSelectionRequested += (_, _) => NavigateTo(_results);
        _input.BackRequested += (_, _) =>
        {
            var target = _inputBackTarget ?? _selection;
            _inputBackTarget = null;
            NavigateTo(target);
        };
        _input.GoToManualRequested += (_, _) =>
        {
            var handoff = _input.BuildEditHandoff();
            if (handoff != null)
            {
                _manual.LoadFromSnapshot(
                    _input.CurrentSnapshot(),
                    handoff.Solution,
                    handoff.SaveName,
                    handoff.ManualCrossLinks,
                    handoff.SavedTimetableId);
                CaptureManualBackBaseline();
            }
            _inputBackTarget = null;
            _manualBackTarget = _input;
            NavigateTo(_manual);
        };

        _selection.CreateNewRequested += (_, _) =>
        {
            _input.LoadForNewTimetable();
            _inputBackTarget = null;
            NavigateTo(_input);
        };
        _selection.EditRequested += (_, record) =>
        {
            _input.LoadForExistingTimetable(record);
            _manual.LoadFromSavedTimetable(record);
            CaptureManualBackBaseline();
            _inputBackTarget = null;
            _manualBackTarget = _selection;
            NavigateTo(_manual);
        };

        _results.EditSelectedRequested += (_, _) =>
        {
            if (_results.SelectedSolution != null)
            {
                _manual.LoadFromSnapshot(
                    _results.CurrentSnapshot,
                    _results.SelectedSolution,
                    "");
                CaptureManualBackBaseline();
            }
            _manualBackTarget = _results;
            NavigateTo(_manual);
        };
        _results.BackRequested += (_, _) => NavigateTo(_input);

        _manual.SavedRequested += (_, record) =>
        {
            _manualBackBaselineKey = null;
            _input.LoadForExistingTimetable(record);
            NavigateTo(_selection);
        };
        _manual.BackRequested += (_, _) =>
        {
            if (HasManualBackChanges() && !ConfirmBackDiscard("\uC218\uB3D9\uD3B8\uC9D1\uC5D0\uC11C \uC800\uC7A5\uD558\uC9C0 \uC54A\uC740 \uBCC0\uACBD"))
                return;
            NavigateTo(_manualBackTarget ?? _results);
        };
        _manual.ConstraintEditRequested += (_, _) =>
        {
            _input.LoadForManualConstraintEdit(_manual.BuildConstraintEditContext());
            _inputBackTarget = _manual;
            NavigateTo(_input);
        };

        currentPage = _selection;
    }

    private bool ConfirmBackDiscard(string label)
    {
        var args = new UnsavedBackNavigationEventArgs(new[] { label });
        BackConfirmationRequested?.Invoke(this, args);
        return args.ShouldContinue;
    }

    private void CaptureManualBackBaseline()
    {
        try
        {
            _manualBackBaselineKey = ManualStateKey(_manual);
        }
        catch
        {
            _manualBackBaselineKey = null;
        }
    }

    private bool HasManualBackChanges()
    {
        if (_manualBackBaselineKey == null)
            return false;

        try
        {
            return !string.Equals(_manualBackBaselineKey, ManualStateKey(_manual), StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    private static string ManualStateKey(ManualEditViewModel manual)
    {
        var handoff = manual.BuildConstraintEditContext().Handoff;
        var normalized = new
        {
            Assignments = handoff.Solution.Assignment
                .OrderBy(assignment => assignment.CourseId, StringComparer.Ordinal)
                .ThenBy(assignment => assignment.Day)
                .ThenBy(assignment => assignment.Period)
                .ThenBy(assignment => assignment.RoomId, StringComparer.Ordinal)
                .ThenBy(assignment => assignment.AssignmentId, StringComparer.Ordinal)
                .Select(assignment => new
                {
                    assignment.CourseId,
                    assignment.Day,
                    assignment.Period,
                    assignment.RoomId,
                    assignment.AssignmentId,
                }),
            ManualCrossLinks = handoff.ManualCrossLinks
                .OrderBy(link => link.SourceCourseId, StringComparer.Ordinal)
                .ThenBy(link => link.TargetCourseId, StringComparer.Ordinal)
                .ThenBy(link => link.SourceDay)
                .ThenBy(link => link.SourcePeriod)
                .ThenBy(link => link.TargetDay)
                .ThenBy(link => link.TargetPeriod)
                .ThenBy(link => link.SourceRoomId, StringComparer.Ordinal)
                .ThenBy(link => link.TargetRoomId, StringComparer.Ordinal)
                .ThenBy(link => link.SourceAssignmentId, StringComparer.Ordinal)
                .ThenBy(link => link.TargetAssignmentId, StringComparer.Ordinal)
                .Select(link => new
                {
                    link.SourceCourseId,
                    link.SourceGrade,
                    link.SourceSection,
                    link.SourceDay,
                    link.SourcePeriod,
                    link.SourceRoomId,
                    link.TargetCourseId,
                    link.TargetGrade,
                    link.TargetSection,
                    link.TargetDay,
                    link.TargetPeriod,
                    link.TargetRoomId,
                    link.PolicyType,
                    link.SourceAssignmentId,
                    link.TargetAssignmentId,
                }),
        };
        return JsonSerializer.Serialize(normalized);
    }

    private void NavigateTo(PageViewModelBase target)
    {
        CurrentPage?.OnNavigatedFrom();
        CurrentPage = target;
        CurrentPage.OnNavigatedTo();
    }
}
