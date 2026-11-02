namespace AlignerSimulator;

/// <summary>
/// Tape frame specification.  The outer contour is a rounded square:
/// four straight flats (at 0, 90, 180, 270 deg) connected by circular
/// arc corners.  All dimensions in millimetres.
/// </summary>
public sealed record TapeFrameSpec(
    double ArcRadiusMm,       // radius of the circular arc at each corner
    double FlatDistanceMm,    // perpendicular distance from centre to each flat
    double InnerRadiusMm);    // radius of the circular inner opening

/// <summary>Shape of a tape frame alignment notch.</summary>
public enum FrameNotchShape { VNotch, UNotch }

/// <summary>Immutable notch definition for tape frame geometry.</summary>
public readonly record struct FrameNotch(
    double AngleDeg,
    double DepthMm,
    double WidthMm,
    FrameNotchShape Shape);

/// <summary>
/// Pure math engine for wafer/frame edge geometry and sensor intersection.
/// All units: millimetres and degrees.
///
/// Coordinate system:
///   Chuck centre = origin.
///   Sensor line is at a fixed radial position, perpendicular to the tangent,
///   measuring 0 mm (closest to chuck centre) to 28 mm (furthest).
///   The sensor is positioned so that the ideal (centred) edge falls at 14 mm.
/// </summary>
public static class WaferGeometry
{
    public const double SensorLength = 28.0;
    public const double SensorMidpoint = 14.0;        // ideal edge landing

    // ---- Tape frame specs derived from engineering drawings ----
    //
    // 200 mm wafer frame (from drawing):
    //   arc diameter = 228 mm  =>  arc radius = 114 mm
    //   flat-to-flat  = 212 mm  =>  flat distance = 106 mm
    //   inner opening = 194 mm  =>  inner radius  =  97 mm
    //
    // 300 mm wafer frame (from drawing):
    //   bounding box  ~ 380 mm  =>  arc radius ~ 190 mm
    //   flat-to-flat  ~ 380 mm  =>  flat distance ~ 190 mm  (very shallow flats)
    //   inner radius  = 172.76 mm (from R172.76 callout)
    //
    //   Re-examining: the 300 mm frame outer circle ~ 400 mm diameter
    //   with 4 flats cutting to ~380 mm flat-to-flat.
    //   Arc radius = 200 mm, flat distance = 190.1 mm.

    private static readonly TapeFrameSpec Frame200 = new(
        ArcRadiusMm: 114.0,
        FlatDistanceMm: 106.0,
        InnerRadiusMm: 97.0);

    private static readonly TapeFrameSpec Frame300 = new(
        ArcRadiusMm: 200.0,
        FlatDistanceMm: 190.1,
        InnerRadiusMm: 172.76);

    /// <summary>
    /// Returns the outer arc diameter of the tape frame for a given wafer size.
    /// Two standard sizes: 228 mm (for wafers up to 200 mm) and 400 mm (for larger).
    /// </summary>
    public static double FrameOuterDiameter(double waferDiameterMm)
        => waferDiameterMm <= 200 ? Frame200.ArcRadiusMm * 2.0 : Frame300.ArcRadiusMm * 2.0;

    /// <summary>
    /// Returns the tape frame spec for the given wafer diameter.
    /// Two standard frame sizes based on engineering drawings.
    /// </summary>
    public static TapeFrameSpec GetTapeFrameSpec(double waferDiameterMm)
        => waferDiameterMm <= 200 ? Frame200 : Frame300;

    // ---- SEMI standard wafer flat lengths (mm) per wafer diameter ----

    /// <summary>SEMI-standard primary flat length for a given wafer diameter.</summary>
    public static double DefaultPrimaryFlatLength(double waferDiameterMm) => waferDiameterMm switch
    {
        <= 50 => 15.88,
        <= 76.2 => 22.22,
        <= 100 => 32.5,
        <= 125 => 42.5,
        <= 150 => 57.5,
        <= 200 => 57.5,
        _ => waferDiameterMm * 0.19
    };

    /// <summary>SEMI-standard secondary flat length for a given wafer diameter.</summary>
    public static double DefaultSecondaryFlatLength(double waferDiameterMm) => waferDiameterMm switch
    {
        <= 50 => 8.0,
        <= 76.2 => 11.18,
        <= 100 => 18.0,
        <= 125 => 27.5,
        <= 150 => 37.5,
        _ => waferDiameterMm * 0.125
    };

