namespace AlignerSimulator;

/// <summary>What an aligner learns from one or more rotations of its edge sensor.</summary>
/// <param name="OffsetXMm">Substrate centre relative to the chuck centre, chuck at 0 deg (as <see cref="SubstrateSpec.OffsetXMm"/>).</param>
/// <param name="OffsetYMm">As <paramref name="OffsetXMm"/>, along Y.</param>
/// <param name="FeatureFound">Whether a notch, flat or frame notch stood out of the edge.</param>
/// <param name="FeatureAngleDeg">The feature's centre in the substrate frame, corrected for the centring offset.</param>
/// <param name="FeatureChuckAngleDeg">The chuck angle at which the feature's centre passed the sensor.</param>
/// <param name="FeatureDepthMm">How far the feature's deepest point fell below the fitted edge.</param>
/// <param name="NoiseMm">Sensor noise estimated from the residual (robust sigma).</param>
/// <param name="ValidSamples">Samples that fell on the substrate edge.</param>
public sealed record EdgeMeasurement(
    double OffsetXMm,
    double OffsetYMm,
    bool FeatureFound,
    double FeatureAngleDeg,
    double FeatureChuckAngleDeg,
    double FeatureDepthMm,
    double NoiseMm,
    int ValidSamples);

/// <summary>
/// The pre-aligner's algorithm: fit the edge as a low-order Fourier series (the first harmonic is
/// the centring offset, the second absorbs saddle warp and the offset's own second-order term),
/// refit without the samples that dip below it, and take the deepest dip as the feature, centred
/// between its half-depth crossings.  A tape frame's rounded square is 4-fold, so its fit runs to
/// a higher order to absorb the corners.
/// </summary>
public static class EdgeAnalyzer
{
    private const int MinSamples = 16;
    private const int WaferHarmonics = 2;
    private const int FrameHarmonics = 24;
    private const double MaskFloorMm = 0.02;
    private const double FeatureFloorMm = 0.05;

