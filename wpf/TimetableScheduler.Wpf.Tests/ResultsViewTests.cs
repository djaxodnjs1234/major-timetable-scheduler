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
                .Select(text => text.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            Assert.Contains("알고리즘·A", texts);
            Assert.Contains("전필 · 김교수", texts);
            Assert.Contains("공학관 101", texts);

            window.Close();
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