    // ---- Tape frame contour (rounded square) ----

    /// <summary>
    /// Computes the polar radius of the tape frame outer contour at a given angle.
    /// The shape is a rounded square: four straight flats at 0, 90, 180, 270 deg
    /// connected by circular arc corners.
    /// </summary>
    public static double ComputeFrameEdgeRadius(double frameAngleDeg, TapeFrameSpec spec)
    {
        double R = spec.ArcRadiusMm;
        double d = spec.FlatDistanceMm;
        double phi = frameAngleDeg * Math.PI / 180.0;

        // Half-angle subtended by each flat from its centre direction
        // At the transition, flat meets arc: d/cos(halfAngle) = R  =>  cos(halfAngle) = d/R
        double flatHalfAngle = Math.Acos(Math.Min(d / R, 1.0));

        // Check each of the 4 flats (centred at 0, 90, 180, 270 deg)
        double[] flatCentres = [0, Math.PI / 2.0, Math.PI, 3.0 * Math.PI / 2.0];

        foreach (double centre in flatCentres)
        {
            double rel = AngleDiffRad(phi, centre);
            if (Math.Abs(rel) < flatHalfAngle)
                return d / Math.Cos(rel);
        }

        // In the arc zone between flats
        return R;
    }

    /// <summary>
    /// Computes the polar radius of the tape frame outer contour at a given angle,
    /// including alignment notch cut-outs.
    /// </summary>
    public static double ComputeFrameEdgeRadius(
        double frameAngleDeg, TapeFrameSpec spec, IReadOnlyList<FrameNotch>? notches)
    {
        double baseR = ComputeFrameEdgeRadius(frameAngleDeg, spec);

        if (notches is null or { Count: 0 })
            return baseR;

        double phi = frameAngleDeg * Math.PI / 180.0;

        foreach (var notch in notches)
        {
            if (notch.DepthMm <= 0 || notch.WidthMm <= 0) continue;

            double centerR = ComputeFrameEdgeRadius(notch.AngleDeg, spec);
            double halfAngle = Math.Asin(Math.Min(notch.WidthMm / 2.0 / centerR, 1.0));
            double notchRad = notch.AngleDeg * Math.PI / 180.0;
            double rel = AngleDiffRad(phi, notchRad);

            if (Math.Abs(rel) < halfAngle)
            {
                double t = rel / halfAngle;
                double cut = notch.Shape == FrameNotchShape.VNotch
                    ? notch.DepthMm * (1.0 - Math.Abs(t))
                    : notch.DepthMm * Math.Sqrt(1.0 - t * t);
                baseR -= cut;
            }
        }

        return Math.Max(baseR, 0);
    }

    // ---- Edge radius in the substrate's own frame ----

    /// <summary>
    /// Polar radius of the substrate's outer edge at an angle in its own frame: the tape frame's
    /// rounded square with its notches, or the wafer with its notch or flats, bow, warp and chips.
    /// </summary>
    public static double EdgeRadius(SubstrateSpec s, double substrateAngleDeg)
    {
        if (s.IsTapeFrame)
            return ComputeFrameEdgeRadius(substrateAngleDeg, GetTapeFrameSpec(s.WaferDiameterMm), s.FrameNotches);

        double angleRad = substrateAngleDeg * Math.PI / 180.0;
        double R = s.NominalRadiusMm;

        // Base circular radius with bow and saddle warp
        double effectiveR = R + s.BowMm + s.PotatoChipMm * Math.Cos(2.0 * angleRad);

        // Notch (cosine-bell profile)
        if (s.HasNotch && s.NotchHalfWidthDeg > 0)
        {
            double rel = AngleDiffRad(angleRad, s.NotchAngleDeg * Math.PI / 180.0);
            double halfRad = s.NotchHalfWidthDeg * Math.PI / 180.0;
            if (Math.Abs(rel) < halfRad)
                effectiveR -= s.NotchDepthMm * Math.Cos(rel / halfRad * Math.PI / 2.0);
        }

        // Flats (chord cuts - take the minimum of circle and flat line)
        if (s.HasPrimaryFlat && s.PrimaryFlatLengthMm > 0)
            effectiveR = Math.Min(effectiveR, FlatRadiusAtAngle(angleRad, R, s.PrimaryFlatLengthMm, s.PrimaryFlatAngleDeg));

        if (s.HasSecondaryFlat && s.SecondaryFlatLengthMm > 0)
            effectiveR = Math.Min(effectiveR, FlatRadiusAtAngle(angleRad, R, s.SecondaryFlatLengthMm, s.SecondaryFlatAngleDeg));

        // Edge chips
        foreach (var chip in s.Chips)
        {
            double chipHalfRad = chip.WidthDeg * Math.PI / 180.0;
            double rel = AngleDiffRad(angleRad, chip.AngleDeg * Math.PI / 180.0);
            if (chipHalfRad > 0 && Math.Abs(rel) < chipHalfRad)
                effectiveR -= chip.DepthMm * Math.Cos(rel / chipHalfRad * Math.PI / 2.0);
        }

        return Math.Max(effectiveR, 0);
    }

