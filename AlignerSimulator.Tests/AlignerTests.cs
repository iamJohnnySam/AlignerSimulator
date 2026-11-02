using Xunit;

namespace AlignerSimulator.Tests;

public class AlignerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(271.5)]
    public void Turns_the_notch_to_the_target(double target)
    {
        var s = new SubstrateSpec { NotchAngleDeg = 133, OffsetXMm = 1.2, OffsetYMm = -0.7, NoiseMm = 0.01 };
        var state = new AlignerState(s, ChuckAngleDeg: 17, ChuckSpeedRpm: 60, SamplingRateHz: 10_000);

        var result = Aligner.Align(state, target, new Random(3));

        Assert.True(result.Succeeded);
        Assert.InRange(result.ErrorDeg!.Value, -0.05, 0.05);
        Assert.Equal(TimeSpan.FromSeconds(1), result.ScanTime);
        Assert.True(result.TurnTime <= TimeSpan.FromSeconds(0.5), "the turn takes the short way round");
    }

    [Fact]
    public void Fails_E102_when_no_feature_stands_out()
    {
        var s = new SubstrateSpec { NotchDepthMm = 0, NoiseMm = 0.01 };

        var result = Aligner.Align(new AlignerState(s, 0, 60, 1000), 0, new Random(1));

        Assert.False(result.Succeeded);
        Assert.Equal(Aligner.NoFeature, result.FailureCode);
        Assert.Equal(TimeSpan.Zero, result.TurnTime);
    }

    [Fact]
    public void Fails_E101_when_the_edge_is_out_of_the_sensors_span()
    {
        // 40 mm off centre: the edge swings 14 +/- 40 mm and the 0-28 mm sensor saturates most of the turn.
        var s = new SubstrateSpec { OffsetXMm = 40 };

        var result = Aligner.Align(new AlignerState(s, 0, 60, 1000), 0, new Random(1));

        Assert.Equal(Aligner.NoEdge, result.FailureCode);
    }
}
