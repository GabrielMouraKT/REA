using System.Globalization;
using System.Text.Json;

namespace REA.PacketAnalyzer;

/// <param name="Redacted">O valor original foi substituído; só se sabe que o cabeçalho existia.</param>
public sealed record HttpHeader(string Name, string Value, bool Redacted);

public sealed record HttpExchange(
    int Index,
    DateTimeOffset? Started,
    string Method,
    string Url,
    string? Host,
    string? HttpVersion,
    int? Status,
    string? StatusText,
    string? ContentType,
    long? RequestBodySize,
    long? ResponseBodySize,
    double? DurationMs,
    IReadOnlyList<HttpHeader> RequestHeaders,
    IReadOnlyList<HttpHeader> ResponseHeaders,
    int? TcpStream);

/// <summary>Conexão TLS vista na captura. O conteúdo é cifrado e não é lido; só o nome do servidor (SNI) aparece.</summary>
public sealed record EncryptedConnection(int TcpStream, string ServerName, string Destination);

public sealed record HttpAnalysis(
    string Source,
    IReadOnlyList<HttpExchange> Exchanges,
    IReadOnlyList<EncryptedConnection> Encrypted,
    IReadOnlyList<string> Notes);

/// <summary>
/// HTTP a partir de um arquivo HAR (exportado do navegador) ou de uma captura via TShark. Os valores de
/// Authorization, Proxy-Authorization, Cookie e Set-Cookie são sempre substituídos por "(oculto)"; corpos não
/// são copiados, apenas os tamanhos. HTTPS sem chaves aparece como conexão cifrada, nunca como conteúdo.
/// </summary>
public static class HttpAnalyzer
{
    public const string RedactedValue = "(oculto)";
    public const int MaxExchanges = 100_000;

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie",
    };

    public static HttpHeader Header(string name, string value) =>
        SensitiveHeaders.Contains(name) ? new HttpHeader(name, RedactedValue, true) : new HttpHeader(name, value, false);

    // ---------------- HAR ----------------

    public static async Task<HttpAnalysis> ReadHarAsync(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(fs, new JsonDocumentOptions { MaxDepth = 64 }, ct);
        return ParseHar(doc.RootElement);
    }

    public static HttpAnalysis ParseHar(JsonElement root)
    {
        if (!root.TryGetProperty("log", out var log) || !log.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Arquivo HAR inválido: falta log.entries.");

        var notes = new List<string>();
        var list = new List<HttpExchange>();
        var index = 0;
        foreach (var e in entries.EnumerateArray())
        {
            if (list.Count >= MaxExchanges) { notes.Add($"HAR com mais de {MaxExchanges} entradas; lista truncada."); break; }
            index++;
            if (!e.TryGetProperty("request", out var req) || req.ValueKind != JsonValueKind.Object)
            {
                notes.Add($"Entrada {index} sem 'request'; ignorada.");
                continue;
            }
            e.TryGetProperty("response", out var res);
            var url = Str(req, "url") ?? "";
            var reqHeaders = Headers(req);
            var resHeaders = res.ValueKind == JsonValueKind.Object ? Headers(res) : [];
            DateTimeOffset? started = DateTimeOffset.TryParse(Str(e, "startedDateTime"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
            int? status = res.ValueKind == JsonValueKind.Object && res.TryGetProperty("status", out var st) && st.TryGetInt32(out var sv) && sv > 0 ? sv : null;
            string? mime = null;
            long? resSize = null;
            if (res.ValueKind == JsonValueKind.Object && res.TryGetProperty("content", out var content))
            {
                mime = Str(content, "mimeType");
                resSize = NonNegative(content, "size");
            }
            list.Add(new HttpExchange(index, started, Str(req, "method") ?? "?", url,
                Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : reqHeaders.FirstOrDefault(h => h.Name.Equals("Host", StringComparison.OrdinalIgnoreCase))?.Value,
                Str(req, "httpVersion"), status, res.ValueKind == JsonValueKind.Object ? Str(res, "statusText") : null,
                mime, NonNegative(req, "bodySize"), resSize ?? (res.ValueKind == JsonValueKind.Object ? NonNegative(res, "bodySize") : null),
                e.TryGetProperty("time", out var t) && t.TryGetDouble(out var ms) && ms >= 0 ? ms : null,
                reqHeaders, resHeaders, null));
        }
        if (list.Any(x => x.RequestHeaders.Concat(x.ResponseHeaders).Any(h => h.Redacted)))
            notes.Add("Valores de Authorization, Cookie e Set-Cookie foram ocultados.");
        return new HttpAnalysis("HAR", list, [], notes);
    }

    private static List<HttpHeader> Headers(JsonElement owner)
    {
        if (!owner.TryGetProperty("headers", out var hs) || hs.ValueKind != JsonValueKind.Array) return [];
        return hs.EnumerateArray()
            .Where(h => h.ValueKind == JsonValueKind.Object)
            .Select(h => Header(Str(h, "name") ?? "", Str(h, "value") ?? ""))
            .Where(h => h.Name.Length > 0)
            .ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? NonNegative(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) && n >= 0 ? n : null;

    // ---------------- TShark ----------------

    private const char Aggregator = '\u001F';

    public static async Task<HttpAnalysis> FromCaptureAsync(string capturePath, CancellationToken ct = default)
    {
        var tshark = TsharkCrossCheck.Locate()
                     ?? throw new InvalidOperationException("TShark não encontrado. Instale o Wireshark ou defina REA_TSHARK.");
        string[] fields =
        [
            "frame.number", "frame.time_epoch", "tcp.stream", "http.request.method", "http.request.full_uri", "http.host",
            "http.request.version", "http.response.code", "http.response.phrase", "http.content_type",
            "http.content_length", "http.request_in", "http.time", "http.request.line", "http.response.line",
            "http.response.version",
        ];
        var args = new List<string> { "-r", capturePath, "-Y", "http.request || http.response", "-T", "fields",
            "-E", "separator=/t", "-E", "occurrence=a", "-E", $"aggregator={Aggregator}" };
        foreach (var f in fields) { args.Add("-e"); args.Add(f); }
        var httpOut = await TsharkCrossCheck.Run(tshark, args, ct);

        var tlsOut = await TsharkCrossCheck.Run(tshark,
        [
            "-r", capturePath, "-Y", "tls.handshake.type == 1", "-T", "fields", "-E", "separator=/t",
            "-e", "tcp.stream", "-e", "ip.dst", "-e", "ipv6.dst", "-e", "tcp.dstport", "-e", "tls.handshake.extensions_server_name",
        ], ct);

        return ParseTshark(httpOut, tlsOut);
    }

    /// <summary>Interpreta a saída de campos do TShark (separada para ser testável sem o TShark).</summary>
    public static HttpAnalysis ParseTshark(string httpFields, string tlsFields)
    {
        var requests = new Dictionary<long, HttpExchange>();
        var order = new List<long>();
        var notes = new List<string>();
        var orphanResponses = 0;

        foreach (var line in Lines(httpFields))
        {
            var c = line.Split('\t');
            if (c.Length < 16 || !long.TryParse(c[0], CultureInfo.InvariantCulture, out var frame)) continue;
            if (c[3].Length > 0)
            {
                if (requests.Count >= MaxExchanges) { notes.Add($"Mais de {MaxExchanges} requisições; lista truncada."); break; }
                DateTimeOffset? at = double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var epoch)
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)(epoch * 1000)) : null;
                // Várias requisições no mesmo quadro (pipelining) chegam agregadas; registra a primeira e avisa.
                if (c[3].Contains(Aggregator)) notes.Add($"Quadro {frame}: várias requisições no mesmo pacote; só a primeira foi listada.");
                requests[frame] = new HttpExchange(order.Count + 1, at, First(c[3]), First(c[4]), Empty(First(c[5])),
                    Empty(First(c[6])), null, null, null, Long(First(c[10])), null, null, HeaderLines(c[13]), [], Int(c[2]));
                order.Add(frame);
            }
            else if (c[7].Length > 0)
            {
                if (!long.TryParse(First(c[11]), CultureInfo.InvariantCulture, out var reqFrame) || !requests.TryGetValue(reqFrame, out var req))
                {
                    orphanResponses++;
                    continue;
                }
                requests[reqFrame] = req with
                {
                    Status = Int(First(c[7])),
                    StatusText = Empty(First(c[8])),
                    ContentType = Empty(First(c[9])),
                    ResponseBodySize = Long(First(c[10])),
                    DurationMs = double.TryParse(First(c[12]), NumberStyles.Float, CultureInfo.InvariantCulture, out var secs) ? secs * 1000 : null,
                    ResponseHeaders = HeaderLines(c[14]),
                };
            }
        }
        if (orphanResponses > 0)
            notes.Add($"{orphanResponses} resposta(s) sem a requisição correspondente na captura (captura começou no meio).");

        var tls = new Dictionary<int, EncryptedConnection>();
        foreach (var line in Lines(tlsFields))
        {
            var c = line.Split('\t');
            if (c.Length < 5 || Int(c[0]) is not { } stream || tls.ContainsKey(stream)) continue;
            var host = c[1].Length > 0 ? c[1] : $"[{c[2]}]";
            tls[stream] = new EncryptedConnection(stream, c[4].Length > 0 ? c[4] : "(sem SNI)", $"{host}:{c[3]}");
        }
        if (tls.Count > 0)
            notes.Add($"{tls.Count} conexão(ões) HTTPS/TLS: conteúdo cifrado, não lido. Só o nome do servidor (SNI) é visível.");

        var exchanges = order.Select(f => requests[f]).ToList();
        if (exchanges.Any(x => x.RequestHeaders.Concat(x.ResponseHeaders).Any(h => h.Redacted)))
            notes.Add("Valores de Authorization, Cookie e Set-Cookie foram ocultados.");
        return new HttpAnalysis("TShark", exchanges, tls.Values.OrderBy(t => t.TcpStream).ToList(), notes);
    }

    private static List<HttpHeader> HeaderLines(string aggregated)
    {
        var result = new List<HttpHeader>();
        foreach (var raw in aggregated.Split(Aggregator, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Replace("\\r\\n", "").Replace("\r", "").Replace("\n", "").Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            result.Add(Header(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }
        return result;
    }

    private static IEnumerable<string> Lines(string s) =>
        s.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);

    private static string First(string s) { var i = s.IndexOf(Aggregator); return i < 0 ? s : s[..i]; }
    private static string? Empty(string s) => s.Length == 0 ? null : s;
    private static int? Int(string s) => int.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static long? Long(string s) => long.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v : null;
}