    /// <summary>
    /// Returns the polar radius imposed by a flat chord at a given angle.
    /// Returns double.MaxValue when outside the flat zone (no constraint).
    /// </summary>
    private static double FlatRadiusAtAngle(double angleRad, double R, double flatLengthMm, double flatAngleDeg)
    {
        double halfChord = flatLengthMm / 2.0;
        if (halfChord >= R) return 0;
        double flatDist = Math.Sqrt(R * R - halfChord * halfChord);
        double flatHalfAngle = Math.Atan2(halfChord, flatDist);
        double flatCenterRad = flatAngleDeg * Math.PI / 180.0;
        double rel = AngleDiffRad(angleRad, flatCenterRad);
        if (Math.Abs(rel) < flatHalfAngle)
            return flatDist / Math.Cos(rel);
        return double.MaxValue;
    }

    // ---- Sensor reading ----

    /// <summary>
    /// Substrate centre in the sensor frame (sensor on +X) with the chuck at the given angle:
    /// the centring offset orbits the chuck centre as the chuck turns.
    /// </summary>
    public static (double X, double Y) CentreInSensorFrame(SubstrateSpec s, double chuckAngleDeg)
    {
        double a = chuckAngleDeg * Math.PI / 180.0;
        return (s.OffsetXMm * Math.Cos(a) + s.OffsetYMm * Math.Sin(a),
               -s.OffsetXMm * Math.Sin(a) + s.OffsetYMm * Math.Cos(a));
    }

    /// <summary>
    /// The sensor reading with the chuck at a given angle: where the sensor's radial line meets
    /// the substrate edge, 0-28 mm with the nominal edge at 14 mm.  NaN when the line misses it.
    /// </summary>
    /// <remarks>
    /// With a centring offset the line does not meet the edge at the chuck angle but at the
    /// chuck angle plus the angle the offset tilts it by, and the edge radius there decides how
    /// far out it meets it.  The two depend on each other, so this iterates; the tilt changes
    /// slowly with the radius, so a few rounds converge even on a notch's flank.
    /// </remarks>
    public static double SensorReading(SubstrateSpec s, double chuckAngleDeg, Random? random = null)
    {
        var (cx, cy) = CentreInSensorFrame(s, chuckAngleDeg);

        double substrateAngle = chuckAngleDeg;
        double along = double.NaN;
        for (int i = 0; i < 5; i++)
        {
            double r = EdgeRadius(s, substrateAngle);
            double d2 = r * r - cy * cy;
            if (d2 < 0)
                return double.NaN;
            along = Math.Sqrt(d2);
            substrateAngle = chuckAngleDeg + Math.Atan2(-cy, along) * 180.0 / Math.PI;
        }

        double sensorZero = s.SensorRadiusMm - SensorMidpoint;
        double sensorValue = cx + along - sensorZero;

        if (s.NoiseMm > 0 && random != null)
            sensorValue += NormalRandom(random) * s.NoiseMm;

        return sensorValue;
    }


    /// <summary>Shortest signed angle difference in radians, result in (-pi, pi].</summary>
    private static double AngleDiffRad(double a, double b)
    {
        double d = a - b;
        d = ((d + Math.PI) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) - Math.PI;
        return d;
    }

    /// <summary>Box-Muller Gaussian.</summary>
    internal static double NormalRandom(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
