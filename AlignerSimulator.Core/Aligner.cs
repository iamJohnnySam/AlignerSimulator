namespace AlignerSimulator;

/// <summary>The aligner as it stands when a command arrives: what is on the chuck, where the chuck is, how it scans.</summary>
public sealed record AlignerState(SubstrateSpec Substrate, double ChuckAngleDeg, double ChuckSpeedRpm, double SamplingRateHz);

/// <summary>
/// One alignment: a full scanning rotation from <see cref="StartChuckAngleDeg"/>, the measurement it
/// gave, and the turn to <see cref="FinalChuckAngleDeg"/> that puts the measured feature at the
/// target.  Angles in the tool frame are measured about the chuck centre from the sensor (0 deg),
/// as the top view draws them.  A failed alignment carries a code and stops after the scan.
/// </summary>
public sealed record AlignResult(
    double TargetDeg,
    EdgeMeasurement? Measurement,
    double StartChuckAngleDeg,
    double FinalChuckAngleDeg,
    TimeSpan ScanTime,
    TimeSpan TurnTime,
    double? TrueFeatureAngleDeg,
    string? FailureCode = null,
    string? FailureDetail = null)
{
    public bool Succeeded => FailureCode is null;

    public TimeSpan Duration => ScanTime + TurnTime;

    /// <summary>Where the feature really ends up in the tool frame, from the substrate's true geometry.</summary>
    public double? AchievedDeg => Succeeded && TrueFeatureAngleDeg is { } truth
        ? EdgeAnalyzer.Normalize(truth - FinalChuckAngleDeg)
        : null;

    /// <summary>Achieved minus target, in (-180, 180].</summary>
    public double? ErrorDeg => AchievedDeg is { } achieved ? SignedDiff(achieved, TargetDeg) : null;

    internal static double SignedDiff(double a, double b)
    {
        double d = EdgeAnalyzer.Normalize(a - b);
        return d > 180 ? d - 360 : d;
    }
}

public static class Aligner
{
    /// <summary>Failure codes the device answers <c>Nak,Err,{code}</c> with.</summary>
    public const string NoEdge = "E101";
    public const string NoFeature = "E102";

    public static int SamplesPerRotation(double samplingRateHz, double chuckSpeedRpm) =>
        Math.Clamp((int)Math.Ceiling(samplingRateHz * 60.0 / chuckSpeedRpm), 4, 36000);

    /// <summary>
    /// Scans one rotation, measures the edge, and works out the turn that brings the measured feature
    /// to <paramref name="targetDeg"/>.  Pure: the caller moves the chuck and waits the durations.
    /// </summary>
    public static AlignResult Align(AlignerState state, double targetDeg, Random random)
    {
        var s = state.Substrate;
        double start = EdgeAnalyzer.Normalize(state.ChuckAngleDeg);
        double rotationSeconds = 60.0 / state.ChuckSpeedRpm;
        var scanTime = TimeSpan.FromSeconds(rotationSeconds);
        targetDeg = EdgeAnalyzer.Normalize(targetDeg);

        int count = SamplesPerRotation(state.SamplingRateHz, state.ChuckSpeedRpm);
        var angles = new double[count];
        var readings = new double[count];
        for (int i = 0; i < count; i++)
        {
            angles[i] = start + 360.0 * i / count;
            readings[i] = WaferGeometry.SensorReading(s, angles[i], random);
        }

        var measurement = EdgeAnalyzer.Analyze(angles, readings, s.SensorRadiusMm, s.IsTapeFrame);
        if (measurement is null)
            return new AlignResult(targetDeg, null, start, start, scanTime, TimeSpan.Zero, s.FeatureAngleDeg,
                NoEdge, "no substrate edge in the sensor's range");
        if (!measurement.FeatureFound)
            return new AlignResult(targetDeg, measurement, start, start, scanTime, TimeSpan.Zero, s.FeatureAngleDeg,
                NoFeature, "no notch or flat stood out of the edge");

        // The feature sits at (its substrate angle - chuck angle) in the tool frame, so this chuck angle puts it on the target.
        double final = EdgeAnalyzer.Normalize(measurement.FeatureAngleDeg - targetDeg);
        double turn = Math.Abs(AlignResult.SignedDiff(final, start));
        var turnTime = TimeSpan.FromSeconds(turn / 360.0 * rotationSeconds);

        return new AlignResult(targetDeg, measurement, start, final, scanTime, turnTime, s.FeatureAngleDeg);
    }
}
