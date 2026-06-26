using System.Windows;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Wpf;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _subscribedVm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.BackConfirmationRequested -= OnBackConfirmationRequested;

        _subscribedVm = e.NewValue as MainWindowViewModel;
        if (_subscribedVm != null)
            _subscribedVm.BackConfirmationRequested += OnBackConfirmationRequested;
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
}
