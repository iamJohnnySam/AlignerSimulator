namespace AlignerSimulator;

/// <summary>
/// Pure math engine – computes where the wafer/frame edge intersects the sensor line
/// for any given chuck angle.  All units millimetres &amp; degrees.
/// 
/// Coordinate system:
///   Chuck centre = origin.
///   Sensor line is at a fixed radial position, perpendicular to the tangent,
///   measuring 0 mm (closest to chuck centre) to 28 mm (furthest).
///   The sensor is positioned so that the ideal (centred) wafer edge falls at 14 mm.
/// </summary>
public static class WaferGeometry
{
    public const double SensorLength = 28.0;          // Keyence IG-028
    public const double SensorMidpoint = 14.0;        // ideal edge landing

    // ── SEMI G74 tape‑frame outer diameters (mm) ──────────────────────
    // Standard says the frame OD for each wafer size:
    //   150 mm wafer → frame OD ~250 mm
    //   200 mm wafer → frame OD ~300 mm
    //   300 mm wafer → frame OD ~400 mm
    public static double FrameOuterDiameter(double waferDiameterMm) => waferDiameterMm switch
    {
        <= 150 => 250.0,
        <= 200 => 300.0,
        <= 300 => 400.0,
        _ => waferDiameterMm + 100.0  // fallback
    };

    /// <summary>
    /// Compute the sensor reading (mm along the 0‑28 mm line) for a given chuck angle.
    /// Returns NaN when the edge is completely outside the sensor range.
    /// </summary>
    /// <param name="angleDeg">Current chuck rotation angle (degrees).</param>
    /// <param name="waferRadiusMm">Nominal wafer (or frame) radius.</param>
    /// <param name="sensorRadiusMm">
    /// Distance from chuck centre to the sensor midpoint (set so the ideal edge = 14 mm).
    /// Typically = waferRadius.
    /// </param>
    /// <param name="offsetXMm">Wafer centre offset X (mm).</param>
    /// <param name="offsetYMm">Wafer centre offset Y (mm).</param>
    /// <param name="notchStartDeg">Angular position of the notch on the wafer (degrees).</param>
    /// <param name="notchDepthMm">Depth of the notch (mm). Typical = ~1 mm for 200/300 wafers.</param>
    /// <param name="notchWidthDeg">Angular half‑width of the notch (degrees).</param>
    /// <param name="bowMm">Bow (centre deflection) – simple radial expansion/contraction.</param>
    /// <param name="potatoChipMm">Saddle‑shaped warp amplitude – produces a cos(2θ) variation.</param>
    /// <param name="chips">Random edge chips: (angleDeg, depthMm, widthDeg).</param>
    /// <param name="noiseMm">Gaussian noise σ to add.</param>
    /// <param name="random">RNG for noise.</param>
    public static double ComputeSensorReading(
        double angleDeg,
        double waferRadiusMm,
        double sensorRadiusMm,
        double offsetXMm,
        double offsetYMm,
        double notchStartDeg,
        double notchDepthMm,
        double notchWidthDeg,
        double bowMm,
        double potatoChipMm,
        IReadOnlyList<(double AngleDeg, double DepthMm, double WidthDeg)> chips,
        double noiseMm,
        Random random)
    {
        double angleRad = angleDeg * Math.PI / 180.0;

        // Effective wafer centre in the sensor's local frame
        double cx = offsetXMm;
        double cy = offsetYMm;

        // The sensor sits at angle 0 (positive X direction), measuring radially.
        // As the chuck rotates by angleDeg the wafer features rotate under the sensor.
        // Equivalently we can keep the sensor fixed and rotate the wafer centre.
        double rotCx = cx * Math.Cos(angleRad) + cy * Math.Sin(angleRad);
        double rotCy = -cx * Math.Sin(angleRad) + cy * Math.Cos(angleRad);

        // The sensor line is along the X axis at Y = 0.
        // Distance from rotated wafer centre to the sensor line (Y = 0) is |rotCy|.
        // The chord intersection with Y = 0 gives the edge X position.
        double effectiveRadius = waferRadiusMm;

        // Bow: uniform radial expansion
        effectiveRadius += bowMm;

        // Potato‑chip / saddle warp: cos(2*(angle relative to wafer))
        double waferAngleRad = angleRad; // feature angle on wafer
        effectiveRadius += potatoChipMm * Math.Cos(2.0 * waferAngleRad);

        // Notch: angular proximity to notch position
        double notchAngle = notchStartDeg * Math.PI / 180.0;
        double relativeAngle = AngleDiffRad(angleRad, notchAngle);
        double notchHalfRad = notchWidthDeg * Math.PI / 180.0;
        if (Math.Abs(relativeAngle) < notchHalfRad && notchHalfRad > 0)
        {
            double t = relativeAngle / notchHalfRad;          // -1 .. 1
            double notchProfile = Math.Cos(t * Math.PI / 2.0); // smooth bell
            effectiveRadius -= notchDepthMm * notchProfile;
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
                effectiveRadius -= chipDepth * Math.Cos(t2 * Math.PI / 2.0);
            }
        }

        // Now compute edge position along the sensor line.
        // The wafer circle centre in sensor frame: (rotCx, rotCy), radius = effectiveRadius.
        // Sensor line = Y = 0.  Solve for X on the circle:
        //   (X - rotCx)^2 + rotCy^2 = effectiveRadius^2
        double d2 = effectiveRadius * effectiveRadius - rotCy * rotCy;
        if (d2 < 0)
            return double.NaN; // no intersection

        double sqrtD = Math.Sqrt(d2);

        // We want the outer edge (furthest from centre = largest X)
        double edgeX = rotCx + sqrtD;

        // Convert to sensor coordinate: sensor 0 mm is at (sensorRadiusMm - SensorMidpoint)
        double sensorZero = sensorRadiusMm - SensorMidpoint;
        double sensorValue = edgeX - sensorZero;

        // Add noise
        if (noiseMm > 0 && random != null)
        {
            sensorValue += NormalRandom(random) * noiseMm;
        }

        return sensorValue;
    }

    /// <summary>Shortest signed angle difference in radians, result in (-π, π].</summary>
    private static double AngleDiffRad(double a, double b)
    {
        double d = a - b;
        d = ((d + Math.PI) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) - Math.PI;
        return d;
    }

    /// <summary>Box‑Muller Gaussian.</summary>
    private static double NormalRandom(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
