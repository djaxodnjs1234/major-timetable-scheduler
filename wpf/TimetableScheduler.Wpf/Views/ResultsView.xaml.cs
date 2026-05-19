using System.Windows;
using System.Windows.Controls;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Wpf.Views;

public partial class ResultsView : UserControl
{
    public ResultsView() => InitializeComponent();

    private ResultsViewModel? Vm => DataContext as ResultsViewModel;

    private void OnCardMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is SolutionCardViewModel card)
            Vm?.SelectCardCommand.Execute(card);
    }
}
