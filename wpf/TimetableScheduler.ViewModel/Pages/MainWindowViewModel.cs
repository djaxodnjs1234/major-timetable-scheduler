using CommunityToolkit.Mvvm.ComponentModel;

namespace TimetableScheduler.ViewModel.Pages;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly TimetableSelectionViewModel _selection;
    private readonly DataInputViewModel _input;
    private readonly ResultsViewModel _results;
    private readonly ManualEditViewModel _manual;
    private PageViewModelBase? _manualBackTarget;

    [ObservableProperty]
    private PageViewModelBase currentPage;

    public TimetableSelectionViewModel Selection => _selection;
    public DataInputViewModel Input => _input;
    public ResultsViewModel Results => _results;
    public ManualEditViewModel Manual => _manual;

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
                ranked, _input.IsSessionMode ? _input.CurrentSnapshot() : null);
        };
        _input.GoToSelectionRequested += (_, _) => NavigateTo(_results);
        _input.BackRequested += (_, _) => NavigateTo(_selection);
        _input.GoToManualRequested += (_, _) =>
        {
            var handoff = _input.BuildEditHandoff();
            if (handoff != null)
            {
                _manual.LoadFromSnapshot(
                    _input.CurrentSnapshot(),
                    handoff.Solution,
                    _input.EditBaseName,
                    handoff.ManualCrossLinks,
                    handoff.SavedTimetableId);
            }
            _manualBackTarget = _input;
            NavigateTo(_manual);
        };

        _selection.CreateNewRequested += (_, _) =>
        {
            _input.LoadForNewTimetable();
            NavigateTo(_input);
        };
        _selection.EditRequested += (_, record) =>
        {
            _input.LoadForExistingTimetable(record);
            NavigateTo(_input);
        };

        _results.EditSelectedRequested += (_, _) =>
        {
            if (_results.SelectedSolution != null)
                _manual.LoadFromSnapshot(
                    _results.CurrentSnapshot,
                    _results.SelectedSolution,
                    "");
            _manualBackTarget = _results;
            NavigateTo(_manual);
        };
        _results.BackRequested += (_, _) => NavigateTo(_input);

        _manual.SavedRequested += (_, _) => NavigateTo(_selection);
        _manual.BackRequested += (_, _) => NavigateTo(_manualBackTarget ?? _results);

        currentPage = _selection;
    }

    private void NavigateTo(PageViewModelBase target)
    {
        CurrentPage?.OnNavigatedFrom();
        CurrentPage = target;
        CurrentPage.OnNavigatedTo();
    }
}
