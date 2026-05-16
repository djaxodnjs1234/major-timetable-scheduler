using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TimetableScheduler.Domain;
using TimetableScheduler.ViewModel.Editors;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Views;

public partial class DataInputView : UserControl
{
    public DataInputView() => InitializeComponent();

    private DataInputViewModel? Vm => DataContext as DataInputViewModel;

    private void OnImportXlsxClick(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "교과 데이터 xlsx 선택",
        };
        if (dlg.ShowDialog() == true)
            Vm.ImportXlsxCommand.Execute(dlg.FileName);
    }

    private void OnExportDbClick(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            Title = "워크스페이스 DB 저장",
            FileName = "timetable_backup.db",
        };
        if (dlg.ShowDialog() == true)
            Vm.Workspace.ExportDatabase(dlg.FileName);
    }

    private void OnImportDbClick(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var result = MessageBox.Show(
            "백업 파일로 복원하면 현재 모든 설정과 저장된 시간표가 백업 파일의 내용으로 교체됩니다.\n계속하시겠습니까?",
            "백업 파일로 복원",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var dlg = new OpenFileDialog
        {
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            Title = "워크스페이스 DB 선택",
        };
        if (dlg.ShowDialog() == true)
            Vm.Workspace.ImportDatabase(dlg.FileName);
    }

    private void OnProfessorExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander || expander.DataContext is not Professor prof || Vm == null) return;

        if (expander.FindName("AllowedRoomsPicker") is CheckListPickerControl roomsPicker)
        {
            roomsPicker.DataContext = CheckListBinder.Bind(
                Vm.Workspace.Rooms.ToList(),
                r => r.Id, r => $"{r.Id} ({r.Name})",
                prof.AllowedRooms);
        }
        if (expander.FindName("UnavailableSlotsPicker") is TimeSlotPickerControl slotPicker)
        {
            slotPicker.DataContext = new TimeSlotPickerViewModel(prof.UnavailableSlots);
        }
    }

    private void OnCourseExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander || expander.DataContext is not Course course || Vm == null) return;

        if (expander.FindName("FixedRoomsPicker") is CheckListPickerControl roomsPicker)
        {
            roomsPicker.DataContext = CheckListBinder.Bind(
                Vm.Workspace.Rooms.ToList(),
                r => r.Id, r => $"{r.Id} ({r.Name})",
                course.FixedRooms);
        }
        if (expander.FindName("CoteachProfsPicker") is CheckListPickerControl coteachPicker)
        {
            coteachPicker.DataContext = CheckListBinder.Bind(
                Vm.Workspace.Professors.ToList(),
                p => p.Id, p => $"{p.Id} ({p.Name})",
                course.CoteachProfs);
        }
        if (expander.FindName("FixedSlotsPicker") is TimeSlotPickerControl slotPicker)
        {
            slotPicker.DataContext = new TimeSlotPickerViewModel(course.FixedSlots);
        }
    }

    private void OnProfessorSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is Professor p && Vm != null)
            Vm.SelectedItem = p;
    }

    private void OnProfessorDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is Professor p && Vm != null)
            Vm.SelectedItem = p;
    }

    private void OnCourseSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is Course c && Vm != null)
            Vm.SelectedItem = c;
    }

    private void OnCourseDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is Course c && Vm != null)
            Vm.SelectedItem = c;
    }
}
