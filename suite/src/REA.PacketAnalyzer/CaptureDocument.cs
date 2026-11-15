using System.Security.Cryptography;
using System.Text;
using REA.Core;

namespace REA.PacketAnalyzer;

/// <summary>Filtro de exibição (aplicado na leitura, não altera o arquivo). Campos vazios = sem restrição.</summary>
public sealed record PacketFilter(
    string? Protocol = null,
    string? Address = null,
    int? Port = null,
    string? Text = null,
    bool OnlyWithPayload = false)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Protocol) && string.IsNullOrWhiteSpace(Address) && Port is null
                           && string.IsNullOrWhiteSpace(Text) && !OnlyWithPayload;

    public bool Matches(PacketSummary s, ReadOnlySpan<byte> raw)
    {
        if (!string.IsNullOrWhiteSpace(Protocol) && !s.Protocol.Equals(Protocol.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(Address))
        {
            var a = Address.Trim();
            if (!s.Source.Equals(a, StringComparison.OrdinalIgnoreCase) && !s.Destination.Equals(a, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (Port is { } p && s.SourcePort != p && s.DestinationPort != p) return false;
        if (OnlyWithPayload && s.PayloadLength == 0) return false;
        if (!string.IsNullOrWhiteSpace(Text))
        {
            var t = Text.Trim();
            if (!s.Info.Contains(t, StringComparison.OrdinalIgnoreCase) && !ContainsAscii(raw, t)) return false;
        }
        return true;
    }

    private static bool ContainsAscii(ReadOnlySpan<byte> raw, string text)
    {
        var needle = Encoding.ASCII.GetBytes(text.ToLowerInvariant());
        if (needle.Length == 0 || needle.Length > raw.Length) return false;
        for (var i = 0; i <= raw.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length && ok; j++)
            {
                var b = raw[i + j];
                if (b is >= (byte)'A' and <= (byte)'Z') b = (byte)(b + 32);
                ok = b == needle[j];
            }
            if (ok) return true;
        }
        return false;
    }
}

public sealed record PacketIndex(string Format, IReadOnlyList<long> Offsets, string? TruncationReason);

/// <summary>
/// Acesso paginado a um arquivo de captura. Guarda só o deslocamento de cada pacote (8 bytes/pacote);
/// filtros e páginas são lidos do disco sob demanda.
/// </summary>
public sealed class CaptureDocument
{
    public string Path { get; }
    public PacketIndex Index { get; }
    public int PacketCount => Index.Offsets.Count;

    private CaptureDocument(string path, PacketIndex index)
    {
        Path = path;
        Index = index;
    }

    public static Task<CaptureDocument> OpenAsync(string path, CancellationToken ct = default) => Task.Run(() =>
    {
        using var reader = new CaptureFileReader(path);
        var offsets = new List<long>();
        foreach (var f in reader.ReadAll(ct)) offsets.Add(f.Offset);
        return new CaptureDocument(path, new PacketIndex(reader.Format, offsets, reader.TruncationReason));
    }, ct);

    /// <summary>Números (1-based) dos pacotes que atendem ao filtro, lendo o arquivo em fluxo.</summary>
    public Task<IReadOnlyList<long>> FilterAsync(PacketFilter filter, CancellationToken ct = default) => Task.Run<IReadOnlyList<long>>(() =>
    {
        if (filter.IsEmpty) return Enumerable.Range(1, PacketCount).Select(i => (long)i).ToList();
        var result = new List<long>();
        using var reader = new CaptureFileReader(Path);
        foreach (var f in reader.ReadAll(ct))
        {
            if (f.Number > PacketCount) break;
            if (filter.Matches(PacketDecoder.Summarize(f), f.Data)) result.Add(f.Number);
        }
        return result;
    }, ct);

    public Task<IReadOnlyList<PacketSummary>> ReadSummariesAsync(IEnumerable<long> numbers, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<PacketSummary>>(() =>
        {
            var list = new List<PacketSummary>();
            using var reader = new CaptureFileReader(Path);
            foreach (var n in numbers)
            {
                ct.ThrowIfCancellationRequested();
                if (Read(reader, n) is { } f) list.Add(PacketDecoder.Summarize(f));
            }
            return list;
        }, ct);

    public Task<PacketDetail?> ReadDetailAsync(long number, CancellationToken ct = default) => Task.Run(() =>
    {
        using var reader = new CaptureFileReader(Path);
        return Read(reader, number) is { } f ? PacketDecoder.Detail(f) : null;
    }, ct);

    public Task<IReadOnlyList<TcpStreamResult>> ReassembleTcpAsync(CancellationToken ct = default) =>
        Task.Run(() => ReassembleTcp(Path, keepPayload: true, ct), ct);

    public static IReadOnlyList<TcpStreamResult> ReassembleTcp(string path, bool keepPayload, CancellationToken ct)
    {
        var r = new TcpReassembler(keepPayload);
        using var reader = new CaptureFileReader(path);
        foreach (var f in reader.ReadAll(ct))
            if (PacketDecoder.TcpSegment(f, PacketDecoder.Parse(f)) is { } seg) r.Add(seg);
        return r.Build();
    }

    private CapturedFrame? Read(CaptureFileReader reader, long number)
    {
        if (number < 1 || number > PacketCount) return null;
        return reader.ReadAt(Index.Offsets[(int)number - 1], number);
    }
}

public sealed class CaptureFileAnalyzer : ICaptureFileAnalyzer
{
    public string? DetectFormat(string path) => CaptureFileReader.Detect(path);

    public Task<CaptureAnalysis> AnalyzeAsync(string path, CancellationToken ct = default) => Task.Run(() =>
    {
        using var reader = new CaptureFileReader(path);
        var protocols = new Dictionary<string, long>();
        var reassembler = new TcpReassembler(keepPayload: false);
        long packets = 0, bytes = 0;
        DateTimeOffset? first = null, last = null;
        foreach (var f in reader.ReadAll(ct))
        {
            packets++;
            bytes += f.OriginalLength;
            first ??= f.Timestamp;
            last = f.Timestamp;
            var packet = PacketDecoder.Parse(f);
            var s = PacketDecoder.Summarize(f, packet);
            protocols[s.Protocol] = protocols.GetValueOrDefault(s.Protocol) + 1;
            if (PacketDecoder.TcpSegment(f, packet) is { } seg) reassembler.Add(seg with { Payload = [] });
        }
        return new CaptureAnalysis(reader.Format, packets, bytes, reassembler.StreamCount, first, last, protocols,
            reader.TruncationReason is not null, reader.TruncationReason);
    }, ct);
}

public sealed class Sha256FileHasher : IFileHasher
{
    public async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash);
    }
}
