using System.IO;
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
                workspace.AddCourse(new Course
                {
                    Id = "GA1005-01",
                    Name = "테스트",
                    Grade = 2,
                    HoursPerWeek = 4,
                    Section = 1,
                    CourseType = "전필",
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

                Assert.NotNull(hoursCombo);
                Assert.NotNull(blockCombo);
                Assert.NotNull(fixedCheckBox);
                Assert.True(fixedCheckBox!.IsChecked == true);

                hoursCombo!.SelectedItem = 3;
                view.UpdateLayout();

                Assert.Equal(3, item.Sections[0].HoursPerWeek);
                Assert.Equal(new[] { 1, 2 }, item.Sections[0].BlockStructure);
                Assert.False(item.Sections[0].IsFixed);
                Assert.True(fixedCheckBox.IsChecked == false);
                Assert.Equal("1+2", blockCombo!.SelectedItem);

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
    public void ValidSharedComboSelections_SaveToAllSections()
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
                view.UpdateLayout();

                var professorCombo = FindDescendant<ComboBox>(expander, combo => combo.Name == "ProfessorComboBox");
                var courseTypeCombo = FindDescendant<ComboBox>(expander, combo => combo.Name == "CourseTypeComboBox");
                Assert.NotNull(professorCombo);
                Assert.NotNull(courseTypeCombo);

                professorCombo!.SelectedValue = "P2";
                courseTypeCombo!.SelectedItem = "전선";
                view.UpdateLayout();
                vm.SaveGroupCommand.Execute(group);

                Assert.All(workspace.Courses, course => Assert.Equal("P2", course.ProfessorId));
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
    public void CourseRoomPickers_KeepUnavailableAndMultiRoomsExclusive()
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
                workspace.AddRoom(new Room { Id = "R2", Name = "Room Two" });
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
                var allButton = FindDescendant<Button>(expander, button => button.Name == "CourseUnavailableAllRoomsButton");
                Assert.NotNull(unavailablePicker);
                Assert.NotNull(fixedPicker);
                Assert.NotNull(allButton);
                Assert.False(allButton!.IsVisible);

                group.IsEditing = true;
                view.UpdateLayout();
                Assert.True(allButton.IsVisible);

                var unavailableItems = ((IEnumerable<CheckListItem>)unavailablePicker!.DataContext).ToList();
                var fixedItems = ((IEnumerable<CheckListItem>)fixedPicker!.DataContext).ToList();

                unavailableItems.Single(item => item.Id == "R1").IsChecked = true;
                Assert.DoesNotContain("R1", group.Sections[0].FixedRooms);
                Assert.Contains("R1", group.Sections[0].UnavailableRooms);

                fixedItems.Single(item => item.Id == "R2").IsChecked = true;
                Assert.Contains("R2", group.Sections[0].FixedRooms);
                Assert.DoesNotContain("R2", group.Sections[0].UnavailableRooms);

                allButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(group.Sections[0].FixedRooms);
                Assert.Equal(new[] { "R1", "R2" }, group.Sections[0].UnavailableRooms.OrderBy(id => id));

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
