using System.Globalization;

namespace AlignerSimulator;

/// <summary>
/// Flow's CommSpec line framing (Flow <c>framings/commspec.yaml</c>), as a device speaks it:
/// requests and replies are <c>&lt;{id},{body}&gt;{checksum}\r\n</c>, the id is four hex digits the
/// reply echoes, and the checksum is the XOR of the characters between the angle brackets in
/// upper-case hex.  A reply is one <c>Ack[,{data}]</c> carrying the result, or a <c>Nak</c>:
/// <c>Nak,Bsy</c> (busy), <c>Nak,Err,{code}</c> (failed) or <c>Nak,{code}</c> (refused).
/// </summary>
public static class CommSpec
{
    public readonly record struct Request(string Id, string Body, bool ChecksumOk);

    public static bool TryParse(string line, out Request request)
    {
        request = default;
        int start = line.IndexOf('<');
        int end = start < 0 ? -1 : line.IndexOf('>', start + 1);
        if (end < 0) return false;

        string inner = line[(start + 1)..end];
        int sep = inner.IndexOf(',');
        if (sep <= 0) return false;

        string tail = line[(end + 1)..].Trim();
        bool checksumOk = tail.Length >= 2
            && byte.TryParse(tail[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte sent)
            && sent == Checksum(inner);

        request = new Request(inner[..sep], inner[(sep + 1)..], checksumOk);
        return true;
    }

    public static string Format(string id, string body)
    {
        string inner = $"{id},{body}";
        return $"<{inner}>{Checksum(inner):X2}";
    }

    public static byte Checksum(string inner)
    {
        byte value = 0;
        foreach (char c in inner)
            value ^= (byte)c;
        return value;
    }
}
