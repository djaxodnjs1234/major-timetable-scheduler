using CommunityToolkit.Mvvm.ComponentModel;

namespace TimetableScheduler.ViewModel.Pages;

public abstract partial class PageViewModelBase : ObservableObject
{
    public abstract string Title { get; }

    public virtual void OnNavigatedTo() { }
    public virtual void OnNavigatedFrom() { }
}
