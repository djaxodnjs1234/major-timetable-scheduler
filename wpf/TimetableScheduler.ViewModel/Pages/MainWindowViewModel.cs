using CommunityToolkit.Mvvm.ComponentModel;

namespace TimetableScheduler.ViewModel.Pages;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly TimetableSelectionViewModel _selection;
    private readonly DataInputViewModel _input;
    private readonly ResultsViewModel _results;
    private readonly ManualEditViewModel _manual;

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
            // In existing-edit mode the solver ran on the session workspace, so the
            // results/manual screens must render against that session snapshot too.
            _results.SetSolutions(
                ranked, _input.IsExistingMode ? _input.CurrentSnapshot() : null);
        };
        _input.GoToSelectionRequested += (_, _) => NavigateTo(_results);
        _input.GoToManualRequested += (_, _) =>
        {
            var handoff = _input.BuildEditHandoff();
            if (handoff != null)
            {
                _manual.LoadFromSnapshot(
                    _input.CurrentSnapshot(), handoff, _input.EditBaseName);
            }
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
            {
                if (_results.SessionSnapshot != null)
                    _manual.LoadFromSnapshot(
                        _results.SessionSnapshot, _results.SelectedSolution, "");
                else
                    _manual.LoadFromSolution(_results.SelectedSolution);
            }
            NavigateTo(_manual);
        };

        _manual.SavedRequested += (_, _) => NavigateTo(_selection);

        currentPage = _selection;
    }

    private void NavigateTo(PageViewModelBase target)
    {
        CurrentPage?.OnNavigatedFrom();
        CurrentPage = target;
        CurrentPage.OnNavigatedTo();
    }
}
