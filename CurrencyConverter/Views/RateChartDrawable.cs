using CurrencyConverter.Models;
using Microsoft.Maui.Graphics;

namespace CurrencyConverter.Views;

public sealed class RateChartDrawable : IDrawable
{
    public IReadOnlyList<RateObservation> Observations { get; set; } = Array.Empty<RateObservation>();
    public string EmptyMessage { get; set; } = "Loading historical rates…";

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Colors.White;
        canvas.FillRectangle(dirtyRect);

        const float left = 8;
        const float right = 8;
        const float top = 12;
        const float bottom = 12;
        var width = Math.Max(0, dirtyRect.Width - left - right);
        var height = Math.Max(0, dirtyRect.Height - top - bottom);
        if (width == 0 || height == 0) return;

        canvas.StrokeSize = 1;
        canvas.StrokeColor = Color.FromArgb("#E4E9F1");
        for (var line = 0; line < 3; line++)
        {
            var y = top + height * line / 2;
            canvas.DrawLine(left, y, left + width, y);
        }

        if (Observations.Count == 0)
        {
            canvas.FontColor = Color.FromArgb("#68758A");
            canvas.FontSize = 13;
            canvas.DrawString(
                EmptyMessage,
                left + 8,
                top + height / 2 - 10,
                Math.Max(0, width - 16),
                20,
                HorizontalAlignment.Center,
                VerticalAlignment.Center);
            return;
        }

        var minimum = Observations.Min(point => point.Rate);
        var maximum = Observations.Max(point => point.Rate);
        var range = maximum - minimum;
        var padding = range == 0 ? Math.Abs(maximum) * 0.01 : range * 0.1;
        minimum -= padding;
        maximum += padding;
        range = maximum - minimum;

        var points = Observations
            .OrderBy(point => point.Timestamp)
            .Select(point =>
            {
                var totalSeconds = Math.Max(
                    1,
                    (Observations[^1].Timestamp - Observations[0].Timestamp).TotalSeconds);
                var elapsedSeconds = (point.Timestamp - Observations[0].Timestamp).TotalSeconds;
                var x = left + (Observations.Count == 1 ? width / 2 : width * (float)(elapsedSeconds / totalSeconds));
                var y = top + height - (float)((point.Rate - minimum) / range) * height;
                return new PointF(x, y);
            })
            .ToArray();

        canvas.StrokeColor = Color.FromArgb("#315FDB");
        canvas.StrokeSize = 3;
        if (points.Length == 1)
        {
            canvas.FillColor = Color.FromArgb("#315FDB");
            canvas.FillCircle(points[0], 5);
            return;
        }

        for (var index = 1; index < points.Length; index++)
            canvas.DrawLine(points[index - 1], points[index]);

        canvas.FillColor = Color.FromArgb("#315FDB");
        foreach (var point in points)
            canvas.FillCircle(point, 3.5f);
    }
}
