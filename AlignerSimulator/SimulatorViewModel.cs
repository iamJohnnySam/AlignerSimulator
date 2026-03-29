using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AlignerSimulator;

public sealed class ChipDefinition : INotifyPropertyChanged
{
    private double _angleDeg = 90;
    private double _depthMm = 0.5;
    private double _widthDeg = 2.0;

    public double AngleDeg { get => _angleDeg; set { _angleDeg = value; OnPropertyChanged(); } }
    public double DepthMm { get => _depthMm; set { _depthMm = value; OnPropertyChanged(); } }
    public double WidthDeg { get => _widthDeg; set { _widthDeg = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class SimulatorViewModel : INotifyPropertyChanged
{
    // ── Backing fields ──────────────────────────────────────────────
    private bool _isTapeFrame;
    private double _waferDiameter = 200.0;
    private double _notchStartDeg;
    private double _notchDepthMm = 1.0;
    private double _notchWidthDeg = 5.0;
    private double _noiseMm = 0.02;
    private double _bowMm;
    private double _potatoChipMm;
    private double _offsetX;
    private double _offsetY;
    private double _chuckSpeedRpm = 60;
    private double _samplingRateHz = 1000;
    private double _manualAngleDeg;
    private bool _isRunning;
    private int _exportRotations = 1;

    // Live display data
    private double _currentSensorValue;
    private double[] _sensorHistory = Array.Empty<double>();
    private double[] _angleHistory = Array.Empty<double>();

    private readonly DispatcherTimer _timer = new();
    private readonly Random _rng = new();
    private DateTime _runStartTime;
    private double _runStartAngle;

    // For export buffer
    private readonly List<(double Angle, double Value)> _exportBuffer = new();

    public SimulatorViewModel()
    {
        _timer.Tick += Timer_Tick;
        UpdateTimerInterval();

        StartStopCommand = new RelayCommand(_ => ToggleRunning());
        ExportCsvCommand = new RelayCommand(_ => ExportCsv());
        AddChipCommand = new RelayCommand(_ => Chips.Add(new ChipDefinition()));
        RemoveChipCommand = new RelayCommand(p => { if (p is ChipDefinition c) Chips.Remove(c); });

        // Fill initial history
        RecalculateFullRotation();
    }

    // ── Properties ──────────────────────────────────────────────────

    public bool IsTapeFrame
    {
        get => _isTapeFrame;
        set { _isTapeFrame = value; OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double WaferDiameter
    {
        get => _waferDiameter;
        set { _waferDiameter = Math.Clamp(value, 50, 450); OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double NotchStartDeg
    {
        get => _notchStartDeg;
        set { _notchStartDeg = value % 360; OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double NotchDepthMm
    {
        get => _notchDepthMm;
        set { _notchDepthMm = Math.Max(0, value); OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double NotchWidthDeg
    {
        get => _notchWidthDeg;
        set { _notchWidthDeg = Math.Clamp(value, 0, 30); OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double NoiseMm
    {
        get => _noiseMm;
        set { _noiseMm = Math.Max(0, value); OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double BowMm
    {
        get => _bowMm;
        set { _bowMm = value; OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double PotatoChipMm
    {
        get => _potatoChipMm;
        set { _potatoChipMm = value; OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double OffsetX
    {
        get => _offsetX;
        set { _offsetX = value; OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double OffsetY
    {
        get => _offsetY;
        set { _offsetY = value; OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double ChuckSpeedRpm
    {
        get => _chuckSpeedRpm;
        set { _chuckSpeedRpm = Math.Clamp(value, 1, 6000); OnPropertyChanged(); UpdateTimerInterval(); }
    }

    public double SamplingRateHz
    {
        get => _samplingRateHz;
        set { _samplingRateHz = Math.Clamp(value, 10, 100000); OnPropertyChanged(); UpdateTimerInterval(); RecalculateFullRotation(); }
    }

    public double ManualAngleDeg
    {
        get => _manualAngleDeg;
        set
        {
            _manualAngleDeg = ((value % 360) + 360) % 360;
            OnPropertyChanged();
            UpdateManualReading();
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartStopLabel)); }
    }

    public string StartStopLabel => IsRunning ? "⏹ Stop" : "▶ Start";

    public double CurrentSensorValue
    {
        get => _currentSensorValue;
        set { _currentSensorValue = value; OnPropertyChanged(); }
    }

    public double[] SensorHistory
    {
        get => _sensorHistory;
        set { _sensorHistory = value; OnPropertyChanged(); }
    }

    public double[] AngleHistory
    {
        get => _angleHistory;
        set { _angleHistory = value; OnPropertyChanged(); }
    }

    public int ExportRotations
    {
        get => _exportRotations;
        set { _exportRotations = Math.Max(1, value); OnPropertyChanged(); }
    }

    public ObservableCollection<ChipDefinition> Chips { get; } = new();

    // ── Commands ────────────────────────────────────────────────────
    public ICommand StartStopCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand AddChipCommand { get; }
    public ICommand RemoveChipCommand { get; }

    // ── Engine ──────────────────────────────────────────────────────

    private double EffectiveRadius => IsTapeFrame
        ? WaferGeometry.FrameOuterDiameter(WaferDiameter) / 2.0
        : WaferDiameter / 2.0;

    private double SensorRadius => EffectiveRadius; // sensor positioned at nominal edge

    private IReadOnlyList<(double, double, double)> ChipList =>
        Chips.Select(c => (c.AngleDeg, c.DepthMm, c.WidthDeg)).ToList();

    private double ReadSensor(double angleDeg)
    {
        double notchDepth = IsTapeFrame ? 0 : NotchDepthMm;
        double notchWidth = IsTapeFrame ? 0 : NotchWidthDeg;

        return WaferGeometry.ComputeSensorReading(
            angleDeg, EffectiveRadius, SensorRadius,
            OffsetX, OffsetY,
            NotchStartDeg, notchDepth, notchWidth,
            BowMm, PotatoChipMm,
            ChipList, NoiseMm, _rng);
    }

    private void RecalculateFullRotation()
    {
        int samples = (int)Math.Ceiling(360.0 / (360.0 * ChuckSpeedRpm / 60.0) * SamplingRateHz);
        samples = Math.Clamp(samples, 360, 36000);

        var angles = new double[samples];
        var values = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            double a = 360.0 * i / samples;
            angles[i] = a;
            values[i] = ReadSensor(a);
        }
        AngleHistory = angles;
        SensorHistory = values;
        UpdateManualReading();
    }

    private void UpdateManualReading()
    {
        CurrentSensorValue = ReadSensor(ManualAngleDeg);
    }

    private void UpdateTimerInterval()
    {
        // We update the display at a maximum of 60 fps regardless of sampling rate
        double displayRateHz = Math.Min(SamplingRateHz, 60);
        _timer.Interval = TimeSpan.FromSeconds(1.0 / displayRateHz);
    }

    private void ToggleRunning()
    {
        if (IsRunning)
        {
            _timer.Stop();
            IsRunning = false;
        }
        else
        {
            _runStartTime = DateTime.UtcNow;
            _runStartAngle = ManualAngleDeg;
            _exportBuffer.Clear();
            _timer.Start();
            IsRunning = true;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _runStartTime).TotalSeconds;
        double degreesPerSecond = ChuckSpeedRpm / 60.0 * 360.0;
        double angle = (_runStartAngle + elapsed * degreesPerSecond) % 360.0;

        ManualAngleDeg = angle; // this triggers recalc + display update

        _exportBuffer.Add((angle, CurrentSensorValue));
    }

    private void ExportCsv()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = $"aligner_data_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dlg.ShowDialog() != true)
            return;

        // Generate data for the requested number of rotations
        double degreesPerSecond = ChuckSpeedRpm / 60.0 * 360.0;
        double totalDegrees = ExportRotations * 360.0;
        double dt = 1.0 / SamplingRateHz;
        int totalSamples = (int)Math.Ceiling(totalDegrees / (degreesPerSecond * dt));

        using var writer = new StreamWriter(dlg.FileName);
        writer.WriteLine("SampleIndex,TimeMs,AngleDeg,SensorValueMm");

        for (int i = 0; i < totalSamples; i++)
        {
            double t = i * dt;
            double angle = (t * degreesPerSecond) % 360.0;
            double value = ReadSensor(angle);
            writer.WriteLine($"{i},{t * 1000.0:F3},{angle:F4},{value:F4}");
        }

        MessageBox.Show($"Exported {totalSamples} samples to:\n{dlg.FileName}",
            "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
