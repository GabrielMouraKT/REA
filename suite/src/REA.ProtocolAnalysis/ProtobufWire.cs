using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace REA.ProtocolAnalysis;

public enum WireType { Varint = 0, Fixed64 = 1, LengthDelimited = 2, StartGroup = 3, EndGroup = 4, Fixed32 = 5 }

/// <summary>
/// Um campo lido do wire format. Sem .proto não existe nome nem tipo declarado: só o número do campo e as
/// interpretações possíveis dos bytes. <see cref="Nested"/> só é preenchido quando o conteúdo inteiro decodifica
/// como mensagem; isso é uma hipótese (bytes arbitrários às vezes decodificam), não uma confirmação.
/// </summary>
public sealed record ProtoField(
    int Number,
    WireType WireType,
    int Offset,
    int Length,
    ulong? Varint,
    ReadOnlyMemory<byte> Bytes,
    IReadOnlyList<ProtoField>? Nested)
{
    /// <summary>Leituras possíveis do valor, da mais comum para a menos comum.</summary>
    public IReadOnlyList<string> Interpretations => ProtobufWire.Interpret(this);
}

public sealed record ProtoDecodeResult(IReadOnlyList<ProtoField> Fields, bool Complete, int BytesConsumed, string? Error);

public static class ProtobufWire
{
    public const int MaxDepth = 32;
    public const int MaxFieldNumber = (1 << 29) - 1;

    /// <summary>Decodifica até o fim ou até o primeiro erro; os campos lidos antes do erro são preservados.</summary>
    public static ProtoDecodeResult Decode(ReadOnlyMemory<byte> data) => Decode(data, 0, 0);

    /// <summary>Verdadeiro se os bytes inteiros formam ao menos um campo válido, sem sobras.</summary>
    public static bool IsCompleteMessage(ReadOnlyMemory<byte> data) =>
        Decode(data) is { Complete: true, Fields.Count: > 0 };

    private static ProtoDecodeResult Decode(ReadOnlyMemory<byte> data, int baseOffset, int depth)
    {
        var fields = new List<ProtoField>();
        var span = data.Span;
        var pos = 0;
        while (pos < span.Length)
        {
            var start = pos;
            if (!TryReadVarint(span, ref pos, out var key))
                return new(fields, false, start, $"Chave varint inválida no byte {baseOffset + start}.");
            var wire = (int)(key & 7);
            var number = key >> 3;
            if (number is 0 or > MaxFieldNumber)
                return new(fields, false, start, $"Número de campo inválido ({number}) no byte {baseOffset + start}.");

            switch ((WireType)wire)
            {
                case WireType.Varint:
                    if (!TryReadVarint(span, ref pos, out var v))
                        return new(fields, false, start, $"Varint inválido no byte {baseOffset + start}.");
                    fields.Add(new((int)number, WireType.Varint, baseOffset + start, pos - start, v, default, null));
                    break;
                case WireType.Fixed64:
                case WireType.Fixed32:
                    var size = wire == 1 ? 8 : 4;
                    if (span.Length - pos < size)
                        return new(fields, false, start, $"Campo fixo de {size} bytes truncado no byte {baseOffset + start}.");
                    fields.Add(new((int)number, (WireType)wire, baseOffset + start, pos + size - start, null, data.Slice(pos, size), null));
                    pos += size;
                    break;
                case WireType.LengthDelimited:
                    if (!TryReadVarint(span, ref pos, out var len) || len > (ulong)(span.Length - pos))
                        return new(fields, false, start, $"Tamanho de campo inválido ou maior que os dados no byte {baseOffset + start}.");
                    var body = data.Slice(pos, (int)len);
                    IReadOnlyList<ProtoField>? nested = null;
                    if (depth < MaxDepth && len > 0)
                    {
                        var inner = Decode(body, baseOffset + pos, depth + 1);
                        if (inner.Complete && inner.Fields.Count > 0 && !LooksLikeText(body.Span)) nested = inner.Fields;
                    }
                    pos += (int)len;
                    fields.Add(new((int)number, WireType.LengthDelimited, baseOffset + start, pos - start, null, body, nested));
                    break;
                default:
                    return new(fields, false, start,
                        $"Wire type {wire} (grupo obsoleto ou inválido) no byte {baseOffset + start}; decodificação interrompida.");
            }
        }
        return new(fields, true, pos, null);
    }

    public static bool TryReadVarint(ReadOnlySpan<byte> span, ref int pos, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            if (pos >= span.Length) return false;
            var b = span[pos++];
            if (shift == 63 && b > 1) return false;
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }

