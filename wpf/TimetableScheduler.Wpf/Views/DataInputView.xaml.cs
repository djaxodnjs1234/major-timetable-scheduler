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
