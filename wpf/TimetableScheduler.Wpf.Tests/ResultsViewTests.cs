using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.Wpf;
using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Tests;

public class ResultsViewTests
{
    [Fact]
    public void TimetableGridChip_ShowsCourseTypeProfessorAndRoomAfterRender()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();

            var vm = new TimetableGridViewModel();
            vm.Render(
                new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
                new[]
                {
                    new Course
                    {
                        Id = "C-01",
                        Name = "알고리즘",
                        Grade = 2,
                        Section = 1,
                        HoursPerWeek = 1,
                        CourseType = "전필",
                        ProfessorId = "P1",
                    },
                },
                professors: new[] { new Professor { Id = "P1", Name = "김교수" } },
                rooms: new[] { new Room { Id = "R1", Name = "공학관 101" } });

            var view = new TimetableGridControl { DataContext = vm };
            var window = new Window
            {
                Width = 900,
                Height = 700,
                Content = view,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };

            window.Show();
            view.UpdateLayout();

            var texts = FindDescendants<TextBlock>(view)
                .Select(text => NormalizeDisplayText(text.Text))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            Assert.Contains("알고리즘 - A", texts);
            Assert.Contains("김교수", texts);
            Assert.Contains("공학관 101", texts);

            window.Close();
        });
    }

    [Fact]
    public void TimetableGridChip_RendersSameSlotAssignmentsSideBySide()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();

            var vm = new TimetableGridViewModel();
            vm.Render(
                new[]
                {
                    new SolutionAssignment("C-01", 0, 1, "R1"),
                    new SolutionAssignment("C-02", 0, 1, "R2"),
                },
                new[]
                {
                    new Course { Id = "C-01", Name = "알고리즘", Grade = 2, Section = 1, HoursPerWeek = 1 },
                    new Course { Id = "C-02", Name = "자료구조", Grade = 2, Section = 2, HoursPerWeek = 1 },
                },
                rooms: new[]
                {
                    new Room { Id = "R1", Name = "101호" },
                    new Room { Id = "R2", Name = "102호" },
                });

            var view = ShowGrid(vm);
            try
            {
                var bodyGrid = FindDescendants<Grid>(view)
                    .Single(grid => grid.Name == "BodyGrid");
                var assignmentBorders = FindDescendants<Border>(bodyGrid)
                    .Where(border => border.Child is Border)
                    .Where(border => Grid.GetRow(border) == 0)
                    .ToList();

                Assert.True(bodyGrid.ColumnDefinitions.Count >= 8);
                Assert.Equal(new[] { 2, 3 }, assignmentBorders.Select(Grid.GetColumn).Order().ToArray());
                Assert.All(assignmentBorders, border =>
                {
                    Assert.Equal(HorizontalAlignment.Stretch, border.HorizontalAlignment);
                    Assert.Equal(VerticalAlignment.Stretch, border.VerticalAlignment);
                });
            }
            finally
            {
                Window.GetWindow(view)?.Close();
            }
        });
    }

    [Fact]
    public void TimetableGridChip_PreservesMultiHourRowSpan()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();

            var vm = new TimetableGridViewModel();
            vm.Render(
                new[]
                {
                    new SolutionAssignment("TWO", 0, 1, "R1"),
                    new SolutionAssignment("TWO", 0, 2, "R1"),
                    new SolutionAssignment("THREE", 1, 6, "R2"),
                    new SolutionAssignment("THREE", 1, 7, "R2"),
                    new SolutionAssignment("THREE", 1, 8, "R2"),
                },
                new[]
                {
                    new Course { Id = "TWO", Name = "2시간수업", Grade = 2, Section = 1, HoursPerWeek = 2 },
                    new Course { Id = "THREE", Name = "3시간수업", Grade = 3, Section = 1, HoursPerWeek = 3 },
                },
                rooms: new[]
                {
                    new Room { Id = "R1", Name = "101호" },
                    new Room { Id = "R2", Name = "102호" },
                });

            var view = ShowGrid(vm);
            try
            {
                var rowSpans = FindDescendants<Border>(view)
                    .Select(Grid.GetRowSpan)
                    .ToList();

                Assert.Contains(2, rowSpans);
                Assert.Contains(3, rowSpans);
            }
            finally
            {
                Window.GetWindow(view)?.Close();
            }
        });
    }

    [Fact]
    public void TimetableGrid_ShowsNightSeparatorBeforePeriod10()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();

            var vm = new TimetableGridViewModel();
            vm.Render(
                new[] { new SolutionAssignment("NIGHT", 0, 10, "R1") },
                new[]
                {
                    new Course
                    {
                        Id = "NIGHT",
                        Name = "야간수업",
                        Grade = AcademicLevels.GraduateGrade,
                        HoursPerWeek = 1,
                        ProfessorId = "P1",
                    },
                },
                rooms: new[] { new Room { Id = "R1", Name = "101호" } });

            var view = ShowGrid(vm);
            try
            {
                var bodyGrid = FindDescendants<Grid>(view)
                    .Single(grid => grid.Name == "BodyGrid");
                var periodTenLabel = FindDescendants<Border>(bodyGrid)
                    .Single(border => border.Child is TextBlock { Text: "10" });
                var nightAssignment = FindDescendants<Border>(bodyGrid)
                    .Single(border => border.Child is Border && Grid.GetRow(border) == 9);

                var nightSeparator = Assert.Single(FindDescendants<Border>(bodyGrid)
                    .Where(border =>
                        !border.IsHitTestVisible
                        && Grid.GetRow(border) == TimetableGridControl.NightSeparatorRow
                        && border.BorderThickness.Top == 3));

                Assert.Equal(Constants.Periods.Count, bodyGrid.RowDefinitions.Count);
                Assert.DoesNotContain(FindDescendants<TextBlock>(bodyGrid), text => text.Text == "야 간 수 업");
                Assert.Equal(new Thickness(0, 3, 0, 0), nightSeparator.BorderThickness);
                Assert.Equal(9, Grid.GetRow(periodTenLabel));
                Assert.Equal(9, Grid.GetRow(nightAssignment));
            }
            finally
            {
                Window.GetWindow(view)?.Close();
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

    private static TimetableGridControl ShowGrid(TimetableGridViewModel vm)
    {
        var view = new TimetableGridControl { DataContext = vm };
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = view,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

        window.Show();
        view.UpdateLayout();
        return view;
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

    private static string NormalizeDisplayText(string text) =>
        text.Replace("\u200B", "");
}
