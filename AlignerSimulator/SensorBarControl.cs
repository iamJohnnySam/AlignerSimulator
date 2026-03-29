using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlignerSimulator;

/// <summary>
/// Vertical bar showing the 0–28 mm sensor range with the current edge position highlighted.
/// Mimics the physical Keyence IG-028 sensor output.
/// </summary>
public sealed class SensorBarControl : Control
{
    public static readonly DependencyProperty SensorValueProperty =
        DependencyProperty.Register(nameof(SensorValue), typeof(double), typeof(SensorBarControl),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double SensorValue
    {
        get => (double)GetValue(SensorValueProperty);
        set => SetValue(SensorValueProperty, value);
    }

    private static readonly Typeface LabelFont = new("Segoe UI");

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 10 || h < 20) return;

        // Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(10, 10, 15)), null, new Rect(0, 0, w, h));

        const double margin = 8;
        double barLeft = margin;
        double barRight = w - margin;
        double barTop = 25;
        double barBottom = h - 15;
        double barW = barRight - barLeft;
        double barH = barBottom - barTop;

        // Sensor bar background (0 at bottom, 28 at top)
        var gradient = new LinearGradientBrush(
            Color.FromRgb(20, 30, 50), Color.FromRgb(40, 60, 100), 90);
        dc.DrawRectangle(gradient, new Pen(Brushes.Gray, 1), new Rect(barLeft, barTop, barW, barH));

        // Tick marks
        var tickBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
        var tickPen = new Pen(tickBrush, 0.5);
        for (double mm = 0; mm <= 28; mm += 2)
        {
            double y = barBottom - (mm / 28.0) * barH;
            dc.DrawLine(tickPen, new Point(barLeft, y), new Point(barLeft + 4, y));
            dc.DrawLine(tickPen, new Point(barRight - 4, y), new Point(barRight, y));

            if (mm % 4 == 0)
            {
                var text = new FormattedText($"{mm:F0}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, LabelFont, 8, tickBrush, 1.0);
                dc.DrawText(text, new Point(barRight + 2, y - text.Height / 2));
            }
        }

        // Midline at 14 mm
        double midY = barBottom - (14.0 / 28.0) * barH;
        var midPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 200, 0)), 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(midPen, new Point(barLeft, midY), new Point(barRight, midY));

        // Current value indicator
        double val = Math.Clamp(SensorValue, 0, 28);
        double valY = barBottom - (val / 28.0) * barH;
        bool inRange = SensorValue >= 0 && SensorValue <= 28;
        var indicatorBrush = inRange
            ? new SolidColorBrush(Color.FromRgb(0, 220, 100))
            : new SolidColorBrush(Colors.Red);

        dc.DrawLine(new Pen(indicatorBrush, 2), new Point(barLeft, valY), new Point(barRight, valY));
        dc.DrawEllipse(indicatorBrush, null, new Point(barLeft + barW / 2, valY), 4, 4);

        // Title
        var title = new FormattedText("IG-028", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelFont, 10, Brushes.White, 1.0);
        dc.DrawText(title, new Point(w / 2 - title.Width / 2, 2));

        // Value readout at bottom
        var readout = new FormattedText($"{SensorValue:F3} mm", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelFont, 10, indicatorBrush, 1.0);
        dc.DrawText(readout, new Point(w / 2 - readout.Width / 2, barBottom + 2));
    }
}
