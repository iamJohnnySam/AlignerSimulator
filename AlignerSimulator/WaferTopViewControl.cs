using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlignerSimulator;

/// <summary>
/// Top-down view of the wafer/frame on the chuck with the sensor position indicated.
/// Shows wafer centre offset, notch, chips, and current rotation angle.
/// </summary>
public sealed class WaferTopViewControl : Control
{
    public static readonly DependencyProperty WaferDiameterProperty =
        DependencyProperty.Register(nameof(WaferDiameter), typeof(double), typeof(WaferTopViewControl),
            new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsTapeFrameProperty =
        DependencyProperty.Register(nameof(IsTapeFrame), typeof(bool), typeof(WaferTopViewControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OffsetXProperty =
        DependencyProperty.Register(nameof(OffsetX), typeof(double), typeof(WaferTopViewControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OffsetYProperty =
        DependencyProperty.Register(nameof(OffsetY), typeof(double), typeof(WaferTopViewControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentAngleProperty =
        DependencyProperty.Register(nameof(CurrentAngle), typeof(double), typeof(WaferTopViewControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NotchStartDegProperty =
        DependencyProperty.Register(nameof(NotchStartDeg), typeof(double), typeof(WaferTopViewControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double WaferDiameter { get => (double)GetValue(WaferDiameterProperty); set => SetValue(WaferDiameterProperty, value); }
    public bool IsTapeFrame { get => (bool)GetValue(IsTapeFrameProperty); set => SetValue(IsTapeFrameProperty, value); }
    public double OffsetX { get => (double)GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
    public double OffsetY { get => (double)GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }
    public double CurrentAngle { get => (double)GetValue(CurrentAngleProperty); set => SetValue(CurrentAngleProperty, value); }
    public double NotchStartDeg { get => (double)GetValue(NotchStartDegProperty); set => SetValue(NotchStartDegProperty, value); }

    private static readonly Pen ChuckPen = new(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 2);
    private static readonly Pen WaferPen = new(new SolidColorBrush(Color.FromRgb(180, 180, 220)), 2);
    private static readonly Pen FramePen = new(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 2);
    private static readonly Pen SensorPen = new(new SolidColorBrush(Color.FromRgb(255, 60, 60)), 2.5);
    private static readonly Pen NotchPen = new(new SolidColorBrush(Color.FromRgb(255, 200, 0)), 2);
    private static readonly Pen AnglePen = new(new SolidColorBrush(Color.FromArgb(120, 100, 255, 100)), 1) { DashStyle = DashStyles.Dot };
    private static readonly Typeface LabelFont = new("Segoe UI");

    static WaferTopViewControl()
    {
        ChuckPen.Freeze(); WaferPen.Freeze(); FramePen.Freeze();
        SensorPen.Freeze(); NotchPen.Freeze(); AnglePen.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 20 || h < 20) return;

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 15, 25)), null, new Rect(0, 0, w, h));

        double cx = w / 2;
        double cy = h / 2;

        double effectiveRadius = IsTapeFrame
            ? WaferGeometry.FrameOuterDiameter(WaferDiameter) / 2.0
            : WaferDiameter / 2.0;

        // Scale factor: fit wafer in view with 15% margin
        double maxRadius = Math.Max(effectiveRadius, WaferDiameter / 2.0) * 1.15;
        double scale = Math.Min(w, h) / 2.0 / maxRadius;

        // Chuck (small circle at centre)
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(50, 50, 50)), ChuckPen,
            new Point(cx, cy), 15, 15);

        // Wafer offset in pixel space
        double oxPx = OffsetX * scale;
        double oyPx = -OffsetY * scale; // Y inverted in screen coords
        double waferCx = cx + oxPx;
        double waferCy = cy + oyPx;
        double waferRadPx = (WaferDiameter / 2.0) * scale;

        // Draw wafer
        var waferBrush = new SolidColorBrush(Color.FromArgb(60, 100, 100, 200));
        dc.DrawEllipse(waferBrush, WaferPen, new Point(waferCx, waferCy), waferRadPx, waferRadPx);

        if (IsTapeFrame)
        {
            // Draw frame ring
            double frameRadPx = effectiveRadius * scale;
            dc.DrawEllipse(null, FramePen, new Point(waferCx, waferCy), frameRadPx, frameRadPx);
        }

        // Notch indicator (small V at wafer edge)
        double notchAngleRad = (NotchStartDeg - CurrentAngle) * Math.PI / 180.0;
        double notchX = waferCx + waferRadPx * Math.Cos(notchAngleRad);
        double notchY = waferCy - waferRadPx * Math.Sin(notchAngleRad);
        dc.DrawEllipse(Brushes.Gold, NotchPen, new Point(notchX, notchY), 5, 5);

        // Current angle indicator (line from centre)
        double angleRad = -CurrentAngle * Math.PI / 180.0;
        double indicatorLen = waferRadPx + 10;
        dc.DrawLine(AnglePen, new Point(waferCx, waferCy),
            new Point(waferCx + indicatorLen * Math.Cos(angleRad),
                      waferCy + indicatorLen * Math.Sin(angleRad)));

        // Sensor position (red line at 0° / positive X, showing the sensor span)
        double sensorCentre = effectiveRadius * scale;
        double sensorHalf = (WaferGeometry.SensorLength / 2.0) * scale;
        double sensorX = cx + sensorCentre;
        dc.DrawLine(SensorPen,
            new Point(sensorX - sensorHalf, cy - 6),
            new Point(sensorX + sensorHalf, cy - 6));
        dc.DrawLine(SensorPen,
            new Point(sensorX - sensorHalf, cy + 6),
            new Point(sensorX + sensorHalf, cy + 6));
        dc.DrawLine(SensorPen,
            new Point(sensorX - sensorHalf, cy - 6),
            new Point(sensorX - sensorHalf, cy + 6));
        dc.DrawLine(SensorPen,
            new Point(sensorX + sensorHalf, cy - 6),
            new Point(sensorX + sensorHalf, cy + 6));

        // "SENSOR" label
        var sensorLabel = new FormattedText("SENSOR", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelFont, 9, Brushes.Red, 1.0);
        dc.DrawText(sensorLabel, new Point(sensorX - sensorLabel.Width / 2, cy + 8));

        // Labels
        var labelBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        var angleLabel = new FormattedText($"θ = {CurrentAngle:F1}°", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelFont, 11, labelBrush, 1.0);
        dc.DrawText(angleLabel, new Point(4, 4));

        string typeLabel = IsTapeFrame ? "Tape Frame" : "Wafer";
        var tLabel = new FormattedText($"{typeLabel} ⌀{WaferDiameter:F0}mm", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelFont, 11, labelBrush, 1.0);
        dc.DrawText(tLabel, new Point(4, 18));
    }
}
