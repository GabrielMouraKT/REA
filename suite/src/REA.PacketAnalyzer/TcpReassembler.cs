namespace REA.PacketAnalyzer;

public sealed record TcpEndpoint(string Address, int Port)
{
    public override string ToString() => Address.Contains(':') ? $"[{Address}]:{Port}" : $"{Address}:{Port}";
}

public sealed record TcpGap(long Offset, long Length, string Reason);

public sealed record TcpTimelineEntry(long Frame, DateTimeOffset Timestamp, bool FromClient, string Flags, int PayloadLength, long RelativeSequence);

public sealed record TcpDirectionResult(
    TcpEndpoint From,
    TcpEndpoint To,
    byte[] Data,
    long PayloadBytesSeen,
    int Segments,
    int Retransmissions,
    int PartialOverlaps,
    IReadOnlyList<TcpGap> Gaps,
    bool SynSeen,
    bool FinSeen,
    bool Truncated);

public enum TcpStreamCompleteness
{
    /// <summary>Handshake e encerramento observados, sem lacunas.</summary>
    Complete,
    /// <summary>Sem lacunas nos bytes observados, mas início e/ou fim da conexão não foram capturados.</summary>
    NoGapsPartialConnection,
    /// <summary>Há segmentos ausentes: o fluxo NÃO foi completamente reconstruído.</summary>
    Incomplete,
}

public sealed record TcpStreamResult(
    int Index,
    TcpEndpoint Client,
    TcpEndpoint Server,
    long FirstFrame,
    long LastFrame,
    DateTimeOffset Start,
    DateTimeOffset End,
    int Packets,
    bool ResetSeen,
    TcpDirectionResult ClientToServer,
    TcpDirectionResult ServerToClient,
    IReadOnlyList<TcpTimelineEntry> Timeline,
    TcpStreamCompleteness Completeness)
{
    public string CompletenessText => Completeness switch
    {
        TcpStreamCompleteness.Complete => "Completo",
        TcpStreamCompleteness.NoGapsPartialConnection => "Sem lacunas (início/fim da conexão não capturados)",
        _ => $"Incompleto: {ClientToServer.Gaps.Count + ServerToClient.Gaps.Count} lacuna(s), " +
             $"{ClientToServer.Gaps.Sum(g => g.Length) + ServerToClient.Gaps.Sum(g => g.Length)} byte(s) ausente(s)",
    };
}

/// <summary>
/// Reconstrói fluxos TCP: agrupa por conexão (4-tupla), reordena por número de sequência, detecta
/// retransmissões e sobreposições, e registra lacunas — inclusive bytes confirmados (ACK) pelo outro lado
/// que não aparecem na captura. Fluxos com lacuna nunca são marcados como completos.
/// </summary>
public sealed class TcpReassembler(bool keepPayload = true, long maxBytesPerDirection = 32L * 1024 * 1024)
{
    private readonly Dictionary<(TcpEndpoint, TcpEndpoint), StreamState> _streams = [];
    private readonly List<StreamState> _ordered = [];

    public int StreamCount => _ordered.Count;

    public void Add(TcpSegmentInfo s)
    {
        var src = new TcpEndpoint(s.SourceIp, s.SourcePort);
        var dst = new TcpEndpoint(s.DestinationIp, s.DestinationPort);
        var key = Canonical(src, dst);
        if (!_streams.TryGetValue(key, out var stream))
        {
            // Cliente = quem envia o SYN sem ACK; sem handshake, quem aparece primeiro.
            var srcIsClient = !(s.Syn && s.Ack);
            stream = new StreamState(_ordered.Count, srcIsClient ? src : dst, srcIsClient ? dst : src, s.Frame, s.Timestamp);
            _streams[key] = stream;
            _ordered.Add(stream);
        }
        stream.Add(s, src == stream.Client, keepPayload);
    }

    public IReadOnlyList<TcpStreamResult> Build() => _ordered.Select(s => s.Build(maxBytesPerDirection)).ToList();

    private static (TcpEndpoint, TcpEndpoint) Canonical(TcpEndpoint a, TcpEndpoint b) =>
        string.CompareOrdinal(a.ToString(), b.ToString()) <= 0 ? (a, b) : (b, a);

    private sealed class StreamState
    {
        public TcpEndpoint Client { get; }
        private readonly int index;
        private readonly TcpEndpoint server;
        private readonly long firstFrame;
        private readonly DateTimeOffset start;
        private readonly DirectionState _c2s = new();
        private readonly DirectionState _s2c = new();
        private readonly List<TcpTimelineEntry> _timeline = [];
        private long _lastFrame;
        private DateTimeOffset _end;
        private bool _rst;

        public StreamState(int index, TcpEndpoint client, TcpEndpoint server, long firstFrame, DateTimeOffset start)
        {
            this.index = index;
            Client = client;
            this.server = server;
            this.firstFrame = firstFrame;
            this.start = start;
            _lastFrame = firstFrame;
            _end = start;
        }

