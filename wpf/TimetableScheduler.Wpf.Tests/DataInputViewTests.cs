using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.Wpf;
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
        if (Application.Current != null) return;

        var app = new App
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        app.InitializeComponent();
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
