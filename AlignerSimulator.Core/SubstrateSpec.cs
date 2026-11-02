namespace AlignerSimulator;

public enum EdgeFeatureMode { Notch, Flat, DoubleFlat }

/// <summary>A localised edge chip: centre angle on the wafer, depth, and half-width.</summary>
public readonly record struct EdgeChip(double AngleDeg, double DepthMm, double WidthDeg);

/// <summary>
/// Immutable snapshot of everything the edge sensor sees: the substrate, its features and
/// imperfections, where it sits on the chuck, and the sensor's noise.  Angles on the substrate
/// are in its own frame (0 deg = +X when the chuck is at 0 deg).  The UI builds one whenever a
/// parameter changes, so the Flow device endpoint can read it from another thread.
/// </summary>
public sealed record SubstrateSpec
{
    public bool IsTapeFrame { get; init; }
    public double WaferDiameterMm { get; init; } = 300;

    public EdgeFeatureMode EdgeFeature { get; init; } = EdgeFeatureMode.Notch;
    public double NotchAngleDeg { get; init; }
    public double NotchDepthMm { get; init; } = 1.0;
    public double NotchHalfWidthDeg { get; init; } = 0.2;
    public double PrimaryFlatLengthMm { get; init; }
    public double PrimaryFlatAngleDeg { get; init; }
    public double SecondaryFlatLengthMm { get; init; }
    public double SecondaryFlatAngleDeg { get; init; } = 90;

    public double BowMm { get; init; }
    public double PotatoChipMm { get; init; }
    public IReadOnlyList<EdgeChip> Chips { get; init; } = [];
    public IReadOnlyList<FrameNotch> FrameNotches { get; init; } = [];

    /// <summary>Substrate centre relative to the chuck centre, with the chuck at 0 deg.</summary>
    public double OffsetXMm { get; init; }
    public double OffsetYMm { get; init; }

    public double NoiseMm { get; init; }

    public bool HasNotch => !IsTapeFrame && EdgeFeature == EdgeFeatureMode.Notch;
    public bool HasPrimaryFlat => !IsTapeFrame && EdgeFeature != EdgeFeatureMode.Notch;
    public bool HasSecondaryFlat => !IsTapeFrame && EdgeFeature == EdgeFeatureMode.DoubleFlat;

    /// <summary>Radius of the nominal (featureless) outline: the wafer, or the frame's corner arcs.</summary>
    public double NominalRadiusMm => IsTapeFrame
        ? WaferGeometry.FrameOuterDiameter(WaferDiameterMm) / 2.0
        : WaferDiameterMm / 2.0;

    /// <summary>The sensor is positioned so the nominal edge lands on its midpoint.</summary>
    public double SensorRadiusMm => NominalRadiusMm;

    /// <summary>
    /// Where the alignment feature really is, in the substrate frame: the notch, the primary
    /// flat's centre, or a frame's deepest notch.  Null when there is none.
    /// </summary>
    public double? FeatureAngleDeg
    {
        get
        {
            if (IsTapeFrame)
            {
                var deepest = FrameNotches.Where(n => n.DepthMm > 0 && n.WidthMm > 0)
                    .OrderByDescending(n => n.DepthMm).ToList();
                return deepest.Count > 0 ? deepest[0].AngleDeg : null;
            }
            if (HasNotch) return NotchDepthMm > 0 && NotchHalfWidthDeg > 0 ? NotchAngleDeg : null;
            return PrimaryFlatLengthMm > 0 ? PrimaryFlatAngleDeg : null;
        }
    }
}
