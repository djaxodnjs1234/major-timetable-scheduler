using System.Windows;
using System.Windows.Controls;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Views;

public partial class ResultsView : UserControl
{
    public TimetableZoom Zoom { get; } = new();

    public ResultsView() => InitializeComponent();

    private ResultsViewModel? Vm => DataContext as ResultsViewModel;

    private void OnCardMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is SolutionCardViewModel card)
            Vm?.SelectCardCommand.Execute(card);
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Zoom.ZoomOut();

    private void OnZoomResetClick(object sender, RoutedEventArgs e) => Zoom.Reset();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Zoom.ZoomIn();
}