    public static long ZigZagDecode(ulong v) => (long)(v >> 1) ^ -(long)(v & 1);
    public static ulong ZigZagEncode(long v) => (ulong)((v << 1) ^ (v >> 63));

    internal static IReadOnlyList<string> Interpret(ProtoField f)
    {
        var c = CultureInfo.InvariantCulture;
        var list = new List<string>();
        switch (f.WireType)
        {
            case WireType.Varint:
                var v = f.Varint!.Value;
                list.Add($"uint: {v}");
                if ((long)v < 0) list.Add($"int64: {(long)v}");
                list.Add($"sint (zigzag): {ZigZagDecode(v)}");
                if (v <= 1) list.Add($"bool: {(v == 1 ? "true" : "false")}");
                break;
            case WireType.Fixed32:
                var s32 = f.Bytes.Span;
                list.Add($"fixed32: {BinaryPrimitives.ReadUInt32LittleEndian(s32)}");
                list.Add($"sfixed32: {BinaryPrimitives.ReadInt32LittleEndian(s32)}");
                list.Add($"float: {BinaryPrimitives.ReadSingleLittleEndian(s32).ToString("R", c)}");
                break;
            case WireType.Fixed64:
                var s64 = f.Bytes.Span;
                list.Add($"fixed64: {BinaryPrimitives.ReadUInt64LittleEndian(s64)}");
                list.Add($"sfixed64: {BinaryPrimitives.ReadInt64LittleEndian(s64)}");
                list.Add($"double: {BinaryPrimitives.ReadDoubleLittleEndian(s64).ToString("R", c)}");
                break;
            case WireType.LengthDelimited:
                var b = f.Bytes.Span;
                if (TryUtf8(b, out var text)) list.Add($"string: \"{text}\"");
                if (f.Nested is not null) list.Add($"mensagem aninhada? ({f.Nested.Count} campos)");
                if (TryPackedVarints(b, out var count)) list.Add($"repeated varint compactado? ({count} valores)");
                list.Add($"bytes: {Convert.ToHexString(b[..Math.Min(b.Length, 32)])}{(b.Length > 32 ? "…" : "")} ({b.Length} bytes)");
                break;
        }
        return list;
    }

    private static bool TryUtf8(ReadOnlySpan<byte> b, out string text)
    {
        text = "";
        if (b.IsEmpty || !LooksLikeText(b)) return false;
        try { text = new UTF8Encoding(false, true).GetString(b); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> b)
    {
        if (b.IsEmpty) return false;
        foreach (var x in b) if (x < 0x20 && x is not (9 or 10 or 13)) return false;
        return true;
    }

    private static bool TryPackedVarints(ReadOnlySpan<byte> b, out int count)
    {
        count = 0;
        var pos = 0;
        while (pos < b.Length)
        {
            if (!TryReadVarint(b, ref pos, out _)) return false;
            count++;
        }
        return count > 1;
    }
}

/// <summary>Monta mensagens Protobuf de teste (wire format), para laboratório e testes.</summary>
public sealed class ProtoWriter
{
    private readonly MemoryStream _ms = new();

    public ProtoWriter Varint(int field, ulong value) { Key(field, WireType.Varint); Raw(value); return this; }
    public ProtoWriter SInt(int field, long value) => Varint(field, ProtobufWire.ZigZagEncode(value));
    public ProtoWriter Fixed32(int field, uint value)
    {
        Key(field, WireType.Fixed32);
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        _ms.Write(b);
        return this;
    }
    public ProtoWriter Fixed64(int field, ulong value)
    {
        Key(field, WireType.Fixed64);
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, value);
        _ms.Write(b);
        return this;
    }
    public ProtoWriter Bytes(int field, ReadOnlySpan<byte> value)
    {
        Key(field, WireType.LengthDelimited);
        Raw((ulong)value.Length);
        _ms.Write(value);
        return this;
    }
    public ProtoWriter String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
    public ProtoWriter Message(int field, Action<ProtoWriter> build)
    {
        var inner = new ProtoWriter();
        build(inner);
        return Bytes(field, inner.ToArray());
    }

    public byte[] ToArray() => _ms.ToArray();

    private void Key(int field, WireType wire)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(field, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(field, ProtobufWire.MaxFieldNumber);
        Raw(((ulong)field << 3) | (ulong)wire);
    }

    private void Raw(ulong v)
    {
        while (v >= 0x80) { _ms.WriteByte((byte)(v | 0x80)); v >>= 7; }
        _ms.WriteByte((byte)v);
    }
}
