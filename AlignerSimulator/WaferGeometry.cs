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

    // ---- Wafer edge radius (for both sensor reading and top-view contour) ----

    /// <summary>
    /// Computes the polar edge radius of a wafer at a given angle, incorporating
    /// notch or flat/double-flat features, bow, warp, and edge chips.
    /// </summary>
    public static double ComputeWaferEdgeRadius(
        double angleDeg,
        double waferRadiusMm,
        double notchStartDeg, double notchDepthMm, double notchWidthDeg,
        double bowMm, double potatoChipMm,
        IReadOnlyList<(double AngleDeg, double DepthMm, double WidthDeg)> chips,
        double primaryFlatLengthMm, double primaryFlatAngleDeg,
        double secondaryFlatLengthMm, double secondaryFlatAngleDeg,
        bool useNotch, bool usePrimaryFlat, bool useSecondaryFlat)
    {
        double angleRad = angleDeg * Math.PI / 180.0;
        double R = waferRadiusMm;

        // Base circular radius with bow and saddle warp
        double effectiveR = R + bowMm + potatoChipMm * Math.Cos(2.0 * angleRad);

        // Notch (cosine-bell profile)
        if (useNotch && notchWidthDeg > 0)
        {
            double notchAngleRad = notchStartDeg * Math.PI / 180.0;
            double rel = AngleDiffRad(angleRad, notchAngleRad);
            double halfRad = notchWidthDeg * Math.PI / 180.0;
            if (Math.Abs(rel) < halfRad)
            {
                double t = rel / halfRad;
                effectiveR -= notchDepthMm * Math.Cos(t * Math.PI / 2.0);
            }
        }

        // Primary flat (chord cut - takes minimum of circle and flat line)
        if (usePrimaryFlat && primaryFlatLengthMm > 0)
        {
            double flatR = FlatRadiusAtAngle(angleRad, R, primaryFlatLengthMm, primaryFlatAngleDeg);
            if (flatR < effectiveR)
                effectiveR = flatR;
        }

        // Secondary flat (chord cut)
        if (useSecondaryFlat && secondaryFlatLengthMm > 0)
        {
            double flatR = FlatRadiusAtAngle(angleRad, R, secondaryFlatLengthMm, secondaryFlatAngleDeg);
            if (flatR < effectiveR)
                effectiveR = flatR;
        }

        // Edge chips
        foreach (var (chipAngle, chipDepth, chipWidth) in chips)
        {
            double chipRad = chipAngle * Math.PI / 180.0;
            double chipHalfRad = chipWidth * Math.PI / 180.0;
            double rel = AngleDiffRad(angleRad, chipRad);
            if (Math.Abs(rel) < chipHalfRad && chipHalfRad > 0)
            {
                double t2 = rel / chipHalfRad;
                effectiveR -= chipDepth * Math.Cos(t2 * Math.PI / 2.0);
            }
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

    // ---- Sensor reading: tape frame ----

    /// <summary>
    /// Compute the sensor reading for a tape frame (outer contour only).
    /// </summary>
    public static double ComputeTapeFrameSensorReading(
        double angleDeg,
        double waferDiameterMm,
        double sensorRadiusMm,
        double offsetXMm,
        double offsetYMm,
        double noiseMm,
        Random random,
        IReadOnlyList<FrameNotch>? notches = null)
    {
        var spec = GetTapeFrameSpec(waferDiameterMm);
        double effectiveRadius = ComputeFrameEdgeRadius(angleDeg, spec, notches);

        double angleRad = angleDeg * Math.PI / 180.0;
        double rotCx = offsetXMm * Math.Cos(angleRad) + offsetYMm * Math.Sin(angleRad);
        double rotCy = -offsetXMm * Math.Sin(angleRad) + offsetYMm * Math.Cos(angleRad);

        double d2 = effectiveRadius * effectiveRadius - rotCy * rotCy;
        if (d2 < 0)
            return double.NaN;

        double edgeX = rotCx + Math.Sqrt(d2);
        double sensorZero = sensorRadiusMm - SensorMidpoint;
        double sensorValue = edgeX - sensorZero;

        if (noiseMm > 0)
            sensorValue += NormalRandom(random) * noiseMm;

        return sensorValue;
    }

    // ---- Sensor reading: wafer ----

    /// <summary>
    /// Compute the sensor reading for a wafer (edge with notch or flat features,
    /// bow, warp, and chips). Returns NaN when edge is outside sensor range.
    /// </summary>
    public static double ComputeSensorReading(
        double angleDeg,
        double waferRadiusMm,
        double sensorRadiusMm,
        double offsetXMm,
        double offsetYMm,
        double notchStartDeg, double notchDepthMm, double notchWidthDeg,
        double bowMm, double potatoChipMm,
        IReadOnlyList<(double AngleDeg, double DepthMm, double WidthDeg)> chips,
        double primaryFlatLengthMm, double primaryFlatAngleDeg,
        double secondaryFlatLengthMm, double secondaryFlatAngleDeg,
        bool useNotch, bool usePrimaryFlat, bool useSecondaryFlat,
        double noiseMm,
        Random random)
    {
        double angleRad = angleDeg * Math.PI / 180.0;

        double effectiveRadius = ComputeWaferEdgeRadius(
            angleDeg, waferRadiusMm,
            notchStartDeg, notchDepthMm, notchWidthDeg,
            bowMm, potatoChipMm, chips,
            primaryFlatLengthMm, primaryFlatAngleDeg,
            secondaryFlatLengthMm, secondaryFlatAngleDeg,
            useNotch, usePrimaryFlat, useSecondaryFlat);

        double rotCx = offsetXMm * Math.Cos(angleRad) + offsetYMm * Math.Sin(angleRad);
        double rotCy = -offsetXMm * Math.Sin(angleRad) + offsetYMm * Math.Cos(angleRad);

        double d2 = effectiveRadius * effectiveRadius - rotCy * rotCy;
        if (d2 < 0)
            return double.NaN;

        double edgeX = rotCx + Math.Sqrt(d2);
        double sensorZero = sensorRadiusMm - SensorMidpoint;
        double sensorValue = edgeX - sensorZero;

        if (noiseMm > 0 && random != null)
            sensorValue += NormalRandom(random) * noiseMm;

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
    private static double NormalRandom(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
