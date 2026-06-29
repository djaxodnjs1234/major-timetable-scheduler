using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Editors;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf;
using TimetableScheduler.Wpf.Controls;
using TimetableScheduler.Wpf.Views;

namespace TimetableScheduler.Wpf.Tests;

public class DataInputViewTests
{
    [Fact]
    public void SolveAdvancedRetakeCheckbox_IsDeclaredWithBindingAndTooltip()
    {
        var xaml = File.ReadAllText(FindDataInputViewXaml());

        Assert.Contains("x:Name=\"ConsiderRetakeStudentsCheckBox\"", xaml);
        Assert.Contains("IsChecked=\"{Binding ConsiderRetakeStudents}\"", xaml);
        Assert.Contains("재수강생 고려", xaml);
        Assert.Contains("전필-전필 시간이 모두 겹치면 시간표가 생성되지 않을 수 있습니다.", xaml);
        Assert.Contains("설정한 강의실은 배정되지 않습니다.", xaml);
    }

    [Fact]
    public void CancelButton_UsesCancelCommandCanExecute()
    {
        var xaml = File.ReadAllText(FindDataInputViewXaml());
        var commandIndex = xaml.IndexOf("Command=\"{Binding CancelCommand}\"", StringComparison.Ordinal);

        Assert.True(commandIndex >= 0);

        var snippetStart = Math.Max(0, commandIndex - 80);
        var snippetLength = Math.Min(220, xaml.Length - snippetStart);
        var cancelButtonSnippet = xaml.Substring(snippetStart, snippetLength);
        Assert.Contains("Content=\"취소\"", cancelButtonSnippet);
        Assert.DoesNotContain("IsEnabled=\"{Binding IsSolving}\"", cancelButtonSnippet);
    }

    [Fact]
    public void BlockStructureChanged_RefreshesFixedCheckbox()
    {
        var source = File.ReadAllText(FindDataInputViewCodeBehind());
        var methodStart = source.IndexOf("private void OnCourseBlockStructureChanged", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);

        var nextMethodStart = source.IndexOf("private void OnCourseSharedSelectionChanged", methodStart, StringComparison.Ordinal);
        Assert.True(nextMethodStart > methodStart);

        var methodBody = source[methodStart..nextMethodStart];
        Assert.Contains("Vm.HandleCourseBlockStructureChanged(item);", methodBody);
        Assert.Contains("RefreshFixedTimeCheckBox(expander);", methodBody);
    }

    [Fact]
    public void UnavailableRoomQuickButtons_UseRoomClassificationAndClearAll()
    {
        var xaml = File.ReadAllText(FindDataInputViewXaml());
        var source = File.ReadAllText(FindDataInputViewCodeBehind());

        Assert.DoesNotContain("CourseUnavailableAllRoomsButton", xaml);
        Assert.Contains("x:Name=\"CourseUnavailableLabRoomsButton\"", xaml);
        Assert.Contains("Click=\"OnCourseUnavailableLabRoomsClick\"", xaml);
        Assert.Contains("x:Name=\"CourseUnavailableNonLabRoomsButton\"", xaml);
        Assert.Contains("Click=\"OnCourseUnavailableNonLabRoomsClick\"", xaml);
        Assert.Contains("x:Name=\"CourseUnavailableClearRoomsButton\"", xaml);
        Assert.Contains("Click=\"OnCourseUnavailableClearRoomsClick\"", xaml);
        Assert.Contains("SetUnavailableRoomSelection(sender, room => room.IsLab);", source);
        Assert.Contains("SetUnavailableRoomSelection(sender, room => !room.IsLab);", source);
        Assert.Contains("item.IsChecked = false;", source);
    }

    [Fact]
    public void ItemEditors_DeclareCancelButtons()
    {
        var xaml = File.ReadAllText(FindDataInputViewXaml());
        var source = File.ReadAllText(FindDataInputViewCodeBehind());

        Assert.Contains("Click=\"OnProfessorCancelClick\"", xaml);
        Assert.Contains("Click=\"OnCourseGroupCancelClick\"", xaml);
        Assert.Contains("DataContext.CancelRoomCommand", xaml);
        Assert.Contains("Vm.CancelProfessorCommand.Execute(item);", source);
        Assert.Contains("Vm.CancelGroupCommand.Execute(item);", source);
    }

