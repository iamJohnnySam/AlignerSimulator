using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AlignerSimulator;

/// <summary>
/// The simulator as a pre-aligner on the network, for Flow's <c>aligner</c> device type with
/// <c>binding: tcp</c> (protocol <c>commspec</c>).  It answers:
/// <list type="bullet">
/// <item><c>ALIGN</c> or <c>ALIGN {deg}</c>: scan a rotation, measure, turn the feature to the target
/// (<see cref="DefaultTargetDeg"/> when none is given), and reply <c>Ack,angle={deg}</c>, which Flow
/// Core reads as the payload's orientation.  <c>Nak,Bsy</c> while aligning; <c>Nak,Err,E101</c> when
/// no edge is in range, <c>Nak,Err,E102</c> when no feature stands out.</item>
/// <item><c>STATUS?</c>: <c>Ack,Idle</c> or <c>Ack,Busy</c>.</item>
/// </list>
/// The reply comes once the motion is done, in real time at the chuck speed.
/// </summary>
public sealed class AlignerDevice : IDisposable
{
    private readonly Func<AlignerState> _readState;
    private readonly Random _random = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private int _busy;

    public AlignerDevice(Func<AlignerState> readState) => _readState = readState;

    /// <summary>Where <c>ALIGN</c> with no argument puts the feature, in the tool frame.</summary>
    public double DefaultTargetDeg { get; set; }

    public bool IsListening => _listener is not null;

    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    /// <summary>Raised when an alignment begins moving the chuck, with its whole plan.</summary>
    public event Action<AlignResult>? AlignStarted;

    /// <summary>Raised when an alignment ends, just before its reply is sent.</summary>
    public event Action<AlignResult>? AlignFinished;

    public event Action<string>? Log;

    /// <summary>Listens on <paramref name="address"/> (loopback when null); port 0 picks a free one.</summary>
    public void Start(int port, IPAddress? address = null)
    {
        if (_listener is not null) return;
        var listener = new TcpListener(address ?? IPAddress.Loopback, port);
        listener.Start();
        _listener = listener;
        _stop = new CancellationTokenSource();
        Log?.Invoke($"listening on {listener.LocalEndpoint}");
        _ = AcceptLoop(listener, _stop.Token);
    }

    public void Stop()
    {
        if (_listener is null) return;
        _stop!.Cancel();
        _listener.Stop();
        _listener = null;
        Log?.Invoke("stopped");
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop); }
            catch (Exception) when (stop.IsCancellationRequested) { return; }
            catch (ObjectDisposedException) { return; }
            _ = Serve(client, stop);
        }
    }

    private async Task Serve(TcpClient client, CancellationToken stop)
    {
        string who = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Log?.Invoke($"{who} connected");
        using var connection = client;
        using var registration = stop.Register(client.Close);
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.ASCII);
        var writeGate = new SemaphoreSlim(1, 1);

        async Task Reply(string id, string body)
        {
            string line = CommSpec.Format(id, body);
            await writeGate.WaitAsync();
            try
            {
                var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
                await stream.WriteAsync(bytes);
                Log?.Invoke($"→ {line}");
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            finally { writeGate.Release(); }
        }

        try
        {
            while (await reader.ReadLineAsync(stop) is { } line)
            {
                if (line.Length == 0) continue;
                Log?.Invoke($"← {line}");
                if (!CommSpec.TryParse(line, out var request))
                    continue;
                if (!request.ChecksumOk)
                {
                    await Reply(request.Id, "Nak,CHECKSUM");
                    continue;
                }
                // ALIGN replies when the motion ends, so it runs on while further lines are read.
                _ = Handle(request, Reply, stop);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        Log?.Invoke($"{who} disconnected");
    }

    private async Task Handle(CommSpec.Request request, Func<string, string, Task> reply, CancellationToken stop)
    {
        string[] words = request.Body.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string command = words.Length > 0 ? words[0].ToUpperInvariant() : "";

        switch (command)
        {
            case "STATUS?":
                await reply(request.Id, Volatile.Read(ref _busy) == 1 ? "Ack,Busy" : "Ack,Idle");
                return;

            case "ALIGN":
                double target = DefaultTargetDeg;
                if (words.Length > 1 && !double.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out target))
                {
                    await reply(request.Id, "Nak,PARAM");
                    return;
                }
                if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                {
                    await reply(request.Id, "Nak,Bsy");
                    return;
                }
                try
                {
                    var result = Aligner.Align(_readState(), target, _random);
                    AlignStarted?.Invoke(result);
                    await Task.Delay(result.Duration, stop);
                    AlignFinished?.Invoke(result);
                    await reply(request.Id, result.Succeeded
                        ? string.Create(CultureInfo.InvariantCulture, $"Ack,angle={result.TargetDeg:F2}")
                        : $"Nak,Err,{result.FailureCode}");
                }
                catch (OperationCanceledException) { }
                finally { Volatile.Write(ref _busy, 0); }
                return;

            default:
                await reply(request.Id, "Nak,UNKNOWN");
                return;
        }
    }
}
