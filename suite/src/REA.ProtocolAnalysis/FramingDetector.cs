using System.Buffers.Binary;

namespace REA.ProtocolAnalysis;

public enum Confidence { Baixa, Media, Alta }

/// <param name="LengthOffset">Posição do campo de tamanho dentro do quadro.</param>
/// <param name="LengthSize">2 ou 4 bytes.</param>
/// <param name="IncludesHeader">O valor do tamanho conta o quadro inteiro (cabeçalho incluído) ou só o corpo.</param>
public sealed record FramingHypothesis(int LengthOffset, int LengthSize, bool LittleEndian, bool IncludesHeader)
{
    public int HeaderLength => LengthOffset + LengthSize;

    public string Describe() =>
        $"Tamanho de {LengthSize} bytes ({(LittleEndian ? "little-endian" : "big-endian")}) no byte {LengthOffset}, " +
        (IncludesHeader ? "contando o quadro inteiro" : "contando só o que vem depois do cabeçalho");
}

public sealed record Frame(int Index, int Offset, int Length, int BodyOffset, int? OpcodeCandidate, bool BodyIsProtobuf);

public sealed record OpcodeStat(int Value, int Count, int MinBodyLength, int MaxBodyLength);

/// <param name="Score">Para ordenação; só tem sentido comparado a outras hipóteses dos mesmos bytes.</param>
/// <param name="BytesExplained">Bytes cobertos por quadros completos, a partir do início.</param>
/// <param name="TrailingBytes">Bytes que sobram no fim (quadro incompleto ou hipótese errada).</param>
/// <param name="OpcodeField">Onde está o opcode candidato, em texto; nulo quando não há posição plausível.</param>
public sealed record FramingResult(
    FramingHypothesis Hypothesis,
    Confidence Confidence,
    double Score,
    int FrameCount,
    int BytesExplained,
    int TrailingBytes,
    IReadOnlyList<Frame> Frames,
    string? OpcodeField,
    IReadOnlyList<OpcodeStat> OpcodeCandidates,
    double ProtobufBodyRatio,
    IReadOnlyList<string> Notes);

/// <summary>
/// Gera hipóteses de enquadramento por prefixo de tamanho sobre os bytes de UMA direção de um fluxo TCP e as
/// pontua por quanto dos dados elas explicam. Nada aqui é afirmado como formato real do protocolo: o resultado
/// é sempre uma hipótese com confiança, e o "opcode" é sempre candidato.
/// </summary>
public static class FramingDetector
{
    public const int MaxFrameLength = 16 * 1024 * 1024;
    public const int MaxFramesListed = 10_000;
    private static readonly int[] Offsets = [0, 2, 4];
    private static readonly int[] Sizes = [2, 4];

    public static IReadOnlyList<FramingHypothesis> AllHypotheses() =>
        (from o in Offsets
         from s in Sizes
         from le in new[] { true, false }
         from inc in new[] { true, false }
         select new FramingHypothesis(o, s, le, inc)).ToList();

