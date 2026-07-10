using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Diagnostics;
using Microsoft.Win32;
using TimetableScheduler.Domain;
using TimetableScheduler.ViewModel.Editors;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Views;

public partial class DataInputView : UserControl
{
    private static readonly IReadOnlyList<int> ProfessorUnavailablePeriods =
        TimetableScheduler.Solver.Constants.Periods
            .Where(period => period < TimetableScheduler.Solver.Constants.FirstNightPeriod)
            .ToList();

    private DataInputViewModel? _subscribedVm;

    public DataInputView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private DataInputViewModel? Vm => DataContext as DataInputViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.DeleteBlocked -= OnDeleteBlocked;
            _subscribedVm.BackConfirmationRequested -= OnBackConfirmationRequested;
        }
        _subscribedVm = e.NewValue as DataInputViewModel;
        if (_subscribedVm != null)
        {
            _subscribedVm.DeleteBlocked += OnDeleteBlocked;
            _subscribedVm.BackConfirmationRequested += OnBackConfirmationRequested;
        }
    }

    private static void OnDeleteBlocked(object? sender, string message)
    {
        MessageBox.Show(
            message,
            "삭제 불가",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void OnBackConfirmationRequested(object? sender, UnsavedBackNavigationEventArgs e)
    {
        var labels = e.UnsavedEditLabels.Take(5).ToList();
        var more = e.UnsavedEditLabels.Count > labels.Count
            ? $"{Environment.NewLine}- \uC678 {e.UnsavedEditLabels.Count - labels.Count}\uAC74"
            : "";
        var body =
            "\uC800\uC7A5\uD558\uC9C0 \uC54A\uC740 \uC218\uC815 \uC0AC\uD56D\uC774 \uC788\uC2B5\uB2C8\uB2E4." +
            $"{Environment.NewLine}\uB4A4\uB85C\uAC00\uAE30\uB97C \uD558\uBA74 \uB2E4\uC74C \uC218\uC815 \uB0B4\uC6A9\uC774 \uC800\uC7A5\uB418\uC9C0 \uC54A\uACE0 \uC0AC\uB77C\uC9D1\uB2C8\uB2E4." +
            $"{Environment.NewLine}{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", labels)}{more}" +
            $"{Environment.NewLine}{Environment.NewLine}\uADF8\uB798\uB3C4 \uB4A4\uB85C\uAC00\uC2DC\uACA0\uC2B5\uB2C8\uAE4C?";

        var result = MessageBox.Show(
            body,
            "\uC800\uC7A5\uD558\uC9C0 \uC54A\uC740 \uC218\uC815",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        e.ShouldContinue = result == MessageBoxResult.Yes;
    }

    private void OnImportXlsxClick(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "교과 데이터 xlsx 선택",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            Vm.ImportXlsxCommand.Execute(dlg.FileName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XLSX_IMPORT] Manual import failed: {ex}");
            MessageBox.Show(
                "불러오기에 실패하였습니다.",
                "불러오기 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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

    private void OnNumericTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateTarget();
    }

    private void OnProfessorExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander || expander.DataContext is not ProfessorItem item || Vm == null) return;
        var prof = item.Professor;

        if (expander.FindName("UnavailableSlotsPicker") is TimeSlotPickerControl slotPicker)
        {
            slotPicker.DataContext = new TimeSlotPickerViewModel(
                prof.UnavailableSlots,
                ProfessorUnavailablePeriods,
                Vm.Workspace.SchedulePolicy);
        }
    }

    private void OnCourseGroupExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander || expander.DataContext is not CourseGroupItem item || Vm == null) return;
        var course = item.Sections[0];

        var rooms = Vm.Workspace.Rooms.ToList();
        List<CheckListItem>? unavailableRoomItems = null;
        List<CheckListItem>? fixedRoomItems = null;

        if (expander.FindName("GroupUnavailableRoomsPicker") is CheckListPickerControl roomsPicker)
        {
            unavailableRoomItems = CheckListBinder.Bind(
                rooms,
                r => r.Id, r => RoomDisplayLabel(r),
                course.UnavailableRooms,
                (id, isChecked) =>
                {
                    if (!isChecked) return;
                    var fixedRoomItem = fixedRoomItems?.FirstOrDefault(item => item.Id == id);
                    if (fixedRoomItem != null) fixedRoomItem.IsChecked = false;
                });
            roomsPicker.DataContext = unavailableRoomItems;
        }
        if (expander.FindName("GroupFixedRoomsPicker") is CheckListPickerControl fixedRoomsPicker)
        {
            fixedRoomItems = CheckListBinder.Bind(
                rooms,
                r => r.Id, r => RoomDisplayLabel(r),
                course.FixedRooms,
                (id, isChecked) =>
                {
                    if (!isChecked) return;
                    var unavailableRoomItem = unavailableRoomItems?.FirstOrDefault(item => item.Id == id);
                    if (unavailableRoomItem != null) unavailableRoomItem.IsChecked = false;
                });
            fixedRoomsPicker.DataContext = fixedRoomItems;
        }
        if (expander.FindName("GroupCoteachProfsPicker") is CheckListPickerControl coteachPicker)
        {
            coteachPicker.DataContext = CheckListBinder.Bind(
                Vm.Workspace.Professors.ToList(),
                p => p.Id, p => p.Name,
                course.CoteachProfs);
        }
        item.FixedSlotEditor = FixedSlotEditorViewModel.Build(
            item, course.IsFixed, Vm.Workspace.SchedulePolicy);
        RefreshCourseBlockStructureCombo(expander, item);
        RefreshSchoolFixedDependentControls(expander, item);
    }

    private void OnCourseHoursChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        if (e.RemovedItems.Count == 0) return;
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not int weeklyHours) return;

        var item = FindCourseGroupItem(dep);
        if (item == null) return;
        var expander = FindAncestor<Expander>(dep);
        if (expander == null) return;

        UpdateSectionProfessorSelectionSources(expander);
        Vm.HandleCourseHoursChanged(item, weeklyHours);
        RefreshCourseBlockStructureCombo(expander, item);
        RefreshFixedTimeCheckBox(expander);
        RebuildFixedSlotEditor(item);
    }

    private void OnCourseBlockStructureChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        if (e.RemovedItems.Count == 0) return;
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not string) return;

        if (sender is ComboBox combo)
            BindingOperations.GetBindingExpression(combo, ComboBox.SelectedItemProperty)?.UpdateSource();

        var item = FindCourseGroupItem(dep);
        if (item == null) return;
        var expander = FindAncestor<Expander>(dep);
        if (expander == null) return;

        Vm.HandleCourseBlockStructureChanged(item);
        RefreshCourseBlockStructureCombo(expander, item);
        RefreshFixedTimeCheckBox(expander);
        RebuildFixedSlotEditor(item);
    }

    private void OnCourseSharedSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || e.AddedItems.Count == 0) return;
        UpdateCourseSharedSelectionSource(combo);
    }

    private static void UpdateCourseSharedSelectionSource(ComboBox combo)
    {
        if (combo.SelectedItem == null) return;
        var property = string.IsNullOrEmpty(combo.SelectedValuePath)
            ? ComboBox.SelectedItemProperty
            : ComboBox.SelectedValueProperty;
        BindingOperations.GetBindingExpression(combo, property)?.UpdateSource();
    }

    private void OnIsFixedCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        var item = FindCourseGroupItem(dep);
        if (item == null) return;
        var expander = FindAncestor<Expander>(dep);
        if (expander == null) return;
        if (!item.Sections[0].IsFixed)
        {
            foreach (var section in item.Sections)
                section.FixedSlots.Clear();
        }
        item.FixedSlotEditor = FixedSlotEditorViewModel.Build(
            item, item.Sections[0].IsFixed, Vm.Workspace.SchedulePolicy);
    }

    private void OnSchoolFixedCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        if (sender is CheckBox checkBox)
            BindingOperations.GetBindingExpression(checkBox, CheckBox.IsCheckedProperty)?.UpdateSource();
        var item = FindCourseGroupItem(dep);
        if (item == null) return;
        var expander = FindAncestor<Expander>(dep);
        if (expander == null) return;

        Vm.HandleCourseSchoolFixedChanged(item);
        RefreshFixedTimeCheckBox(expander);
        RefreshSchoolFixedDependentControls(expander, item);
        RebuildFixedSlotEditor(item);
    }

    private static void RefreshCourseBlockStructureCombo(Expander expander, CourseGroupItem item)
    {
        if (expander.FindName("BlockStructureComboBox") is not ComboBox combo) return;

        combo.ItemsSource = DataInputViewModel.GenerateBlockStructureOptions(item.Sections[0].HoursPerWeek);
        combo.SetCurrentValue(ComboBox.SelectedItemProperty,
            DataInputViewModel.FormatBlockStructure(item.Sections[0].BlockStructure));
        BindingOperations.GetBindingExpression(combo, ComboBox.SelectedItemProperty)?.UpdateTarget();
    }

    private void RebuildFixedSlotEditor(CourseGroupItem item)
    {
        item.FixedSlotEditor = FixedSlotEditorViewModel.Build(
            item,
            item.Sections[0].IsFixed,
            Vm?.Workspace.SchedulePolicy ?? SchedulePolicy.Default);
    }

    private static void RefreshFixedTimeCheckBox(Expander expander)
    {
        if (expander.FindName("IsFixedCheckBox") is not CheckBox checkBox) return;

        BindingOperations.GetBindingExpression(checkBox, CheckBox.IsCheckedProperty)?.UpdateTarget();
    }

    private static void RefreshSchoolFixedDependentControls(Expander expander, CourseGroupItem item)
    {
        var isEnabled = item.Sections.Count == 0 || !item.Sections[0].IsSchoolFixed;

        if (expander.FindName("CourseGradeComboBox") is ComboBox gradeCombo)
        {
            gradeCombo.IsEnabled = isEnabled;
            BindingOperations.GetBindingExpression(gradeCombo, ComboBox.SelectedValueProperty)?.UpdateTarget();
        }

        if (expander.FindName("SectionProfessorItemsControl") is ItemsControl sectionProfessorItems)
        {
            sectionProfessorItems.IsEnabled = isEnabled;
            foreach (var combo in FindDescendants<ComboBox>(sectionProfessorItems))
                BindingOperations.GetBindingExpression(combo, ComboBox.SelectedValueProperty)?.UpdateTarget();
        }

        RefreshCheckListPickerEnabled(expander, "GroupCoteachProfsPicker", isEnabled);
        RefreshCheckListPickerEnabled(expander, "GroupUnavailableRoomsPicker", isEnabled);
        RefreshCheckListPickerEnabled(expander, "GroupFixedRoomsPicker", isEnabled);
        RefreshButtonEnabled(expander, "CourseUnavailableLabRoomsButton", isEnabled);
        RefreshButtonEnabled(expander, "CourseUnavailableNonLabRoomsButton", isEnabled);
        RefreshButtonEnabled(expander, "CourseUnavailableClearRoomsButton", isEnabled);
    }

    private static void RefreshCheckListPickerEnabled(Expander expander, string name, bool isEnabled)
    {
        if (expander.FindName(name) is not CheckListPickerControl picker) return;

        picker.IsEnabled = isEnabled;
        if (picker.DataContext is not IEnumerable<CheckListItem> items) return;

        foreach (var item in items)
        {
            if (!isEnabled)
                item.IsChecked = false;
            item.IsEnabled = isEnabled;
        }
    }

    private static void RefreshButtonEnabled(Expander expander, string name, bool isEnabled)
    {
        if (expander.FindName(name) is Button button)
            button.IsEnabled = isEnabled;
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

    private static IEnumerable<T> FindDescendants<T>(DependencyObject start) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(start);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(start, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }

    private static void UpdateSectionProfessorSelectionSources(Expander expander)
    {
        foreach (var combo in FindDescendants<ComboBox>(expander)
            .Where(combo => combo.SelectedValuePath == "Id" && combo.Items.OfType<Professor>().Any()))
        {
            UpdateCourseSharedSelectionSource(combo);
        }
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

        var expander = FindAncestor<Expander>(dep);
        if (expander?.FindName("CourseTypeComboBox") is ComboBox courseTypeCombo)
            UpdateCourseSharedSelectionSource(courseTypeCombo);
        if (expander != null)
            UpdateSectionProfessorSelectionSources(expander);

        var rep = item.Sections[0];
        if (rep.BlockStructure.Count > 0 && rep.BlockStructure.Sum() != rep.HoursPerWeek)
        {
            MessageBox.Show(
                $"블록구조 합({rep.BlockStructure.Sum()})이 주당 수업시간({rep.HoursPerWeek})과 일치하지 않습니다.\n" +
                "해를 찾을 수 없으니 값을 맞춰주세요.",
                "저장 불가",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Flush fixed slot editor values back to sections before save
        var editorCtrl = expander != null ? FindDescendant<FixedSlotEditorControl>(expander) : null;
        var candidateFixedSlots = editorCtrl?.DataContext is FixedSlotEditorViewModel pendingEditorVm
            ? pendingEditorVm.SectionEditors
                .Select(sectionEditor => (IReadOnlyList<TimeSlot>)sectionEditor.ToFixedSlots())
                .ToList()
            : null;
        var overlap = Vm.FindFixedTimeOverlap(item, candidateFixedSlots);
        if (overlap != null)
        {
            var dayName = overlap.Day switch
            {
                0 => "월",
                1 => "화",
                2 => "수",
                3 => "목",
                4 => "금",
                _ => "?",
            };
            var sectionText = overlap.ExistingSection > 0 ? $" ({overlap.ExistingSection}분반)" : string.Empty;
            MessageBox.Show(
                $"선택한 고정 시간이 기존 고정 수업과 겹칩니다.\n" +
                $"겹치는 시간: {dayName}요일 {overlap.Period}교시\n" +
                $"기존 수업: {overlap.ExistingCourseName}{sectionText}",
                "저장 불가",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        var schoolFixedOverlap = Vm.FindSchoolFixedTimeOverlapWarning(item, candidateFixedSlots);
        if (schoolFixedOverlap != null)
        {
            var dayName = schoolFixedOverlap.Day switch
            {
                0 => "월",
                1 => "화",
                2 => "수",
                3 => "목",
                4 => "금",
                _ => "?",
            };
            var sectionText = schoolFixedOverlap.ExistingSection > 0 ? $" ({schoolFixedOverlap.ExistingSection}분반)" : string.Empty;
            var result = MessageBox.Show(
                $"선택한 고정 시간이 학교 고정 시간과 겹칩니다.\n" +
                $"겹치는 시간: {dayName}요일 {schoolFixedOverlap.Period}교시\n" +
                $"겹치는 항목: {schoolFixedOverlap.ExistingCourseName}{sectionText}\n\n" +
                "그래도 저장할까요?",
                "저장 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;
        }
        if (editorCtrl?.DataContext is FixedSlotEditorViewModel editorVm)
        {
            editorVm.ApplyTo(item);
        }
        if (item.IsFixedIndividual)
            Vm.SaveSectionCommand.Execute(item);
        else
            Vm.SaveGroupCommand.Execute(item);

        if (item.IsEditing && IsCourseSaveWarning(Vm.StatusMessage))
        {
            MessageBox.Show(
                Vm.StatusMessage,
                "저장 불가",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool IsCourseSaveWarning(string message) =>
        message.StartsWith("IE-014:", StringComparison.Ordinal) ||
        message.StartsWith("IE-015:", StringComparison.Ordinal) ||
        message.StartsWith("IE-016:", StringComparison.Ordinal) ||
        message.StartsWith("IE-017:", StringComparison.Ordinal) ||
        message.StartsWith("IE-018:", StringComparison.Ordinal) ||
        message.StartsWith("IE-042:", StringComparison.Ordinal);

    private void OnCourseGroupDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        var item = FindCourseGroupItem(dep);
        if (item == null) return;

        var label = item.Sections.Count > 1
            ? $"교과목 '{DisplayName(item.HeaderName, item.BaseId)}' {item.Sections.Count}개 분반"
            : $"교과목 '{DisplayName(item.HeaderName, item.BaseId)}'";
        var impact = Vm.PreviewCourseDeletion(item);
        if (!ConfirmDelete(label, impact)) return;

        if (item.IsFixedIndividual)
            Vm.DeleteSectionCommand.Execute(item);
        else
            Vm.DeleteGroupCommand.Execute(item);
    }

    private void OnCourseGroupCancelClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject dep || Vm == null) return;
        var item = FindCourseGroupItem(dep);
        if (item == null) return;

        Vm.CancelGroupCommand.Execute(item);
        if (FindAncestor<Expander>(dep) is Expander expander)
            OnCourseGroupExpanded(expander, new RoutedEventArgs());
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
        if (sender is not FrameworkElement el || el.DataContext is not ProfessorItem item || Vm == null) return;
        Vm.SaveProfessorCommand.Execute(item);
    }

    private void OnProfessorCancelClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not ProfessorItem item || Vm == null) return;

        Vm.CancelProfessorCommand.Execute(item);
        if (FindAncestor<Expander>(el) is Expander expander)
            OnProfessorExpanded(expander, new RoutedEventArgs());
    }

    private void OnProfessorDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not ProfessorItem item || Vm == null) return;

        var label = $"교수 '{DisplayName(item.Professor.Name, item.Professor.Id)}'";
        if (!ConfirmDelete(label)) return;

        Vm.DeleteProfessorItemCommand.Execute(item);
    }

    private void OnRoomDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not RoomItem item || Vm == null) return;

        var label = $"강의실 '{DisplayName(item.Room.Name, item.Room.Id)}'";
        if (!ConfirmDelete(label)) return;

        Vm.DeleteRoomItemCommand.Execute(item);
    }

    private void OnCrossDeleteClick(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedCrossGroup == null) return;

        var label = $"Cross '{Vm.SelectedCrossGroup.Display}'";
        if (!ConfirmDelete(label)) return;

        Vm.DeleteCrossCommand.Execute(Vm.SelectedCrossGroup);
    }

    private static bool ConfirmDelete(string targetLabel, CourseDeletionImpact? impact = null)
    {
        var sessionWarning = impact == null
            ? ""
            : "\n\n경고: 이 과목을 삭제하면 현재 편집 중인 시간표의 해당 수업 배정과 수동 Cross 관계도 함께 제거됩니다.";
        var impactMessage = impact is { } value &&
            (value.TimetableAssignmentCount > 0 || value.ManualCrossLinkCount > 0 || value.RelatedConstraintCount > 0)
            ? $"\n\n시간표의 {value.TimetableAssignmentCount}개 배정, 수동 Cross {value.ManualCrossLinkCount}개, " +
              $"관련 조건 {value.RelatedConstraintCount}개도 함께 제거됩니다.\n" +
              "수동편집 화면에는 남은 수업만 유지됩니다."
            : "";
        var finalNotice = impact == null
            ? "\n\n경고: 삭제 후에는 되돌릴 수 없습니다."
            : "\n저장 전까지 기존 저장 시간표에는 반영되지 않습니다.";
        var result = MessageBox.Show(
            $"{targetLabel}을(를) 정말 삭제하시겠습니까?{sessionWarning}{impactMessage}{finalNotice}",
            "삭제 확인",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private static string DisplayName(string name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;

    private void OnCourseUnavailableLabRoomsClick(object sender, RoutedEventArgs e)
    {
        SetUnavailableRoomSelection(sender, room => room.IsLab);
    }

    private void OnCourseUnavailableNonLabRoomsClick(object sender, RoutedEventArgs e)
    {
        SetUnavailableRoomSelection(sender, room => !room.IsLab);
    }

    private void OnCourseUnavailableClearRoomsClick(object sender, RoutedEventArgs e)
    {
        SetUnavailableRoomSelection(sender, null);
    }

    private void SetUnavailableRoomSelection(object sender, Func<Room, bool>? includeRoom)
    {
        var expander = sender is DependencyObject dep ? FindAncestor<Expander>(dep) : null;
        if (expander?.FindName("GroupUnavailableRoomsPicker") is not CheckListPickerControl picker) return;
        if (picker.DataContext is not IEnumerable<CheckListItem> items) return;

        foreach (var item in items)
        {
            if (includeRoom == null)
            {
                item.IsChecked = false;
                continue;
            }

            var room = Vm?.Workspace.Rooms.FirstOrDefault(room => room.Id == item.Id);
            if (room != null && includeRoom(room)) item.IsChecked = true;
        }
    }

    private static string RoomDisplayLabel(Room room)
    {
        var parts = new List<string>();
        if (room.IsLab) parts.Add("실습실");
        if (room.Capacity > 0) parts.Add($"{room.Capacity}명");

        return parts.Count == 0 ? room.Name : $"{room.Name} ({string.Join(", ", parts)})";
    }
}
