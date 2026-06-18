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

public class UnifiedTimetableControlIdentityTests
{
    [Fact]
    public void SelectingB_AfterSelectingA_RemovesPreviousSelectionVisual()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();
            var vm = BuildDuplicateCourseViewModel();
            var view = ShowUnifiedGrid(vm);
            try
            {
                var a = vm.Cells.Single(c => c.Assignment.AssignmentId == "A");
                var b = vm.Cells.Single(c => c.Assignment.AssignmentId == "B");

                vm.SetEditState(new UnifiedCellKey(a.Day, a.Period, a.Grade, a.SubColumnIdx), new Dictionary<UnifiedCellKey, EditCellState>());
                view.UpdateLayout();
                Assert.Single(SelectedOverlays(view));

                vm.SetEditState(new UnifiedCellKey(b.Day, b.Period, b.Grade, b.SubColumnIdx), new Dictionary<UnifiedCellKey, EditCellState>());
                view.UpdateLayout();

                var overlay = Assert.Single(SelectedOverlays(view));
                Assert.Equal(Grid.GetRow(AssignmentBorder(view, "B")), Grid.GetRow(overlay));
                Assert.Equal(Grid.GetColumn(AssignmentBorder(view, "B")), Grid.GetColumn(overlay));
            }
            finally
            {
                Window.GetWindow(view)?.Close();
            }
        });
    }

    [Fact]
    public void Rebuild_RemovesStaleDragSourceAndHoverBadge()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();
            var vm = BuildDuplicateCourseViewModel();
            var view = ShowUnifiedGrid(vm);
            try
            {
                view.EnableCrossHover = true;
                view.CrossHoverEvaluator = _ => CrossHoverState.Available();
                Assert.True(view.ShowHoverBadgeForTests("A", cross: true, swap: false));
                Assert.True(view.HasActiveHoverBadgeForTests);
                Assert.NotEmpty(HoverBadges(view));

                var stale = new UnifiedTimetableControl.CellClickedEventArgs(
                    0,
                    1,
                    2,
                    0,
                    vm.Cells.Single(c => c.Assignment.AssignmentId == "A").Assignment);
                view.SetDragSourceForTests(stale);
                Assert.True(view.HasDragSourceForTests);

                vm.Render(
                    new[] { new SolutionAssignment("C-01", 1, 1, "R1", "B") },
                    Courses(),
                    Professors(),
                    Rooms());
                view.UpdateLayout();

                Assert.False(view.HasDragSourceForTests);
                Assert.False(view.HasActiveHoverBadgeForTests);
                Assert.Empty(HoverBadges(view));
            }
            finally
            {
                Window.GetWindow(view)?.Close();
            }
        });
    }

    [Fact]
    public void CrossBadgeClick_ClearsHoverBadgeAfterRequest()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();
            var vm = BuildDuplicateCourseViewModel();
            var view = ShowUnifiedGrid(vm);
            try
            {
                view.EnableCrossHover = true;
                view.CrossHoverEvaluator = _ => CrossHoverState.Available();
                UnifiedTimetableControl.CellClickedEventArgs? requested = null;
                view.CrossAddRequested += (_, args) => requested = args;

                Assert.True(view.ShowHoverBadgeForTests("B", cross: true, swap: false));
                var badge = Assert.Single(HoverBadges(view).Where(b => Equals(b.Content, "+")));
                badge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                view.UpdateLayout();

                Assert.NotNull(requested);
                Assert.Equal("B", requested!.Assignment?.AssignmentId);
                Assert.False(view.HasActiveHoverBadgeForTests);
                Assert.Empty(HoverBadges(view));
            }
            finally
            {
                Window.GetWindow(view)?.Close();
            }
        });
    }

    [Fact]
    public void SwapBadgeClick_ClearsHoverBadgeAfterRequest()
    {
        RunSta(() =>
        {
            EnsureApplicationResources();
            var vm = BuildDuplicateCourseViewModel();
            var view = ShowUnifiedGrid(vm);
            try
            {
                view.EnableSwapHover = true;
                view.SwapHoverEvaluator = _ => SwapHoverState.Available();
                UnifiedTimetableControl.CellClickedEventArgs? requested = null;
                view.SwapRequested += (_, args) => requested = args;

                Assert.True(view.ShowHoverBadgeForTests("B", cross: false, swap: true));
                var badge = Assert.Single(HoverBadges(view).Where(b => Equals(b.Content, "⇄")));
                badge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                view.UpdateLayout();

                Assert.NotNull(requested);
                Assert.Equal("B", requested!.Assignment?.AssignmentId);
                Assert.False(view.HasActiveHoverBadgeForTests);
                Assert.Empty(HoverBadges(view));
            }
            finally
            {
                Window.GetWindow(view)?.Close();
            }
        });
    }

    private static UnifiedTimetableViewModel BuildDuplicateCourseViewModel()
    {
        var vm = new UnifiedTimetableViewModel();
        vm.Render(
            new[]
            {
                new SolutionAssignment("C-01", 0, 1, "R1", "A"),
                new SolutionAssignment("C-01", 0, 2, "R2", "B"),
            },
            Courses(),
            Professors(),
            Rooms());
        return vm;
    }

    private static Course[] Courses() =>
        new[]
        {
            new Course
            {
                Id = "C-01",
                Name = "프로그래밍응용",
                Grade = 2,
                Section = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
            },
        };

    private static Professor[] Professors() =>
        new[] { new Professor { Id = "P1", Name = "김교수" } };

    private static Room[] Rooms() =>
        new[]
        {
            new Room { Id = "R1", Name = "101호" },
            new Room { Id = "R2", Name = "102호" },
        };

    private static UnifiedTimetableControl ShowUnifiedGrid(UnifiedTimetableViewModel vm)
    {
        var view = new UnifiedTimetableControl { DataContext = vm };
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

    private static Border AssignmentBorder(DependencyObject root, string assignmentId) =>
        FindDescendants<Border>(root)
            .Single(border =>
                border.Tag is ValueTuple<int, int, int, int, CellAssignment?> tag
                && tag.Item5?.AssignmentId == assignmentId);

    private static List<Border> SelectedOverlays(DependencyObject root) =>
        FindDescendants<Border>(root)
            .Where(border =>
                !border.IsHitTestVisible
                && border.BorderThickness == new Thickness(2))
            .ToList();

    private static List<Button> HoverBadges(DependencyObject root) =>
        FindDescendants<Button>(root)
            .Where(button => Equals(button.Content, "+") || Equals(button.Content, "⇄"))
            .ToList();

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