        public void Add(TcpSegmentInfo s, bool fromClient, bool keepPayload)
        {
            var dir = fromClient ? _c2s : _s2c;
            var peer = fromClient ? _s2c : _c2s;
            dir.Add(s, keepPayload);
            if (s.Ack) peer.ObserveAck(s.Acknowledgment);
            _rst |= s.Rst;
            _lastFrame = s.Frame;
            if (s.Timestamp > _end) _end = s.Timestamp;
            var flags = string.Join(", ", new[] { s.Syn ? "SYN" : null, s.Fin ? "FIN" : null, s.Rst ? "RST" : null, s.Psh ? "PSH" : null, s.Ack ? "ACK" : null }
                .Where(f => f is not null));
            _timeline.Add(new TcpTimelineEntry(s.Frame, s.Timestamp, fromClient, flags, s.Payload.Length, dir.Relative(s.Sequence)));
        }

        public TcpStreamResult Build(long maxBytes)
        {
            var c2s = _c2s.Build(Client, server, maxBytes);
            var s2c = _s2c.Build(server, Client, maxBytes);
            var gaps = c2s.Gaps.Count + s2c.Gaps.Count;
            var completeness = gaps > 0
                ? TcpStreamCompleteness.Incomplete
                : c2s.SynSeen && s2c.SynSeen && ((c2s.FinSeen && s2c.FinSeen) || _rst)
                    ? TcpStreamCompleteness.Complete
                    : TcpStreamCompleteness.NoGapsPartialConnection;
            return new TcpStreamResult(index, Client, server, firstFrame, _lastFrame, start, _end, _timeline.Count, _rst,
                c2s, s2c, _timeline, completeness);
        }
    }

    private sealed class DirectionState
    {
        private readonly List<(uint Seq, byte[] Data, long Frame)> _segments = [];
        private uint? _isn;          // número de sequência inicial (do SYN)
        private uint? _firstSeq;     // primeiro número observado (sem SYN)
        private uint? _maxAckFromPeer;
        private long _payloadSeen;
        private bool _fin;

        public void Add(TcpSegmentInfo s, bool keepPayload)
        {
            if (s.Syn) _isn ??= s.Sequence;
            _fin |= s.Fin;
            if (s.Payload.Length > 0)
            {
                _firstSeq ??= s.Sequence;
                _payloadSeen += s.Payload.Length;
                _segments.Add((s.Sequence, keepPayload ? s.Payload : new byte[s.Payload.Length], s.Frame));
            }
        }

        public void ObserveAck(uint ack)
        {
            if (_maxAckFromPeer is null || (int)(ack - _maxAckFromPeer.Value) > 0) _maxAckFromPeer = ack;
        }

        private uint? Base => _isn is { } isn ? isn + 1 : _firstSeq;

        public long Relative(uint seq) => Base is { } b ? (int)(seq - b) : 0;

        public TcpDirectionResult Build(TcpEndpoint from, TcpEndpoint to, long maxBytes)
        {
            var baseSeq = Base;
            if (baseSeq is null)
                return new TcpDirectionResult(from, to, [], 0, 0, 0, 0, [], _isn is not null, _fin, false);

            // Sem SYN, o segmento mais antigo pode estar antes do primeiro observado (reordenação): usa o menor.
            var b = baseSeq.Value;
            if (_isn is null && _segments.Count > 0)
            {
                var min = _segments.Min(s => (long)(int)(s.Seq - b));
                if (min < 0) b = (uint)(b + min);
            }

            var ordered = _segments
                .Select(s => (Start: (long)(int)(s.Seq - b), s.Data, s.Frame))
                .Where(s => s.Start >= 0) // dados antes do ISN: inválidos
                .OrderBy(s => s.Start).ThenBy(s => s.Frame)
                .ToList();

            var output = new MemoryStream();
            var gaps = new List<TcpGap>();
            long expected = 0;
            int retrans = 0, overlaps = 0;
            var truncated = false;

            foreach (var (start, data, _) in ordered)
            {
                var end = start + data.Length;
                if (end <= expected) { retrans++; continue; }
                var from_ = start;
                if (start > expected)
                {
                    gaps.Add(new TcpGap(expected, start - expected, "segmento(s) ausente(s) na captura"));
                    if (!truncated) output.Write(new byte[Math.Min(start - expected, Math.Max(0, maxBytes - output.Length))]);
                }
                else if (start < expected)
                {
                    overlaps++;
                    from_ = expected;
                }
                if (!truncated)
                {
                    var slice = data.AsSpan((int)(from_ - start));
                    var room = maxBytes - output.Length;
                    if (slice.Length > room) { slice = slice[..(int)Math.Max(0, room)]; truncated = true; }
                    output.Write(slice);
                }
                expected = end;
            }

            // Bytes que o outro lado confirmou (ACK) mas não estão na captura.
            if (_maxAckFromPeer is { } ack)
            {
                var acked = (long)(int)(ack - b) - (_fin ? 1 : 0);
                if (acked > expected && acked - expected < int.MaxValue)
                    gaps.Add(new TcpGap(expected, acked - expected, "confirmado pelo outro lado mas não capturado"));
            }

            return new TcpDirectionResult(from, to, output.ToArray(), _payloadSeen, _segments.Count, retrans, overlaps, gaps,
                _isn is not null, _fin, truncated);
        }
    }
}
