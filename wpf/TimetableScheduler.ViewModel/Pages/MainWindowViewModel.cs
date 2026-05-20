using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
            _results.SetSolutions(ranked);
        };
        _input.GoToSelectionRequested += (_, _) => NavigateTo(_results);
        _selection.EditRequested += (_, timetable) =>
        {
            _manual.LoadFromSavedTimetable(timetable);
            NavigateTo(_manual);
        };

        currentPage = _selection;
    }

    [RelayCommand]
    private void GoToSelection() => NavigateTo(_selection);

    [RelayCommand]
    private void GoToInput() => NavigateTo(_input);

    [RelayCommand]
    private void GoToResults() => NavigateTo(_results);

    [RelayCommand]
    private void GoToManual()
    {
        if (_results.SelectedSolution != null)
            _manual.LoadFromSolution(_results.SelectedSolution);
        NavigateTo(_manual);
    }

    private void NavigateTo(PageViewModelBase target)
    {
        CurrentPage?.OnNavigatedFrom();
        CurrentPage = target;
        CurrentPage.OnNavigatedTo();
    }
}
