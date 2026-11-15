using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace REA.PacketAnalyzer;

public sealed record StreamComparison(int Index, string Endpoints, bool ClientToServerMatches, bool ServerToClientMatches,
    int ReaClientBytes, int TsharkClientBytes, int ReaServerBytes, int TsharkServerBytes);

public sealed record CrossCheckReport(
    string TsharkVersion,
    long ReaPackets,
    long TsharkPackets,
    int ReaTcpStreams,
    int TsharkTcpConversations,
    IReadOnlyList<StreamComparison> Streams,
    int StreamsNotCompared)
{
    public bool PacketsMatch => ReaPackets == TsharkPackets;
    public bool StreamsMatch => ReaTcpStreams == TsharkTcpConversations;
    public bool PayloadsMatch => Streams.All(s => s.ClientToServerMatches && s.ServerToClientMatches);
    public bool AllMatch => PacketsMatch && StreamsMatch && PayloadsMatch;
}

/// <summary>
/// Validação cruzada com o TShark (ferramenta independente): contagem de quadros, conversas TCP e
/// conteúdo de cada fluxo (follow,tcp,raw). Serve como evidência de que a leitura/reconstrução do REA está correta.
/// </summary>
public static class TsharkCrossCheck
{
    public static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable("REA_TSHARK");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var fromPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim().Trim('"')), "tshark.exe")).FirstOrDefault(File.Exists);
        if (fromPath is not null) return fromPath;
        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Wireshark", "tshark.exe");
        return File.Exists(pf) ? pf : null;
    }

    public static async Task<CrossCheckReport> RunAsync(string capturePath, int maxStreams = 20, CancellationToken ct = default)
    {
        var tshark = Locate() ?? throw new InvalidOperationException("TShark não encontrado. Instale o Wireshark ou defina REA_TSHARK.");
        var version = FirstLine(await Run(tshark, ["--version"], ct));

        var frames = await Run(tshark, ["-r", capturePath, "-T", "fields", "-e", "frame.number"], ct);
        var tsharkPackets = frames.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LongLength;

        var conv = await Run(tshark, ["-r", capturePath, "-q", "-z", "conv,tcp"], ct);
        var tsharkConversations = conv.Split('\n').Count(l => l.Contains("<->", StringComparison.Ordinal));

        long reaPackets;
        using (var reader = new CaptureFileReader(capturePath)) reaPackets = reader.ReadAll(ct).LongCount();
        var streams = CaptureDocument.ReassembleTcp(capturePath, keepPayload: true, ct);

        var comparisons = new List<StreamComparison>();
        foreach (var s in streams.Take(maxStreams))
        {
            var follow = await Run(tshark, ["-r", capturePath, "-q", "-z", $"follow,tcp,raw,{s.Index}"], ct);
            var (c2s, s2c) = ParseFollowRaw(follow);
            var reaC2s = Contiguous(s.ClientToServer);
            var reaS2c = Contiguous(s.ServerToClient);
            comparisons.Add(new StreamComparison(s.Index, $"{s.Client} ⇄ {s.Server}",
                reaC2s.AsSpan().SequenceEqual(c2s), reaS2c.AsSpan().SequenceEqual(s2c),
                reaC2s.Length, c2s.Length, reaS2c.Length, s2c.Length));
        }

        return new CrossCheckReport(version, reaPackets, tsharkPackets, streams.Count, tsharkConversations, comparisons,
            Math.Max(0, streams.Count - maxStreams));
    }

    /// <summary>O TShark entrega só os bytes observados; o REA preenche lacunas com zeros. Remove as lacunas antes de comparar.</summary>
    private static byte[] Contiguous(TcpDirectionResult d)
    {
        if (d.Gaps.Count == 0) return d.Data;
        var output = new MemoryStream();
        long pos = 0;
        foreach (var gap in d.Gaps.Where(g => g.Offset < d.Data.Length).OrderBy(g => g.Offset))
        {
            output.Write(d.Data.AsSpan((int)pos, (int)(gap.Offset - pos)));
            pos = Math.Min(d.Data.Length, gap.Offset + gap.Length);
        }
        output.Write(d.Data.AsSpan((int)pos));
        return output.ToArray();
    }

    /// <summary>Formato "follow,tcp,raw": linhas hex; as do nó 1 (servidor) começam com TAB.</summary>
    public static (byte[] ClientToServer, byte[] ServerToClient) ParseFollowRaw(string output)
    {
        var c2s = new MemoryStream();
        var s2c = new MemoryStream();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('=') || line.StartsWith("Follow", StringComparison.Ordinal)
                || line.StartsWith("Filter", StringComparison.Ordinal) || line.StartsWith("Node", StringComparison.Ordinal)) continue;
            var server = line.StartsWith('\t');
            var hex = line.Trim();
            if (hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit)) continue;
            (server ? s2c : c2s).Write(Convert.FromHexString(hex));
        }
        return (c2s.ToArray(), s2c.ToArray());
    }

    private static async Task<string> Run(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar o TShark.");
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"TShark retornou {p.ExitCode.ToString(CultureInfo.InvariantCulture)}: {FirstLine(await stderr)}");
        return await stdout;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
}
