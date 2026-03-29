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

public enum EdgeFeatureMode { Notch, Flat, DoubleFlat }

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
        AddFrameNotchCommand = new RelayCommand(_ => FrameNotches.Add(new FrameNotchDefinition()));
        RemoveFrameNotchCommand = new RelayCommand(p => { if (p is FrameNotchDefinition n) FrameNotches.Remove(n); });
        SavePresetCommand = new RelayCommand(_ => SavePreset());
        LoadPresetCommand = new RelayCommand(_ => LoadPreset());

        _primaryFlatLengthMm = WaferGeometry.DefaultPrimaryFlatLength(_waferDiameter);
        _secondaryFlatLengthMm = WaferGeometry.DefaultSecondaryFlatLength(_waferDiameter);

        // Wire up collection change handlers for live updates
        Chips.CollectionChanged += (_, _) => RecalculateFullRotation();
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

    public int SamplesPerRotation => Math.Clamp((int)Math.Ceiling(SamplingRateHz * 60.0 / ChuckSpeedRpm), 4, 36000);

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

    // ── Commands ────────────────────────────────────────────────────
    public ICommand StartStopCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand AddChipCommand { get; }
    public ICommand RemoveChipCommand { get; }
    public ICommand AddFrameNotchCommand { get; }
    public ICommand RemoveFrameNotchCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand LoadPresetCommand { get; }

    // ── Engine ──────────────────────────────────────────────────────

    private double EffectiveRadius => IsTapeFrame
        ? WaferGeometry.FrameOuterDiameter(WaferDiameter) / 2.0
        : WaferDiameter / 2.0;

    private double SensorRadius => EffectiveRadius; // sensor positioned at nominal edge

    private IReadOnlyList<(double, double, double)> ChipList =>
        Chips.Select(c => (c.AngleDeg, c.DepthMm, c.WidthDeg)).ToList();

    private double ReadSensor(double angleDeg)
    {
        if (IsTapeFrame)
        {
            return WaferGeometry.ComputeTapeFrameSensorReading(
                angleDeg, WaferDiameter, SensorRadius,
                OffsetX, OffsetY, NoiseMm, _rng, FrameNotchList);
        }

        return WaferGeometry.ComputeSensorReading(
            angleDeg, EffectiveRadius, SensorRadius,
            OffsetX, OffsetY,
            NotchStartDeg, NotchDepthMm, NotchWidthDeg,
            BowMm, PotatoChipMm, ChipList,
            PrimaryFlatLengthMm, PrimaryFlatAngleDeg,
            SecondaryFlatLengthMm, SecondaryFlatAngleDeg,
            IsNotchMode, HasPrimaryFlat, HasSecondaryFlat,
            NoiseMm, _rng);
    }

    private void RecalculateFullRotation()
    {
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
                contour[i] = WaferGeometry.ComputeWaferEdgeRadius(
                    a, EffectiveRadius,
                    NotchStartDeg, NotchDepthMm, NotchWidthDeg,
                    BowMm, PotatoChipMm, ChipList,
                    PrimaryFlatLengthMm, PrimaryFlatAngleDeg,
                    SecondaryFlatLengthMm, SecondaryFlatAngleDeg,
                    IsNotchMode, HasPrimaryFlat, HasSecondaryFlat);
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
