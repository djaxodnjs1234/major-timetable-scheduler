using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TimetableScheduler.Wpf.Controls;

public sealed class TimetableZoom : INotifyPropertyChanged
{
    public const double MinimumScale = 0.5;
    public const double MaximumScale = 2.0;
    private const double Step = 0.1;

    public static TimetableZoom Shared { get; } = new();

    private double _scale = 1.0;

    public double Scale
    {
        get => _scale;
        private set
        {
            if (Math.Abs(_scale - value) < double.Epsilon) return;
            _scale = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayPercent));
        }
    }

    public string DisplayPercent => $"{Scale * 100:0}%";

    public void ZoomIn() => SetScale(Scale + Step);

    public void ZoomOut() => SetScale(Scale - Step);

    public void Reset() => SetScale(1.0);

    private void SetScale(double scale) =>
        Scale = Math.Clamp(Math.Round(scale, 1, MidpointRounding.AwayFromZero), MinimumScale, MaximumScale);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
