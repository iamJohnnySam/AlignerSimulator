using Xunit;

namespace AlignerSimulator.Tests;

public class EdgeAnalyzerTests
{
    private static EdgeMeasurement Measure(SubstrateSpec s, int samples, int seed = 1, double startDeg = 0)
    {
        var random = new Random(seed);
        var angles = Enumerable.Range(0, samples).Select(i => startDeg + 360.0 * i / samples).ToArray();
        var readings = angles.Select(a => WaferGeometry.SensorReading(s, a, random)).ToArray();
        return EdgeAnalyzer.Analyze(angles, readings, s.SensorRadiusMm, s.IsTapeFrame)
            ?? throw new Xunit.Sdk.XunitException("no edge measured");
    }

    private static void AngleNear(double expected, double actual, double tolerance) =>
        Assert.InRange(AlignResult.SignedDiff(actual, expected), -tolerance, tolerance);

    [Theory]
    [InlineData(37, 1.5, -2.0)]
    [InlineData(0, 0, 0)]
    [InlineData(359.9, -3, 4)]
    [InlineData(180, 4.5, 0.5)]
    public void Finds_a_notch_and_the_centring_offset(double notchDeg, double offsetX, double offsetY)
    {
        var s = new SubstrateSpec { NotchAngleDeg = notchDeg, OffsetXMm = offsetX, OffsetYMm = offsetY, NoiseMm = 0.01 };

        var m = Measure(s, samples: 10_000);

        Assert.True(m.FeatureFound);
        AngleNear(notchDeg, m.FeatureAngleDeg, 0.03);
        Assert.Equal(offsetX, m.OffsetXMm, 0.01);
        Assert.Equal(offsetY, m.OffsetYMm, 0.01);
        Assert.Equal(1.0, m.FeatureDepthMm, 0.1);
        Assert.Equal(0.01, m.NoiseMm, 0.003);
    }

    [Fact]
    public void Finds_the_notch_at_the_default_sampling_where_it_spans_two_samples()
    {
        // 1000 Hz at 60 RPM: 0.36 deg between samples against a notch 0.4 deg wide.
        var s = new SubstrateSpec { NotchAngleDeg = 211.3, OffsetXMm = 1, OffsetYMm = 1, NoiseMm = 0.02 };

        var m = Measure(s, samples: 1000);

        Assert.True(m.FeatureFound);
        AngleNear(211.3, m.FeatureAngleDeg, 0.2);
    }

    [Fact]
    public void Finds_the_primary_flat_of_a_double_flat_wafer()
    {
        var s = new SubstrateSpec
        {
            WaferDiameterMm = 150, EdgeFeature = EdgeFeatureMode.DoubleFlat,
            PrimaryFlatAngleDeg = 270, PrimaryFlatLengthMm = 57.5,
            SecondaryFlatAngleDeg = 0, SecondaryFlatLengthMm = 37.5,
            OffsetXMm = 0.8, OffsetYMm = 0.5, NoiseMm = 0.01,
        };

        var m = Measure(s, samples: 3600);

        Assert.True(m.FeatureFound);
        AngleNear(270, m.FeatureAngleDeg, 0.2);
        Assert.Equal(0.8, m.OffsetXMm, 0.05);
        Assert.Equal(0.5, m.OffsetYMm, 0.05);
    }

    [Fact]
    public void Warp_and_bow_alone_are_no_feature()
    {
        var s = new SubstrateSpec { NotchDepthMm = 0, BowMm = 0.4, PotatoChipMm = 0.6, OffsetXMm = 2, NoiseMm = 0.01 };

        var m = Measure(s, samples: 3600);

        Assert.False(m.FeatureFound);
        Assert.Equal(2, m.OffsetXMm, 0.01);
    }

    [Fact]
    public void A_shallower_chip_does_not_draw_it_off_the_notch()
    {
        var s = new SubstrateSpec
        {
            NotchAngleDeg = 90, NoiseMm = 0.01,
            Chips = [new EdgeChip(200, 0.5, 1.0), new EdgeChip(15, 0.3, 2.0)],
        };

        var m = Measure(s, samples: 10_000);

        AngleNear(90, m.FeatureAngleDeg, 0.05);
    }

    [Fact]
    public void Finds_a_tape_frame_notch_past_the_corners()
    {
        var s = new SubstrateSpec
        {
            IsTapeFrame = true, WaferDiameterMm = 300,
            FrameNotches = [new FrameNotch(78, 3, 6, FrameNotchShape.VNotch), new FrameNotch(102, 2, 6, FrameNotchShape.UNotch)],
            OffsetXMm = 1, OffsetYMm = -1, NoiseMm = 0.01,
        };

        var m = Measure(s, samples: 10_000);

        Assert.True(m.FeatureFound);
        AngleNear(78, m.FeatureAngleDeg, 0.3);
        Assert.Equal(1, m.OffsetXMm, 0.1);
        Assert.Equal(-1, m.OffsetYMm, 0.1);
    }

    [Fact]
    public void The_scan_may_start_anywhere_and_wrap()
    {
        var s = new SubstrateSpec { NotchAngleDeg = 2, OffsetXMm = -1, NoiseMm = 0.01 };

        var m = Measure(s, samples: 10_000, startDeg: 301.7);

        AngleNear(2, m.FeatureAngleDeg, 0.03);
    }

    [Fact]
    public void Nothing_on_the_sensor_is_no_measurement()
    {
        var angles = Enumerable.Range(0, 360).Select(i => (double)i).ToArray();
        var saturated = angles.Select(_ => 30.0).ToArray();

        Assert.Null(EdgeAnalyzer.Analyze(angles, saturated, 150));
    }
}
