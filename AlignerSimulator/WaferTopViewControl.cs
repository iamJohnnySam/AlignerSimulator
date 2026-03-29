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

    public static readonly DependencyProperty WaferContourProperty =
        DependencyProperty.Register(nameof(WaferContour), typeof(double[]), typeof(WaferTopViewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsNotchModeProperty =
        DependencyProperty.Register(nameof(IsNotchMode), typeof(bool), typeof(WaferTopViewControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FrameNotchesProperty =
        DependencyProperty.Register(nameof(FrameNotches), typeof(FrameNotch[]), typeof(WaferTopViewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double WaferDiameter { get => (double)GetValue(WaferDiameterProperty); set => SetValue(WaferDiameterProperty, value); }
    public bool IsTapeFrame { get => (bool)GetValue(IsTapeFrameProperty); set => SetValue(IsTapeFrameProperty, value); }
    public double OffsetX { get => (double)GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
    public double OffsetY { get => (double)GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }
    public double CurrentAngle { get => (double)GetValue(CurrentAngleProperty); set => SetValue(CurrentAngleProperty, value); }
    public double NotchStartDeg { get => (double)GetValue(NotchStartDegProperty); set => SetValue(NotchStartDegProperty, value); }
    public double[]? WaferContour { get => (double[]?)GetValue(WaferContourProperty); set => SetValue(WaferContourProperty, value); }
    public bool IsNotchMode { get => (bool)GetValue(IsNotchModeProperty); set => SetValue(IsNotchModeProperty, value); }
    public FrameNotch[]? FrameNotches { get => (FrameNotch[]?)GetValue(FrameNotchesProperty); set => SetValue(FrameNotchesProperty, value); }

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

        var bgBrush = new SolidColorBrush(Color.FromRgb(15, 15, 25));
        dc.DrawRectangle(bgBrush, null, new Rect(0, 0, w, h));

        double cx = w / 2;
        double cy = h / 2;

        double effectiveRadius = IsTapeFrame
            ? WaferGeometry.FrameOuterDiameter(WaferDiameter) / 2.0
            : WaferDiameter / 2.0;

        double maxRadius = effectiveRadius * 1.15;
        double scale = Math.Min(w, h) / 2.0 / maxRadius;

        // Chuck centre
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(50, 50, 50)), ChuckPen,
            new Point(cx, cy), 15, 15);

        double oxPx = OffsetX * scale;
        double oyPx = -OffsetY * scale;
        double waferCx = cx + oxPx;
        double waferCy = cy + oyPx;

        if (IsTapeFrame)
        {
            var spec = WaferGeometry.GetTapeFrameSpec(WaferDiameter);

            // Outer contour (rounded square: 4 flats + arc corners)
            var outerGeom = new StreamGeometry();
            using (var ctx = outerGeom.Open())
            {
                const int segments = 720;
                bool first = true;
                for (int i = 0; i <= segments; i++)
                {
                    double framePhi = 360.0 * i / segments;
                    double r = WaferGeometry.ComputeFrameEdgeRadius(framePhi, spec, FrameNotches);
                    double screenRad = (framePhi - CurrentAngle) * Math.PI / 180.0;
                    double px = waferCx + r * scale * Math.Cos(screenRad);
                    double py = waferCy - r * scale * Math.Sin(screenRad);
                    if (first) { ctx.BeginFigure(new Point(px, py), true, true); first = false; }
                    else ctx.LineTo(new Point(px, py), true, false);
                }
            }
            outerGeom.Freeze();
            var frameFill = new SolidColorBrush(Color.FromArgb(80, 160, 160, 160));
            dc.DrawGeometry(frameFill, FramePen, outerGeom);

            // Inner circle (frame opening)
            double innerRPx = spec.InnerRadiusMm * scale;
            dc.DrawEllipse(bgBrush, new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1),
                new Point(waferCx, waferCy), innerRPx, innerRPx);

            // Flat labels at 0, 90, 180, 270 deg (rotate with frame)
            var featureBrush = new SolidColorBrush(Color.FromRgb(200, 200, 100));
            double labelR = (spec.ArcRadiusMm + 6) * scale;
            for (int i = 0; i < 4; i++)
            {
                double deg = i * 90.0;
                double screenRad = (deg - CurrentAngle) * Math.PI / 180.0;
                double lx = waferCx + labelR * Math.Cos(screenRad);
                double ly = waferCy - labelR * Math.Sin(screenRad);
                var ft = new FormattedText("F", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, LabelFont, 10, featureBrush, 1.0);
                dc.DrawText(ft, new Point(lx - ft.Width / 2, ly - ft.Height / 2));
            }
        }
        else
        {
            double waferRadPx = (WaferDiameter / 2.0) * scale;
            var waferBrush = new SolidColorBrush(Color.FromArgb(60, 100, 100, 200));
            var contour = WaferContour;

            if (contour != null && contour.Length > 2)
            {
                // Shaped wafer contour (notch, flats, chips visible)
                var waferGeom = new StreamGeometry();
                using (var ctx = waferGeom.Open())
                {
                    int N = contour.Length;
                    bool first = true;
                    for (int i = 0; i <= N; i++)
                    {
                        int idx = i % N;
                        double waferAngleDeg = 360.0 * idx / N;
                        double r = contour[idx] * scale;
                        double screenRad = (waferAngleDeg - CurrentAngle) * Math.PI / 180.0;
                        double px = waferCx + r * Math.Cos(screenRad);
                        double py = waferCy - r * Math.Sin(screenRad);
                        if (first) { ctx.BeginFigure(new Point(px, py), true, true); first = false; }
                        else ctx.LineTo(new Point(px, py), true, false);
                    }
                }
                waferGeom.Freeze();
                dc.DrawGeometry(waferBrush, WaferPen, waferGeom);
            }
            else
            {
                dc.DrawEllipse(waferBrush, WaferPen, new Point(waferCx, waferCy), waferRadPx, waferRadPx);
            }

            // Notch indicator (only in notch mode)
            if (IsNotchMode)
            {
                double notchAngleRad = (NotchStartDeg - CurrentAngle) * Math.PI / 180.0;
                double notchX = waferCx + waferRadPx * Math.Cos(notchAngleRad);
                double notchY = waferCy - waferRadPx * Math.Sin(notchAngleRad);
                dc.DrawEllipse(Brushes.Gold, NotchPen, new Point(notchX, notchY), 5, 5);
            }
        }

        // Angle indicator
        double aiRad = -CurrentAngle * Math.PI / 180.0;
        double aiLen = effectiveRadius * scale + 10;
        dc.DrawLine(AnglePen, new Point(waferCx, waferCy),
            new Point(waferCx + aiLen * Math.Cos(aiRad),
                      waferCy + aiLen * Math.Sin(aiRad)));

        // Sensor position
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
