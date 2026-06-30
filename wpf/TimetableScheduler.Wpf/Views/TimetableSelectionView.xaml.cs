using System.Windows;
using System.Windows.Controls;
using TimetableScheduler.Data;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Views;

public partial class TimetableSelectionView : UserControl
{
    public TimetableZoom Zoom { get; } = new();

    public TimetableSelectionView() => InitializeComponent();

    private TimetableSelectionViewModel? Vm => DataContext as TimetableSelectionViewModel;

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Zoom.ZoomOut();

    private void OnZoomResetClick(object sender, RoutedEventArgs e) => Zoom.Reset();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Zoom.ZoomIn();

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not SavedTimetableRecord record || Vm == null) return;
        var result = MessageBox.Show(
            $"'{record.Name}' 시간표를 삭제하시겠습니까?",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            Vm.DeleteTimetableCommand.Execute(record);
    }

}