    /// <summary>
    /// Analyses a trace of chuck angles and sensor readings.  Readings outside the sensor's span
    /// (saturated, or NaN where the line missed the edge) are ignored.  Returns null when fewer
    /// than half the samples fell on the edge: the substrate is too far off centre to measure.
    /// </summary>
    public static EdgeMeasurement? Analyze(
        IReadOnlyList<double> chuckAnglesDeg,
        IReadOnlyList<double> readingsMm,
        double sensorRadiusMm,
        bool tapeFrame = false)
    {
        var samples = new List<(double Angle, double Value)>(readingsMm.Count);
        for (int i = 0; i < readingsMm.Count; i++)
        {
            double v = readingsMm[i];
            if (v is >= 0 and <= WaferGeometry.SensorLength)
                samples.Add((Normalize(chuckAnglesDeg[i]), v));
        }
        if (samples.Count < Math.Max(MinSamples, readingsMm.Count / 2))
            return null;
        samples.Sort((a, b) => a.Angle.CompareTo(b.Angle));

        int n = samples.Count;
        var angles = samples.Select(p => p.Angle).ToArray();
        var values = samples.Select(p => p.Value).ToArray();
        int harmonics = Math.Min(tapeFrame ? FrameHarmonics : WaferHarmonics, (n - 1) / 4);

        // Fit, then refit without what dips below the fit, until the mask settles.
        var use = Enumerable.Repeat(true, n).ToArray();
        double[] coeffs = [];
        var residual = new double[n];
        double sigma = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            coeffs = FitFourier(angles, values, use, harmonics);
            for (int i = 0; i < n; i++)
                residual[i] = values[i] - Evaluate(coeffs, angles[i]);
            sigma = RobustSigma(residual, use);

            double threshold = -Math.Max(4 * sigma, MaskFloorMm);
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                bool keep = residual[i] >= threshold;
                changed |= keep != use[i];
                use[i] = keep;
            }
            if (use.Count(u => u) < 2 * harmonics + 1 + MinSamples)
                Array.Fill(use, true);
            if (!changed) break;
        }

        double offsetX = coeffs[1];
        double offsetY = coeffs[2];

        int deepest = 0;
        for (int i = 1; i < n; i++)
            if (residual[i] < residual[deepest]) deepest = i;
        double depth = -residual[deepest];
        bool found = depth > Math.Max(6 * sigma, FeatureFloorMm);

        double chuckAngle = angles[deepest];
        if (found)
        {
            double half = -depth / 2;
            double left = Crossing(angles, residual, deepest, -1, half);
            double right = Crossing(angles, residual, deepest, +1, half);
            chuckAngle = Normalize(left + Normalize(right - left) / 2);
        }

        // The feature passed the sensor at chuckAngle, but with an offset the sensor line meets the
        // edge off the chuck radius: turn by the angle the offset tilts the line by (as the sensor model does).
        double a = chuckAngle * Math.PI / 180.0;
        double cx = offsetX * Math.Cos(a) + offsetY * Math.Sin(a);
        double cy = -offsetX * Math.Sin(a) + offsetY * Math.Cos(a);
        double along = Evaluate(coeffs, chuckAngle) + sensorRadiusMm - WaferGeometry.SensorMidpoint - cx;
        double featureAngle = Normalize(chuckAngle + Math.Atan2(-cy, along) * 180.0 / Math.PI);

        return new EdgeMeasurement(offsetX, offsetY, found, featureAngle, chuckAngle, depth, sigma, n);
    }

    /// <summary>
    /// Walks from the deepest sample in one direction (wrapping round the rotation) to where the
    /// residual rises back above <paramref name="level"/>, and interpolates the angle of the crossing.
    /// </summary>
    private static double Crossing(double[] angles, double[] residual, int from, int step, double level)
    {
        int n = angles.Length;
        int inside = from;
        double unwrapped = angles[from];
        for (int k = 1; k < n; k++)
        {
            int next = ((inside + step) % n + n) % n;
            double nextAngle = unwrapped + step * Normalize(step * (angles[next] - angles[inside]));
            if (residual[next] >= level)
            {
                double t = (level - residual[inside]) / (residual[next] - residual[inside]);
                return unwrapped + t * (nextAngle - unwrapped);
            }
            inside = next;
            unwrapped = nextAngle;
        }
        return unwrapped;
    }

    /// <summary>Least-squares fit of c0 + sum(a_k cos k.theta + b_k sin k.theta), k = 1..harmonics.</summary>
    private static double[] FitFourier(double[] anglesDeg, double[] values, bool[] use, int harmonics)
    {
        int p = 2 * harmonics + 1;
        var normal = new double[p, p];
        var rhs = new double[p];
        var basis = new double[p];

        for (int i = 0; i < anglesDeg.Length; i++)
        {
            if (!use[i]) continue;
            Basis(anglesDeg[i], basis);
            for (int r = 0; r < p; r++)
            {
                rhs[r] += basis[r] * values[i];
                for (int c = r; c < p; c++)
                    normal[r, c] += basis[r] * basis[c];
            }
        }
        for (int r = 0; r < p; r++)
            for (int c = 0; c < r; c++)
                normal[r, c] = normal[c, r];

        return Solve(normal, rhs);
    }

    private static double Evaluate(double[] coeffs, double angleDeg)
    {
        var basis = new double[coeffs.Length];
        Basis(angleDeg, basis);
        double sum = 0;
        for (int i = 0; i < coeffs.Length; i++)
            sum += coeffs[i] * basis[i];
        return sum;
    }

    /// <summary>[1, cos t, sin t, cos 2t, sin 2t, ...]: coefficient 1 and 2 are the centring offset.</summary>
    private static void Basis(double angleDeg, double[] basis)
    {
        double t = angleDeg * Math.PI / 180.0;
        basis[0] = 1;
        for (int k = 1; 2 * k < basis.Length; k++)
        {
            basis[2 * k - 1] = Math.Cos(k * t);
            basis[2 * k] = Math.Sin(k * t);
        }
    }

    /// <summary>Gaussian elimination with partial pivoting; the systems here are at most 49 x 49.</summary>
    private static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
            if (pivot != col)
            {
                for (int c = 0; c < n; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }
            double diag = a[col, col];
            if (Math.Abs(diag) < 1e-12) continue;
            for (int r = col + 1; r < n; r++)
            {
                double f = a[r, col] / diag;
                if (f == 0) continue;
                for (int c = col; c < n; c++) a[r, c] -= f * a[col, c];
                b[r] -= f * b[col];
            }
        }

        var x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double sum = b[r];
            for (int c = r + 1; c < n; c++) sum -= a[r, c] * x[c];
            x[r] = Math.Abs(a[r, r]) < 1e-12 ? 0 : sum / a[r, r];
        }
        return x;
    }

    /// <summary>1.4826 x the median absolute residual of the samples in use: sigma for Gaussian noise.</summary>
    private static double RobustSigma(double[] residual, bool[] use)
    {
        var abs = residual.Where((_, i) => use[i]).Select(Math.Abs).OrderBy(v => v).ToArray();
        if (abs.Length == 0) return 0;
        double median = abs.Length % 2 == 1
            ? abs[abs.Length / 2]
            : (abs[abs.Length / 2 - 1] + abs[abs.Length / 2]) / 2;
        return 1.4826 * median;
    }

    internal static double Normalize(double deg) => ((deg % 360) + 360) % 360;
}