    [Fact]
    public void CourseGroupSave_ShowsWarningWhenGraduateHoursValidationBlocksSave()
    {
        var source = File.ReadAllText(FindDataInputViewCodeBehind());
        var methodStart = source.IndexOf("private void OnCourseGroupSaveClick", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);

        var nextMethodStart = source.IndexOf("private void OnCourseGroupDeleteClick", methodStart, StringComparison.Ordinal);
        Assert.True(nextMethodStart > methodStart);

        var methodBody = source[methodStart..nextMethodStart];
        Assert.Contains("Vm.SaveGroupCommand.Execute(item);", methodBody);
        var warningStart = methodBody.IndexOf(
            "Vm.StatusMessage.StartsWith(\"IE-042:\", StringComparison.Ordinal)",
            StringComparison.Ordinal);
        Assert.True(warningStart >= 0);

        var warningBody = methodBody[warningStart..];
        Assert.Contains("MessageBox.Show(", warningBody);
        Assert.Contains("Vm.StatusMessage,", warningBody);
    }

    [Fact]
    public void GenerationSettings_ExposeOnlyTheTotalTimeLimit()
    {
        var xaml = File.ReadAllText(FindDataInputViewXaml());

        Assert.Contains("Text=\"{Binding TimeLimitSec, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml);
        Assert.Contains("전체 생성 제한 시간 (초)", xaml);
        Assert.DoesNotContain("PerSolveTimeSec", xaml);
    }

    [Fact]
    public void ChangingWeeklyHours_UnchecksFixedCheckboxAndResetsDefaultBlocks()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();
            var dbPath = Path.Combine(Path.GetTempPath(), $"wpf_view_test_{Guid.NewGuid():N}.db");

            try
            {
                var repo = new SqliteRepository(dbPath);
                var workspace = new WorkspaceService(repo);
                workspace.AddProfessor(new Professor { Id = "P1", Name = "교수1" });
                workspace.AddProfessor(new Professor { Id = "P2", Name = "교수2" });
                workspace.AddCourse(new Course
                {
                    Id = "GA1005-01",
                    Name = "테스트",
                    Grade = 2,
                    HoursPerWeek = 4,
                    Section = 1,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    BlockStructure = new List<int> { 2, 2 },
                    IsFixed = true,
                    FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1), new(1, 2) },
                });

                var vm = new DataInputViewModel(workspace, null!);
                var item = vm.CourseGroups.Single();
                item.IsEditing = true;

                var view = new DataInputView { DataContext = vm };
                var window = new Window
                {
                    Width = 1400,
                    Height = 900,
                    Content = view,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };

                window.Show();
                view.UpdateLayout();

                var expander = FindDescendant<Expander>(view, e => e.DataContext is CourseGroupItem);
                Assert.NotNull(expander);

                expander!.IsExpanded = true;
                view.UpdateLayout();

                var hoursCombo = FindDescendant<ComboBox>(expander, combo => Equals(combo.SelectedItem, 4));
                var blockCombo = FindDescendant<ComboBox>(expander, combo => Equals(combo.SelectedItem, "2+2"));
                var fixedCheckBox = FindDescendant<CheckBox>(expander, cb => cb.Content is string text && text == "시간 고정");
                var professorCombo = FindDescendant<ComboBox>(expander, combo =>
                    combo.SelectedValuePath == "Id" && combo.Items.OfType<Professor>().Any());

                Assert.NotNull(hoursCombo);
                Assert.NotNull(blockCombo);
                Assert.NotNull(fixedCheckBox);
                Assert.NotNull(professorCombo);
                Assert.True(fixedCheckBox!.IsChecked == true);
                Assert.Equal("P1", professorCombo!.SelectedValue);

                professorCombo.SelectedValue = "P2";
                hoursCombo!.SelectedItem = 3;
                view.UpdateLayout();

                Assert.Equal(3, item.Sections[0].HoursPerWeek);
                Assert.Equal(new[] { 1, 2 }, item.Sections[0].BlockStructure);
                Assert.False(item.Sections[0].IsFixed);
                Assert.True(fixedCheckBox.IsChecked == false);
                Assert.Equal("1+2", blockCombo!.SelectedItem);
                Assert.Equal("P2", item.Sections[0].ProfessorId);
                Assert.Equal("P2", professorCombo.SelectedValue);

