using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

public sealed class FrameNotchDefinition : INotifyPropertyChanged
{
    private double _angleDeg;
    private double _depthMm = 3.0;
    private double _widthMm = 6.0;
    private FrameNotchShape _shape = FrameNotchShape.VNotch;

    public double AngleDeg { get => _angleDeg; set { _angleDeg = value; OnPropertyChanged(); } }
    public double DepthMm { get => _depthMm; set { _depthMm = value; OnPropertyChanged(); } }
    public double WidthMm { get => _widthMm; set { _widthMm = value; OnPropertyChanged(); } }
    public FrameNotchShape Shape { get => _shape; set { _shape = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShapeIndex)); } }
    public int ShapeIndex { get => (int)_shape; set { Shape = (FrameNotchShape)value; } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class SimulatorViewModel : INotifyPropertyChanged
{
    // ── Backing fields ──────────────────────────────────────────────
    private bool _isTapeFrame;
    private double _waferDiameter = 300.0;
    private double _notchStartDeg;
    private double _notchDepthMm = 1.0;
    private double _notchWidthDeg = 0.2;
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
    private EdgeFeatureMode _edgeFeature = EdgeFeatureMode.Notch;
    private double _primaryFlatLengthMm;
    private double _primaryFlatAngleDeg;
    private double _secondaryFlatLengthMm;
    private double _secondaryFlatAngleDeg = 90.0;

    // Live display data
    private double _currentSensorValue;
    private double[] _sensorHistory = Array.Empty<double>();
    private double[] _angleHistory = Array.Empty<double>();
    private double[] _waferContour = Array.Empty<double>();

    private readonly DispatcherTimer _timer = new();
    private readonly Random _rng = new();
    private DateTime _runStartTime;
    private double _runStartAngle;
    private SubstrateSpec _spec = new();

    // Measurement and the Flow device endpoint
    private string _measurementText = "Press Measure to run the aligner's algorithm on the trace.";
    private readonly AlignerDevice _device;
    private int _devicePort = 7300;
    private double _deviceTargetDeg;
    private string _deviceStatus = "Not listening";
    private string _lastAlignText = "";
    private AlignResult? _motion;
    private DateTime _motionStart;

    public SimulatorViewModel()
    {
        _timer.Tick += Timer_Tick;
        UpdateTimerInterval();

        // The device runs on pool threads: it reads the state and reports back through the dispatcher.
        var dispatcher = Dispatcher.CurrentDispatcher;
        _device = new AlignerDevice(() => dispatcher.Invoke(() => new AlignerState(_spec, ManualAngleDeg, ChuckSpeedRpm, SamplingRateHz)));
        _device.AlignStarted += r => dispatcher.BeginInvoke(() => BeginMotion(r));
        _device.AlignFinished += r => dispatcher.BeginInvoke(() => EndMotion(r));
        _device.Log += line => dispatcher.BeginInvoke(() => AddDeviceLog(line));

        StartStopCommand = new RelayCommand(_ => ToggleRunning());
        MeasureCommand = new RelayCommand(_ => Measure());
        ToggleDeviceCommand = new RelayCommand(_ => ToggleDevice());
        ExportCsvCommand = new RelayCommand(_ => ExportCsv());
        AddChipCommand = new RelayCommand(_ => Chips.Add(new ChipDefinition()));
        RemoveChipCommand = new RelayCommand(p => { if (p is ChipDefinition c) Chips.Remove(c); });
        AddFrameNotchCommand = new RelayCommand(_ => FrameNotches.Add(new FrameNotchDefinition()));
        RemoveFrameNotchCommand = new RelayCommand(p => { if (p is FrameNotchDefinition n) FrameNotches.Remove(n); });
        SavePresetCommand = new RelayCommand(_ => SavePreset());
        LoadPresetCommand = new RelayCommand(_ => LoadPreset());

        _primaryFlatLengthMm = WaferGeometry.DefaultPrimaryFlatLength(_waferDiameter);
        _secondaryFlatLengthMm = WaferGeometry.DefaultSecondaryFlatLength(_waferDiameter);

        // Wire up collection change handlers for live updates
        Chips.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (var item in e.NewItems)
                    if (item is ChipDefinition c)
                        c.PropertyChanged += (_, _) => RecalculateFullRotation();
            RecalculateFullRotation();
        };
        FrameNotches.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (var item in e.NewItems)
                    if (item is FrameNotchDefinition fn)
                        fn.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(FrameNotchList)); RecalculateFullRotation(); };
            OnPropertyChanged(nameof(FrameNotchList));
            RecalculateFullRotation();
        };

        // Default tape frame notches (parameterized – adjust values to match actual frame)
        // Both notches on the same flat (top, centred at 90°)
        FrameNotches.Add(new FrameNotchDefinition { AngleDeg = 78, DepthMm = 3.0, WidthMm = 6.0, Shape = FrameNotchShape.VNotch });
        FrameNotches.Add(new FrameNotchDefinition { AngleDeg = 102, DepthMm = 3.0, WidthMm = 6.0, Shape = FrameNotchShape.UNotch });

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
        set { _waferDiameter = Math.Round(Math.Clamp(value, 50, 450)); OnPropertyChanged(); RecalculateFullRotation(); }
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
        set { _chuckSpeedRpm = Math.Clamp(value, 1, 6000); OnPropertyChanged(); OnPropertyChanged(nameof(SamplesPerRotation)); UpdateTimerInterval(); RecalculateFullRotation(); }
    }

    public double SamplingRateHz
    {
        get => _samplingRateHz;
        set { _samplingRateHz = Math.Clamp(value, 10, 100000); OnPropertyChanged(); OnPropertyChanged(nameof(SamplesPerRotation)); UpdateTimerInterval(); RecalculateFullRotation(); }
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

    public int SamplesPerRotation => Aligner.SamplesPerRotation(SamplingRateHz, ChuckSpeedRpm);

    public ObservableCollection<ChipDefinition> Chips { get; } = new();

    public ObservableCollection<FrameNotchDefinition> FrameNotches { get; } = new();

    public FrameNotch[] FrameNotchList =>
        FrameNotches.Select(n => new FrameNotch(n.AngleDeg, n.DepthMm, n.WidthMm, n.Shape)).ToArray();

    public int EdgeFeatureIndex
    {
        get => (int)_edgeFeature;
        set
        {
            _edgeFeature = (EdgeFeatureMode)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotchMode));
            OnPropertyChanged(nameof(HasPrimaryFlat));
            OnPropertyChanged(nameof(HasSecondaryFlat));
            RecalculateFullRotation();
        }
    }

    public bool IsNotchMode => _edgeFeature == EdgeFeatureMode.Notch;
    public bool HasPrimaryFlat => _edgeFeature != EdgeFeatureMode.Notch;
    public bool HasSecondaryFlat => _edgeFeature == EdgeFeatureMode.DoubleFlat;

    public double PrimaryFlatLengthMm
    {
        get => _primaryFlatLengthMm;
        set { _primaryFlatLengthMm = Math.Max(0, value); OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double PrimaryFlatAngleDeg
    {
        get => _primaryFlatAngleDeg;
        set { _primaryFlatAngleDeg = ((value % 360) + 360) % 360; OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double SecondaryFlatLengthMm
    {
        get => _secondaryFlatLengthMm;
        set { _secondaryFlatLengthMm = Math.Max(0, value); OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double SecondaryFlatAngleDeg
    {
        get => _secondaryFlatAngleDeg;
        set { _secondaryFlatAngleDeg = ((value % 360) + 360) % 360; OnPropertyChanged(); RecalculateFullRotation(); }
    }

    public double[] WaferContour
    {
        get => _waferContour;
        private set { _waferContour = value; OnPropertyChanged(); }
    }

    public string MeasurementText
    {
        get => _measurementText;
        private set { _measurementText = value; OnPropertyChanged(); }
    }

    public int DevicePort
    {
        get => _devicePort;
        set { _devicePort = Math.Clamp(value, 1, 65535); OnPropertyChanged(); }
    }

    public double DeviceTargetDeg
    {
        get => _deviceTargetDeg;
        set { _deviceTargetDeg = ((value % 360) + 360) % 360; _device.DefaultTargetDeg = _deviceTargetDeg; OnPropertyChanged(); }
    }

    public bool IsDeviceListening => _device.IsListening;

    public string DeviceButtonLabel => IsDeviceListening ? "⏹ Stop Listening" : "🔌 Listen";

    public string DeviceStatus
    {
        get => _deviceStatus;
        private set { _deviceStatus = value; OnPropertyChanged(); }
    }

    public string LastAlignText
    {
        get => _lastAlignText;
        private set { _lastAlignText = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> DeviceLog { get; } = new();

    // ── Commands ────────────────────────────────────────────────────
    public ICommand StartStopCommand { get; }
    public ICommand MeasureCommand { get; }
    public ICommand ToggleDeviceCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand AddChipCommand { get; }
    public ICommand RemoveChipCommand { get; }
    public ICommand AddFrameNotchCommand { get; }
    public ICommand RemoveFrameNotchCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand LoadPresetCommand { get; }

    // ── Engine ──────────────────────────────────────────────────────

    private SubstrateSpec BuildSpec() => new()
    {
        IsTapeFrame = IsTapeFrame,
        WaferDiameterMm = WaferDiameter,
        EdgeFeature = _edgeFeature,
        NotchAngleDeg = NotchStartDeg,
        NotchDepthMm = NotchDepthMm,
        NotchHalfWidthDeg = NotchWidthDeg,
        PrimaryFlatLengthMm = PrimaryFlatLengthMm,
        PrimaryFlatAngleDeg = PrimaryFlatAngleDeg,
        SecondaryFlatLengthMm = SecondaryFlatLengthMm,
        SecondaryFlatAngleDeg = SecondaryFlatAngleDeg,
        BowMm = BowMm,
        PotatoChipMm = PotatoChipMm,
        Chips = Chips.Select(c => new EdgeChip(c.AngleDeg, c.DepthMm, c.WidthDeg)).ToArray(),
        FrameNotches = FrameNotchList,
        OffsetXMm = OffsetX,
        OffsetYMm = OffsetY,
        NoiseMm = NoiseMm,
    };

    private double ReadSensor(double angleDeg) => WaferGeometry.SensorReading(_spec, angleDeg, _rng);

    private void RecalculateFullRotation()
    {
        _spec = BuildSpec();
        int samples = SamplesPerRotation;

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

        if (!IsTapeFrame)
        {
            const int contourPoints = 720;
            var contour = new double[contourPoints];
            for (int i = 0; i < contourPoints; i++)
            {
                double a = 360.0 * i / contourPoints;
                contour[i] = WaferGeometry.EdgeRadius(_spec, a);
            }
            WaferContour = contour;
        }

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
            IsRunning = false;
            if (_motion is null) _timer.Stop();
        }
        else
        {
            if (_motion is not null) return; // the device owns the chuck until its alignment ends
            _runStartTime = DateTime.UtcNow;
            _runStartAngle = ManualAngleDeg;
            _timer.Start();
            IsRunning = true;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_motion is { } m)
        {
            // An alignment: one scanning rotation, then the short turn to the final angle.
            double t = (DateTime.UtcNow - _motionStart).TotalSeconds;
            double scan = m.ScanTime.TotalSeconds;
            double turn = m.TurnTime.TotalSeconds;
            if (t < scan)
                ManualAngleDeg = m.StartChuckAngleDeg + 360.0 * t / scan;
            else if (t < scan + turn)
            {
                double delta = AngleDelta(m.FinalChuckAngleDeg, m.StartChuckAngleDeg);
                ManualAngleDeg = m.StartChuckAngleDeg + delta * (t - scan) / turn;
            }
            else
                ManualAngleDeg = m.FinalChuckAngleDeg;
            return;
        }

        double elapsed = (DateTime.UtcNow - _runStartTime).TotalSeconds;
        double degreesPerSecond = ChuckSpeedRpm / 60.0 * 360.0;
        ManualAngleDeg = (_runStartAngle + elapsed * degreesPerSecond) % 360.0; // this triggers recalc + display update
    }

    private static double AngleDelta(double to, double from)
    {
        double d = (((to - from) % 360) + 360) % 360;
        return d > 180 ? d - 360 : d;
    }

    // ── Measurement and the Flow device ─────────────────────────────

    private void Measure()
    {
        var m = EdgeAnalyzer.Analyze(AngleHistory, SensorHistory, _spec.SensorRadiusMm, _spec.IsTapeFrame);
        MeasurementText = Describe(m, _spec);
    }

    private static string Describe(EdgeMeasurement? m, SubstrateSpec truth)
    {
        if (m is null)
            return "No edge: fewer than half the samples fell within the sensor's 0–28 mm.";

        string text = $"Offset X {m.OffsetXMm:F3}  Y {m.OffsetYMm:F3} mm (true {truth.OffsetXMm:F3}, {truth.OffsetYMm:F3})\n"
                    + $"Noise σ {m.NoiseMm:F3} mm · {m.ValidSamples} samples";
        if (!m.FeatureFound)
            return text + "\nNo notch or flat stood out of the edge.";

        text += $"\nFeature {m.FeatureAngleDeg:F3}° · depth {m.FeatureDepthMm:F2} mm";
        if (truth.FeatureAngleDeg is { } trueAngle)
            text += $"\nTrue {trueAngle:F3}° · error {AngleDelta(m.FeatureAngleDeg, trueAngle):+0.000;-0.000}°";
        return text;
    }

    private void BeginMotion(AlignResult r)
    {
        if (IsRunning) IsRunning = false;
        _motion = r;
        _motionStart = DateTime.UtcNow;
        _timer.Start();
        LastAlignText = $"Aligning to {r.TargetDeg:F2}°…";
    }

    private void EndMotion(AlignResult r)
    {
        _motion = null;
        if (!IsRunning) _timer.Stop();
        ManualAngleDeg = r.FinalChuckAngleDeg;
        MeasurementText = Describe(r.Measurement, _spec);
        LastAlignText = r.Succeeded
            ? $"Aligned to {r.TargetDeg:F2}° in {r.Duration.TotalSeconds:F2} s"
              + (r.ErrorDeg is { } err ? $" · notch really at {r.AchievedDeg:F3}° (error {err:+0.000;-0.000}°)" : "")
            : $"Failed {r.FailureCode}: {r.FailureDetail}";
    }

    private void ToggleDevice()
    {
        try
        {
            if (_device.IsListening)
            {
                _device.Stop();
                DeviceStatus = "Not listening";
            }
            else
            {
                _device.DefaultTargetDeg = DeviceTargetDeg;
                _device.Start(DevicePort);
                DeviceStatus = $"Listening on 127.0.0.1:{_device.Port} (CommSpec)";
            }
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            DeviceStatus = $"Cannot listen on {DevicePort}: {ex.Message}";
        }
        OnPropertyChanged(nameof(IsDeviceListening));
        OnPropertyChanged(nameof(DeviceButtonLabel));
    }

    private void AddDeviceLog(string line)
    {
        DeviceLog.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {line}");
        while (DeviceLog.Count > 100)
            DeviceLog.RemoveAt(DeviceLog.Count - 1);
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

    private void SavePreset()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = "aligner_preset.json"
        };

        if (dlg.ShowDialog() != true)
            return;

        var preset = new SimulatorPreset
        {
            IsTapeFrame = IsTapeFrame,
            WaferDiameter = WaferDiameter,
            EdgeFeatureIndex = EdgeFeatureIndex,
            NotchStartDeg = NotchStartDeg,
            NotchDepthMm = NotchDepthMm,
            NotchWidthDeg = NotchWidthDeg,
            PrimaryFlatLengthMm = PrimaryFlatLengthMm,
            PrimaryFlatAngleDeg = PrimaryFlatAngleDeg,
            SecondaryFlatLengthMm = SecondaryFlatLengthMm,
            SecondaryFlatAngleDeg = SecondaryFlatAngleDeg,
            NoiseMm = NoiseMm,
            BowMm = BowMm,
            PotatoChipMm = PotatoChipMm,
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            ChuckSpeedRpm = ChuckSpeedRpm,
            SamplingRateHz = SamplingRateHz,
            ExportRotations = ExportRotations,
            Chips = Chips.Select(c => new ChipPresetData
            {
                AngleDeg = c.AngleDeg,
                DepthMm = c.DepthMm,
                WidthDeg = c.WidthDeg
            }).ToList(),
            FrameNotches = FrameNotches.Select(n => new FrameNotchPresetData
            {
                AngleDeg = n.AngleDeg,
                DepthMm = n.DepthMm,
                WidthMm = n.WidthMm,
                Shape = (int)n.Shape
            }).ToList()
        };

        var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dlg.FileName, json);
        MessageBox.Show($"Preset saved to:\n{dlg.FileName}",
            "Preset Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadPreset()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var preset = JsonSerializer.Deserialize<SimulatorPreset>(json);
            if (preset == null) return;

            IsTapeFrame = preset.IsTapeFrame;
            WaferDiameter = preset.WaferDiameter;
            EdgeFeatureIndex = preset.EdgeFeatureIndex;
            NotchStartDeg = preset.NotchStartDeg;
            NotchDepthMm = preset.NotchDepthMm;
            NotchWidthDeg = preset.NotchWidthDeg;
            PrimaryFlatLengthMm = preset.PrimaryFlatLengthMm;
            PrimaryFlatAngleDeg = preset.PrimaryFlatAngleDeg;
            SecondaryFlatLengthMm = preset.SecondaryFlatLengthMm;
            SecondaryFlatAngleDeg = preset.SecondaryFlatAngleDeg;
            NoiseMm = preset.NoiseMm;
            BowMm = preset.BowMm;
            PotatoChipMm = preset.PotatoChipMm;
            OffsetX = preset.OffsetX;
            OffsetY = preset.OffsetY;
            ChuckSpeedRpm = preset.ChuckSpeedRpm;
            SamplingRateHz = preset.SamplingRateHz;
            ExportRotations = preset.ExportRotations;

            Chips.Clear();
            foreach (var c in preset.Chips)
                Chips.Add(new ChipDefinition { AngleDeg = c.AngleDeg, DepthMm = c.DepthMm, WidthDeg = c.WidthDeg });

            FrameNotches.Clear();
            foreach (var n in preset.FrameNotches)
                FrameNotches.Add(new FrameNotchDefinition
                {
                    AngleDeg = n.AngleDeg,
                    DepthMm = n.DepthMm,
                    WidthMm = n.WidthMm,
                    Shape = (FrameNotchShape)n.Shape
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load preset:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
