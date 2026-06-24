using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Tests;

public class TimetableZoomTests
{
    [Fact]
    public void Zoom_ChangesByTenPercentAndRespectsBounds()
    {
        var zoom = new TimetableZoom();

        zoom.ZoomOut();
        Assert.Equal(0.9, zoom.Scale);
        Assert.Equal("90%", zoom.DisplayPercent);

        for (var i = 0; i < 10; i++) zoom.ZoomOut();
        Assert.Equal(TimetableZoom.MinimumScale, zoom.Scale);

        for (var i = 0; i < 20; i++) zoom.ZoomIn();
        Assert.Equal(TimetableZoom.MaximumScale, zoom.Scale);

        zoom.Reset();
        Assert.Equal(1.0, zoom.Scale);
        Assert.Equal("100%", zoom.DisplayPercent);
    }
}
