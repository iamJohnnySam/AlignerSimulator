using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlignerSimulator;

/// <summary>
/// Renders the sensor data as a chart (angle vs sensor value) with grid lines.
/// Also draws a vertical indicator for the current manual angle.
/// </summary>
public sealed class SensorChartControl : Control
{
    public static readonly DependencyProperty SensorHistoryProperty =
        DependencyProperty.Register(nameof(SensorHistory), typeof(double[]), typeof(SensorChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AngleHistoryProperty =
        DependencyProperty.Register(nameof(AngleHistory), typeof(double[]), typeof(SensorChartControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentAngleProperty =
        DependencyProperty.Register(nameof(CurrentAngle), typeof(double), typeof(SensorChartControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentValueProperty =
        DependencyProperty.Register(nameof(CurrentValue), typeof(double), typeof(SensorChartControl),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[] SensorHistory
    {
        get => (double[])GetValue(SensorHistoryProperty);
        set => SetValue(SensorHistoryProperty, value);
    }

    public double[] AngleHistory
    {
        get => (double[])GetValue(AngleHistoryProperty);
        set => SetValue(AngleHistoryProperty, value);
    }

    public double CurrentAngle
    {
        get => (double)GetValue(CurrentAngleProperty);
        set => SetValue(CurrentAngleProperty, value);
    }

    public double CurrentValue
    {
        get => (double)GetValue(CurrentValueProperty);
        set => SetValue(CurrentValueProperty, value);
    }

    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
    private static readonly Pen DataPen = new(new SolidColorBrush(Color.FromRgb(0, 200, 255)), 1.5);
    private static readonly Pen MidlinePen = new(new SolidColorBrush(Color.FromArgb(80, 255, 200, 0)), 1) { DashStyle = DashStyles.Dash };
    private static readonly Pen CursorPen = new(new SolidColorBrush(Color.FromRgb(255, 80, 80)), 1.5) { DashStyle = DashStyles.Dash };
    private static readonly Typeface LabelFont = new("Segoe UI");

    static SensorChartControl()
    {
        GridPen.Freeze();
        DataPen.Freeze();
        MidlinePen.Freeze();
        CursorPen.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 10 || h < 10) return;

        // Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 20, 30)), null, new Rect(0, 0, w, h));

        const double margin = 45;
        double plotLeft = margin;
        double plotTop = 10;
        double plotRight = w - 15;
        double plotBottom = h - 30;
        double plotW = plotRight - plotLeft;
        double plotH = plotBottom - plotTop;

        if (plotW < 10 || plotH < 10) return;

        const double yMin = 0;
        const double yMax = 28;

        // Grid lines and Y labels
        var labelBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        for (double yVal = 0; yVal <= 28; yVal += 4)
        {
            double y = plotBottom - (yVal - yMin) / (yMax - yMin) * plotH;
            dc.DrawLine(GridPen, new Point(plotLeft, y), new Point(plotRight, y));

            var text = new FormattedText($"{yVal:F0}", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, LabelFont, 10, labelBrush, 1.0);
            dc.DrawText(text, new Point(plotLeft - text.Width - 4, y - text.Height / 2));
        }

        // X grid lines and labels (angle)
        for (double xVal = 0; xVal <= 360; xVal += 45)
        {
            double x = plotLeft + xVal / 360.0 * plotW;
            dc.DrawLine(GridPen, new Point(x, plotTop), new Point(x, plotBottom));

            var text = new FormattedText($"{xVal:F0}°", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, LabelFont, 10, labelBrush, 1.0);
            dc.DrawText(text, new Point(x - text.Width / 2, plotBottom + 4));
        }

        // Midline at 14 mm
        double midY = plotBottom - (14.0 - yMin) / (yMax - yMin) * plotH;
        dc.DrawLine(MidlinePen, new Point(plotLeft, midY), new Point(plotRight, midY));

        // Y axis label
        var yLabel = new FormattedText("Sensor (mm)", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelFont, 10, labelBrush, 1.0);
        dc.PushTransform(new RotateTransform(-90, 8, plotTop + plotH / 2));
        dc.DrawText(yLabel, new Point(8 - yLabel.Width / 2, plotTop + plotH / 2 - yLabel.Height / 2));
        dc.Pop();

        // Data line
        var angles = AngleHistory;
        var values = SensorHistory;
        if (angles != null && values != null && angles.Length > 1 && angles.Length == values.Length)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                bool started = false;
                for (int i = 0; i < angles.Length; i++)
                {
                    double val = values[i];
                    if (double.IsNaN(val)) continue;

                    double x = plotLeft + angles[i] / 360.0 * plotW;
                    double y = plotBottom - (val - yMin) / (yMax - yMin) * plotH;
                    y = Math.Clamp(y, plotTop, plotBottom);

                    if (!started) { ctx.BeginFigure(new Point(x, y), false, false); started = true; }
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, DataPen, geometry);
        }

        // Current angle cursor
        double cursorX = plotLeft + CurrentAngle / 360.0 * plotW;
        dc.DrawLine(CursorPen, new Point(cursorX, plotTop), new Point(cursorX, plotBottom));

        // Current value dot
        double cursorY = plotBottom - (Math.Clamp(CurrentValue, yMin, yMax) - yMin) / (yMax - yMin) * plotH;
        dc.DrawEllipse(new SolidColorBrush(Colors.Red), null, new Point(cursorX, cursorY), 4, 4);

        // Value readout
        var readout = new FormattedText($"{CurrentValue:F3} mm", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelFont, 11, Brushes.White, 1.0);
        dc.DrawText(readout, new Point(Math.Min(cursorX + 6, plotRight - readout.Width - 2), cursorY - readout.Height - 2));
    }
}
