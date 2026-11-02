using Xunit;

namespace AlignerSimulator.Tests;

public class SensorModelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(123.4)]
    [InlineData(359.9)]
    public void A_centred_featureless_wafer_reads_the_sensor_midpoint(double chuckAngle)
    {
        var s = new SubstrateSpec { NotchDepthMm = 0 };

        Assert.Equal(WaferGeometry.SensorMidpoint, WaferGeometry.SensorReading(s, chuckAngle), 9);
    }

    [Fact]
    public void An_offset_wafer_swings_the_reading_by_the_offset()
    {
        var s = new SubstrateSpec { NotchDepthMm = 0, OffsetXMm = 2 };

        Assert.Equal(16, WaferGeometry.SensorReading(s, 0), 9);
        Assert.Equal(12, WaferGeometry.SensorReading(s, 180), 9);
    }

    /// <summary>
    /// The point the sensor reports must lie on the substrate's true edge.  Before the ray-cast fix
    /// the edge radius was taken at the chuck angle, so with an offset a notch read up to ~1 deg off.
    /// </summary>
    [Theory]
    [InlineData(0, 3, 40, 0.2)]
    [InlineData(-2.5, 1.5, 200, 0.5)]
    [InlineData(4, -4, 359.8, 1.0)]
    public void The_reported_point_lies_on_the_true_edge(double offsetX, double offsetY, double notchDeg, double halfWidthDeg)
    {
        var s = new SubstrateSpec
        {
            OffsetXMm = offsetX, OffsetYMm = offsetY,
            NotchAngleDeg = notchDeg, NotchDepthMm = 1, NotchHalfWidthDeg = halfWidthDeg,
        };
        double sensorZero = s.SensorRadiusMm - WaferGeometry.SensorMidpoint;

        for (double chuck = notchDeg - 3; chuck < notchDeg + 3; chuck += 0.01)
        {
            double reading = WaferGeometry.SensorReading(s, chuck);
            var (cx, cy) = WaferGeometry.CentreInSensorFrame(s, chuck);
            double dx = sensorZero + reading - cx, dy = -cy;
            double substrateAngle = chuck + Math.Atan2(dy, dx) * 180 / Math.PI;

            Assert.Equal(WaferGeometry.EdgeRadius(s, substrateAngle), Math.Sqrt(dx * dx + dy * dy), 5);
        }
    }

    [Fact]
    public void The_notch_bottom_passes_the_sensor_where_the_offset_tilts_it_to()
    {
        // The centre sits 3 mm to the side of the sensor line with the chuck near 40 deg, so the line meets
        // the notch bottom (radius 149) asin(3/149) ~ 1.15 deg after the chuck reaches 40.
        double a = 40 * Math.PI / 180;
        var s = new SubstrateSpec
        {
            OffsetXMm = -3 * Math.Sin(a), OffsetYMm = 3 * Math.Cos(a),
            NotchAngleDeg = 40, NotchDepthMm = 1, NotchHalfWidthDeg = 0.2,
        };

        double deepest = Enumerable.Range(0, 50_000).Select(i => 38 + i * 0.0001)
            .MinBy(chuck => WaferGeometry.SensorReading(s, chuck));

        Assert.InRange(deepest, 40 + Math.Asin(3.0 / 149) * 180 / Math.PI - 0.01, 40 + Math.Asin(3.0 / 149) * 180 / Math.PI + 0.01);
    }
}
