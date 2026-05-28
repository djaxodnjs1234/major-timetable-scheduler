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

    private void OnCourseGroupExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander || expander.DataContext is not CourseGroupItem item || Vm == null) return;
        var course = item.Sections[0];

        if (expander.FindName("GroupFixedRoomsPicker") is CheckListPickerControl roomsPicker)
        {
            roomsPicker.DataContext = CheckListBinder.Bind(
                Vm.Workspace.Rooms.ToList(),
                r => r.Id, r => $"{r.Id} ({r.Name})",
                course.FixedRooms);
        }
        if (expander.FindName("GroupCoteachProfsPicker") is CheckListPickerControl coteachPicker)
        {
            coteachPicker.DataContext = CheckListBinder.Bind(
                Vm.Workspace.Professors.ToList(),
                p => p.Id, p => $"{p.Id} ({p.Name})",
                course.CoteachProfs);
        }
        if (expander.FindName("FixedSlotEditor") is FixedSlotEditorControl editor)
        {
            editor.DataContext = FixedSlotEditorViewModel.Build(item, course.IsFixed);
        }
    }

    private void OnIsFixedCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject dep) return;
        var item = FindCourseGroupItem(dep);
        if (item == null) return;
        var expander = FindAncestor<Expander>(dep);
        if (expander == null) return;
        var editor = FindDescendant<FixedSlotEditorControl>(expander);
        if (editor != null)
            editor.DataContext = FixedSlotEditorViewModel.Build(item, item.Sections[0].IsFixed);
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(start);
        while (current != null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject start) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(start);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(start, i);
            if (child is T match) return match;
            var result = FindDescendant<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private static CourseGroupItem? FindCourseGroupItem(DependencyObject start)
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(start);
        while (current != null)
        {
            if (current is Expander exp && exp.DataContext is CourseGroupItem item)
                return item;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnCourseGroupSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        var item = FindCourseGroupItem(dep);
        if (item == null) return;

        var rep = item.Sections[0];
        if (rep.BlockStructure.Count > 0 && rep.BlockStructure.Sum() != rep.HoursPerWeek)
        {
            MessageBox.Show(
                $"블록구조 합({rep.BlockStructure.Sum()})이 시수/주({rep.HoursPerWeek})와 일치하지 않습니다.\n" +
                "해를 찾을 수 없으니 값을 맞춰주세요.",
                "저장 불가",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Flush fixed slot editor values back to sections before save
        var expander = FindAncestor<Expander>(dep);
        var editorCtrl = expander != null ? FindDescendant<FixedSlotEditorControl>(expander) : null;
        if (editorCtrl?.DataContext is FixedSlotEditorViewModel editorVm)
        {
            editorVm.ApplyTo(item);
        }
        if (item.IsFixedIndividual)
            Vm.SaveSectionCommand.Execute(item);
        else
            Vm.SaveGroupCommand.Execute(item);
    }

    private void OnCourseGroupDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        var item = FindCourseGroupItem(dep);
        if (item == null) return;
        if (item.IsFixedIndividual)
            Vm.DeleteSectionCommand.Execute(item);
        else
            Vm.DeleteGroupCommand.Execute(item);
    }

    private void OnAddSectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        var item = FindCourseGroupItem(dep);
        if (item != null) Vm.AddSectionCommand.Execute(item);
    }

    private void OnRemoveSectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        var item = FindCourseGroupItem(dep);
        if (item != null) Vm.RemoveSectionCommand.Execute(item);
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
}
