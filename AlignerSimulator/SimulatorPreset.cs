namespace AlignerSimulator;

public sealed class SimulatorPreset
{
    public bool IsTapeFrame { get; set; }
    public double WaferDiameter { get; set; } = 300;
    public int EdgeFeatureIndex { get; set; }
    public double NotchStartDeg { get; set; }
    public double NotchDepthMm { get; set; } = 1.0;
    public double NotchWidthDeg { get; set; } = 0.2;
    public double PrimaryFlatLengthMm { get; set; }
    public double PrimaryFlatAngleDeg { get; set; }
    public double SecondaryFlatLengthMm { get; set; }
    public double SecondaryFlatAngleDeg { get; set; } = 90;
    public double NoiseMm { get; set; } = 0.02;
    public double BowMm { get; set; }
    public double PotatoChipMm { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double ChuckSpeedRpm { get; set; } = 60;
    public double SamplingRateHz { get; set; } = 1000;
    public int ExportRotations { get; set; } = 1;
    public List<ChipPresetData> Chips { get; set; } = [];
    public List<FrameNotchPresetData> FrameNotches { get; set; } = [];
}

public sealed class ChipPresetData
{
    public double AngleDeg { get; set; }
    public double DepthMm { get; set; }
    public double WidthDeg { get; set; }
}

public sealed class FrameNotchPresetData
{
    public double AngleDeg { get; set; }
    public double DepthMm { get; set; }
    public double WidthMm { get; set; }
    public int Shape { get; set; }
}