                vm.SaveGroupCommand.Execute(item);
                vm.OnNavigatedTo();
                Assert.Equal("P2", vm.CourseGroups.Single().Sections[0].ProfessorId);

                window.Close();
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        });
    }

    [Fact]
    public void LoadingSavedTimetable_KeepsProfessorAndCourseTypeVisibleInCourseManagement()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();
            var dbPath = Path.Combine(Path.GetTempPath(), $"wpf_saved_view_test_{Guid.NewGuid():N}.db");

            try
            {
                var repo = new SqliteRepository(dbPath);
                var workspace = new WorkspaceService(repo);
                workspace.AddProfessor(new Professor { Id = "P1", Name = "스냅교수" });
                workspace.AddCourse(new Course
                {
                    Id = "GA1005-01",
                    Name = "저장과목",
                    Grade = 2,
                    HoursPerWeek = 3,
                    Section = 1,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    BlockStructure = new List<int> { 1, 2 },
                });
                workspace.SaveTimetable(
                    "saved",
                    new[] { new SolutionAssignment("GA1005-01", 0, 1, "R1") },
                    snapshot: workspace.Snapshot());

                workspace.DeleteCourse(workspace.Courses.Single());
                workspace.DeleteProfessor("P1");

                var vm = new DataInputViewModel(workspace, null!);
                vm.LoadForExistingTimetable(workspace.SavedTimetables.Single(t => t.Name == "saved"));

                var view = new DataInputView { DataContext = vm };
                var window = new Window
                {
                    Width = 1400,
                    Height = 900,
                    Content = view,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };

                window.Show();
                view.UpdateLayout();

                var expander = FindDescendant<Expander>(view, e => e.DataContext is CourseGroupItem);
                Assert.NotNull(expander);

                expander!.IsExpanded = true;
                view.UpdateLayout();

                var texts = FindDescendants<TextBlock>(view)
                    .Select(text => text.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                Assert.Contains("스냅교수", texts);
                Assert.Contains("전필", texts);

                window.Close();
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        });
    }

    [Fact]
    public void RebuildingRenderedCourseGroups_PreservesSingleAndMultiSectionProfessors()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();
            var dbPath = Path.Combine(Path.GetTempPath(), $"wpf_rebuild_professor_test_{Guid.NewGuid():N}.db");

            try
            {
                var repo = new SqliteRepository(dbPath);
                var workspace = new WorkspaceService(repo);
                workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor One" });
                workspace.AddCourse(new Course
                {
                    Id = "SINGLE-01",
                    Name = "Single",
                    Grade = 1,
                    HoursPerWeek = 1,
                    Section = 1,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    BlockStructure = new List<int> { 1 },
                });
                workspace.AddCourse(new Course
                {
                    Id = "MULTI-01",
                    Name = "Multi",
                    Grade = 2,
                    HoursPerWeek = 1,
                    Section = 1,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    BlockStructure = new List<int> { 1 },
                });
                workspace.AddCourse(new Course
                {
                    Id = "MULTI-02",
                    Name = "Multi",
                    Grade = 2,
                    HoursPerWeek = 1,
                    Section = 2,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    BlockStructure = new List<int> { 1 },
                });

                var vm = new DataInputViewModel(workspace, null!);
                foreach (var group in vm.CourseGroups)
                    group.IsEditing = true;

                var view = new DataInputView { DataContext = vm };
                var window = new Window
                {
                    Width = 1400,
                    Height = 900,
                    Content = view,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };

                window.Show();
                view.UpdateLayout();
                foreach (var expander in FindDescendants<Expander>(view)
                    .Where(expander => expander.DataContext is CourseGroupItem))
                {
                    expander.IsExpanded = true;
                }
                view.UpdateLayout();

                vm.OnNavigatedTo();
                view.UpdateLayout();

                Assert.All(workspace.Courses, course => Assert.Equal("P1", course.ProfessorId));
                Assert.Equal("P1", vm.CourseGroups.Single(group => group.BaseId == "SINGLE").Sections[0].ProfessorId);
                Assert.All(vm.CourseGroups.Single(group => group.BaseId == "MULTI").Sections,
                    section => Assert.Equal("P1", section.ProfessorId));

                window.Close();
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        });
    }

    [Fact]
    public void SectionProfessorCombos_SaveIndependentlyWhileSharedComboSavesToAllSections()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();
            var dbPath = Path.Combine(Path.GetTempPath(), $"wpf_shared_combo_test_{Guid.NewGuid():N}.db");

            try
            {
                var repo = new SqliteRepository(dbPath);
                var workspace = new WorkspaceService(repo);
                workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor One" });
                workspace.AddProfessor(new Professor { Id = "P2", Name = "Professor Two" });
                workspace.AddCourse(new Course
                {
                    Id = "MULTI-01",
                    Name = "Multi",
                    Grade = 2,
                    HoursPerWeek = 1,
                    Section = 1,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    BlockStructure = new List<int> { 1 },
                });
                workspace.AddCourse(new Course
                {
                    Id = "MULTI-02",
                    Name = "Multi",
                    Grade = 2,
                    HoursPerWeek = 1,
                    Section = 2,
                    CourseType = "전필",
                    ProfessorId = "P1",
                    BlockStructure = new List<int> { 1 },
                });

                var vm = new DataInputViewModel(workspace, null!);
                var group = vm.CourseGroups.Single();

                var view = new DataInputView { DataContext = vm };
                var window = new Window
                {
                    Width = 1400,
                    Height = 900,
                    Content = view,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };

                window.Show();
                view.UpdateLayout();
                var expander = FindDescendant<Expander>(view, item => item.DataContext == group);
                Assert.NotNull(expander);
                expander!.IsExpanded = true;
                vm.EditCourseCommand.Execute(group);
                view.UpdateLayout();

                var professorCombos = FindDescendants<ComboBox>(expander)
                    .Where(combo => combo.SelectedValuePath == "Id"
                        && combo.Items.OfType<Professor>().Any())
                    .ToList();
                var courseTypeCombo = FindDescendant<ComboBox>(expander, combo => combo.Name == "CourseTypeComboBox");
                Assert.Equal(2, professorCombos.Count);
                Assert.NotNull(courseTypeCombo);

                professorCombos[0].SelectedValue = "P2";
                professorCombos[1].SelectedValue = "P1";
                courseTypeCombo!.SelectedItem = "전선";
                view.UpdateLayout();
                vm.SaveGroupCommand.Execute(group);

                var saved = workspace.Courses.OrderBy(course => course.Section).ToList();
                Assert.Equal("P2", saved[0].ProfessorId);
                Assert.Equal("P1", saved[1].ProfessorId);
                Assert.All(workspace.Courses, course => Assert.Equal("전선", course.CourseType));

                window.Close();
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        });
    }

    [Fact]
    public void CourseRoomPickers_KeepUnavailableAndMultiRoomsExclusive_AndSupportRoomTypeSelection()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();
            var dbPath = Path.Combine(Path.GetTempPath(), $"wpf_room_picker_test_{Guid.NewGuid():N}.db");

            try
            {
                var repo = new SqliteRepository(dbPath);
                var workspace = new WorkspaceService(repo);
                workspace.AddRoom(new Room { Id = "R1", Name = "Room One" });
                workspace.AddRoom(new Room { Id = "R2", Name = "Room Two", IsLab = true });
                workspace.AddRoom(new Room { Id = "R3", Name = "Room Three", IsLab = true });
                workspace.AddCourse(new Course
                {
                    Id = "ROOM-01",
                    Name = "Room Course",
                    Grade = 2,
                    HoursPerWeek = 1,
                    Section = 1,
                    CourseType = "전필",
                    BlockStructure = new List<int> { 1 },
                    FixedRooms = new List<string> { "R1" },
                    UnavailableRooms = new List<string> { "R2" },
                });

                var vm = new DataInputViewModel(workspace, null!);
                var group = vm.CourseGroups.Single();

                var view = new DataInputView { DataContext = vm };
                var window = new Window
                {
                    Width = 1400,
                    Height = 900,
                    Content = view,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };

                window.Show();
                view.UpdateLayout();
                var expander = FindDescendant<Expander>(view, item => item.DataContext == group);
                Assert.NotNull(expander);
                expander!.IsExpanded = true;
                view.UpdateLayout();

                var unavailablePicker = FindDescendant<CheckListPickerControl>(expander, picker => picker.Name == "GroupUnavailableRoomsPicker");
                var fixedPicker = FindDescendant<CheckListPickerControl>(expander, picker => picker.Name == "GroupFixedRoomsPicker");
                var labButton = FindDescendant<Button>(expander, button => button.Name == "CourseUnavailableLabRoomsButton");
                var nonLabButton = FindDescendant<Button>(expander, button => button.Name == "CourseUnavailableNonLabRoomsButton");
                var clearButton = FindDescendant<Button>(expander, button => button.Name == "CourseUnavailableClearRoomsButton");
                Assert.NotNull(unavailablePicker);
                Assert.NotNull(fixedPicker);
                Assert.NotNull(labButton);
                Assert.NotNull(nonLabButton);
                Assert.NotNull(clearButton);
                Assert.False(labButton!.IsVisible);
                Assert.False(nonLabButton!.IsVisible);
                Assert.False(clearButton!.IsVisible);

                group.IsEditing = true;
                view.UpdateLayout();
                Assert.True(labButton.IsVisible);
                Assert.True(nonLabButton.IsVisible);
                Assert.True(clearButton.IsVisible);

                var unavailableItems = ((IEnumerable<CheckListItem>)unavailablePicker!.DataContext).ToList();
                var fixedItems = ((IEnumerable<CheckListItem>)fixedPicker!.DataContext).ToList();

                unavailableItems.Single(item => item.Id == "R1").IsChecked = true;
                Assert.DoesNotContain("R1", group.Sections[0].FixedRooms);
                Assert.Contains("R1", group.Sections[0].UnavailableRooms);

                fixedItems.Single(item => item.Id == "R2").IsChecked = true;
                Assert.Contains("R2", group.Sections[0].FixedRooms);
                Assert.DoesNotContain("R2", group.Sections[0].UnavailableRooms);

                labButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(group.Sections[0].FixedRooms);
                Assert.Equal(new[] { "R1", "R2", "R3" }, group.Sections[0].UnavailableRooms.OrderBy(id => id));

                clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(group.Sections[0].UnavailableRooms);

                nonLabButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(new[] { "R1" }, group.Sections[0].UnavailableRooms);

                window.Close();
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
            throw new Xunit.Sdk.XunitException($"WPF STA test failed: {error}");
    }

    private static void EnsureApplicationResources()
    {
        lock (typeof(App))
        {
            if (Application.Current != null) return;

            var app = new App
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            app.InitializeComponent();
        }
    }

    private static string FindDataInputViewXaml([CallerFilePath] string sourceFile = "")
    {
        var starts = new[] { Path.GetDirectoryName(sourceFile) ?? "", AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in starts)
        {
            var dir = start;
            for (var i = 0; i < 16 && dir != null; i++)
            {
                var path = Path.Combine(dir, "wpf", "TimetableScheduler.Wpf", "Views", "DataInputView.xaml");
                if (File.Exists(path))
                    return path;
                dir = Path.GetDirectoryName(dir);
            }
        }

        throw new InvalidOperationException("DataInputView.xaml not found");
    }

    private static string FindDataInputViewCodeBehind([CallerFilePath] string sourceFile = "")
    {
        var starts = new[] { Path.GetDirectoryName(sourceFile) ?? "", AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in starts)
        {
            var dir = start;
            for (var i = 0; i < 16 && dir != null; i++)
            {
                var path = Path.Combine(dir, "wpf", "TimetableScheduler.Wpf", "Views", "DataInputView.xaml.cs");
                if (File.Exists(path))
                    return path;
                dir = Path.GetDirectoryName(dir);
            }
        }

        throw new InvalidOperationException("DataInputView.xaml.cs not found");
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && (predicate == null || predicate(match)))
                return match;

            var nested = FindDescendant(child, predicate);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static List<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var results = new List<T>();
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                results.Add(match);

            results.AddRange(FindDescendants<T>(child));
        }

        return results;
    }
}
