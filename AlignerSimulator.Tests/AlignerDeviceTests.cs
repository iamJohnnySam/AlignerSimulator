using System.Net.Sockets;
using System.Text;
using Xunit;

namespace AlignerSimulator.Tests;

public class CommSpecTests
{
    [Fact]
    public void A_formatted_line_parses_back_with_its_checksum()
    {
        string line = CommSpec.Format("00A1", "Ack,angle=90.00");

        Assert.True(CommSpec.TryParse(line, out var parsed));
        Assert.Equal(new CommSpec.Request("00A1", "Ack,angle=90.00", true), parsed);
    }

    [Fact]
    public void The_checksum_is_the_xor_of_the_bracketed_characters_in_upper_hex()
    {
        byte xor = 0;
        foreach (char c in "0001,ALIGN") xor ^= (byte)c;

        Assert.Equal($"<0001,ALIGN>{xor:X2}", CommSpec.Format("0001", "ALIGN"));
    }

    [Fact]
    public void A_wrong_checksum_is_flagged()
    {
        Assert.True(CommSpec.TryParse("<0001,ALIGN>00", out var parsed));
        Assert.False(parsed.ChecksumOk);
    }
}

public sealed class AlignerDeviceTests : IDisposable
{
    private readonly AlignerDevice _device;
    private readonly TcpClient _client = new();
    private readonly StreamReader _reader;
    private readonly Stream _stream;

    public AlignerDeviceTests()
    {
        // 600 RPM: a scan is 0.1 s, 10,000 samples at 100 kHz.
        var s = new SubstrateSpec { NotchAngleDeg = 45, OffsetXMm = 0.5, NoiseMm = 0.01 };
        _device = new AlignerDevice(() => new AlignerState(s, 0, 600, 100_000)) { DefaultTargetDeg = 0 };
        _device.Start(0);
        _client.Connect("127.0.0.1", _device.Port);
        _stream = _client.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII);
    }

    public void Dispose()
    {
        _client.Dispose();
        _device.Dispose();
    }

    private async Task Send(string id, string body) =>
        await _stream.WriteAsync(Encoding.ASCII.GetBytes(CommSpec.Format(id, body) + "\r\n"), TestContext.Current.CancellationToken);

    private async Task<CommSpec.Request> Receive()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        string line = await _reader.ReadLineAsync(timeout.Token) ?? throw new IOException("closed");
        Assert.True(CommSpec.TryParse(line, out var reply), line);
        Assert.True(reply.ChecksumOk, line);
        return reply;
    }

    [Fact]
    public async Task Align_replies_the_angle_flow_reads_as_the_orientation()
    {
        AlignResult? finished = null;
        _device.AlignFinished += r => finished = r;

        await Send("0001", "ALIGN");
        var reply = await Receive();

        Assert.Equal(new CommSpec.Request("0001", "Ack,angle=0.00", true), reply);
        Assert.InRange(finished!.ErrorDeg!.Value, -0.05, 0.05);
    }

    [Fact]
    public async Task Align_takes_a_target_and_refuses_a_second_while_busy()
    {
        await Send("0002", "ALIGN 90");
        await Send("0003", "ALIGN");

        var first = await Receive();
        var second = await Receive();

        Assert.Equal(new CommSpec.Request("0003", "Nak,Bsy", true), first);
        Assert.Equal(new CommSpec.Request("0002", "Ack,angle=90.00", true), second);
    }

    [Fact]
    public async Task Status_unknown_commands_and_bad_checksums_are_answered()
    {
        await Send("0004", "STATUS?");
        Assert.Equal("Ack,Idle", (await Receive()).Body);

        await Send("0005", "HOME");
        Assert.Equal("Nak,UNKNOWN", (await Receive()).Body);

        await _stream.WriteAsync(Encoding.ASCII.GetBytes("<0006,ALIGN>00\r\n"), TestContext.Current.CancellationToken);
        Assert.Equal(new CommSpec.Request("0006", "Nak,CHECKSUM", true), await Receive());
    }
}