    /// <summary>Hipóteses viáveis, da melhor para a pior. Lista vazia quando nenhuma explica ao menos dois quadros.</summary>
    public static IReadOnlyList<FramingResult> Analyze(ReadOnlyMemory<byte> data, int top = 5)
    {
        var results = new List<FramingResult>();
        foreach (var h in AllHypotheses())
        {
            var r = Evaluate(data, h);
            if (r is not null) results.Add(r);
        }
        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Hypothesis.LengthOffset)
            .ThenBy(r => r.Hypothesis.LengthSize)
            .Take(top)
            .ToList();
    }

    public static FramingResult? Evaluate(ReadOnlyMemory<byte> data, FramingHypothesis h)
    {
        var span = data.Span;
        var frames = new List<(int Offset, int Length)>();
        var pos = 0;
        var headerOnly = 0;
        while (span.Length - pos >= h.HeaderLength)
        {
            var field = span.Slice(pos + h.LengthOffset, h.LengthSize);
            long value = h.LengthSize == 2
                ? (h.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(field) : BinaryPrimitives.ReadUInt16BigEndian(field))
                : (h.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(field) : BinaryPrimitives.ReadUInt32BigEndian(field));
            var total = h.IncludesHeader ? value : value + h.HeaderLength;
            if (total < h.HeaderLength || total > MaxFrameLength || total > span.Length - pos) break;
            if (total == h.HeaderLength) headerOnly++;
            frames.Add((pos, (int)total));
            pos += (int)total;
        }

        var exact = pos == span.Length;
        if (frames.Count < 2 && !(exact && frames.Count == 1)) return null;

        var notes = new List<string>();
        var coverage = span.Length == 0 ? 0 : pos / (double)span.Length;
        var degenerate = headerOnly > frames.Count / 2;
        if (degenerate) notes.Add("Mais da metade dos quadros não tem corpo; pode ser preenchimento com zeros, não protocolo.");
        if (!exact) notes.Add($"{span.Length - pos} bytes no fim não formam um quadro completo (captura cortada ou hipótese errada).");

        // Opcode candidato: os 2 bytes antes do tamanho (offset > 0) ou logo depois dele (offset 0).
        var opcodeAt = h.LengthOffset >= 2 ? h.LengthOffset - 2 : h.HeaderLength;
        var opcodeField = h.LengthOffset >= 2 ? $"2 bytes no início do quadro (byte {opcodeAt})" : $"2 bytes logo após o tamanho (byte {opcodeAt})";
        var listed = new List<Frame>();
        var stats = new Dictionary<int, (int Count, int Min, int Max)>();
        var protobufBodies = 0;
        var bodies = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            var (off, len) = frames[i];
            int? opcode = null;
            var bodyOffset = off + h.HeaderLength;
            if (opcodeAt + 2 <= len)
            {
                var o = span.Slice(off + opcodeAt, 2);
                opcode = h.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(o) : BinaryPrimitives.ReadUInt16BigEndian(o);
                if (h.LengthOffset == 0) bodyOffset += 2;
            }
            var bodyLen = off + len - bodyOffset;
            var isProto = false;
            if (bodyLen > 0)
            {
                bodies++;
                isProto = ProtobufWire.IsCompleteMessage(data.Slice(bodyOffset, bodyLen));
                if (isProto) protobufBodies++;
            }
            if (opcode is { } op)
            {
                stats[op] = stats.TryGetValue(op, out var s)
                    ? (s.Count + 1, Math.Min(s.Min, bodyLen), Math.Max(s.Max, bodyLen))
                    : (1, bodyLen, bodyLen);
            }
            if (listed.Count < MaxFramesListed) listed.Add(new Frame(i, off, len, bodyOffset, opcode, isProto));
        }
        if (frames.Count > MaxFramesListed) notes.Add($"Lista de quadros limitada aos primeiros {MaxFramesListed}; contagens usam todos.");

        var opcodes = stats.Select(kv => new OpcodeStat(kv.Key, kv.Value.Count, kv.Value.Min, kv.Value.Max))
            .OrderByDescending(o => o.Count).ThenBy(o => o.Value).ToList();
        if (opcodes.Count > frames.Count * 0.8 && frames.Count >= 5)
            notes.Add("Quase todo quadro tem um valor diferente na posição do opcode; provavelmente não é um opcode.");

        var score = frames.Count * coverage * (exact ? 2 : 1) * (degenerate ? 0.1 : 1);
        var confidence = exact switch
        {
            true when frames.Count >= 5 && !degenerate => Confidence.Alta,
            true when frames.Count >= 2 && !degenerate => Confidence.Media,
            false when coverage >= 0.9 && frames.Count >= 5 && !degenerate => Confidence.Media,
            _ => Confidence.Baixa,
        };

        return new FramingResult(h, confidence, score, frames.Count, pos, span.Length - pos, listed,
            opcodes.Count > 0 ? opcodeField : null, opcodes,
            bodies == 0 ? 0 : protobufBodies / (double)bodies, notes);
    }
}
