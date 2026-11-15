using System.Buffers.Binary;
using System.Text;

namespace REA.PacketAnalyzer;

/// <summary>Link-layer type (valores LINKTYPE_* do tcpdump.org).</summary>
public static class LinkTypes
{
    public const ushort Null = 0;        // loopback BSD/Npcap (família do protocolo em 4 bytes)
    public const ushort Ethernet = 1;
    public const ushort Raw = 101;
    public const ushort Loop = 108;
    public const ushort LinuxSll = 113;
}

/// <summary>Pacote lido de um arquivo de captura. <see cref="Offset"/> aponta para o bloco/registro no arquivo.</summary>
public sealed record CapturedFrame(
    long Number,
    long Offset,
    DateTimeOffset Timestamp,
    ushort LinkType,
    int OriginalLength,
    byte[] Data);

public sealed class CaptureFormatException(string message) : Exception(message);

// =====================================================================
// Escrita PCAPNG (SHB + IDB + EPB, little-endian, resolução de microssegundos)
// =====================================================================

/// <summary>
/// Grava PCAPNG em fluxo. Cada pacote é um Enhanced Packet Block completo; após <see cref="Flush"/>
/// o arquivo é válido até o último bloco, mesmo se o processo morrer depois.
/// </summary>
public sealed class PcapNgWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private bool _disposed;

    public long PacketsWritten { get; private set; }

    public PcapNgWriter(string path, ushort linkType, int snapLength, string? interfaceName, string? interfaceDescription,
        string application = "REA Advanced Suite")
        : this(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16), true,
            linkType, snapLength, interfaceName, interfaceDescription, application)
    {
    }

    public PcapNgWriter(Stream stream, bool ownsStream, ushort linkType, int snapLength, string? interfaceName,
        string? interfaceDescription, string application = "REA Advanced Suite")
    {
        _stream = stream;
        _ownsStream = ownsStream;
        WriteSectionHeader(application);
        WriteInterfaceDescription(linkType, snapLength, interfaceName, interfaceDescription);
        _stream.Flush();
    }

    public void WritePacket(DateTimeOffset timestamp, ReadOnlySpan<byte> data, int originalLength)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var micros = (ulong)((timestamp.UtcDateTime - DateTime.UnixEpoch).Ticks / 10);
        var padded = Pad4(data.Length);
        var total = 32 + padded;
        Span<byte> header = stackalloc byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 6);                       // tipo EPB
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], 0);                  // interface 0
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)(micros >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)micros);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)Math.Max(originalLength, data.Length));
        _stream.Write(header);
        _stream.Write(data);
        WriteZeros(padded - data.Length);
        WriteUInt32((uint)total);
        PacketsWritten++;
    }

    public void Flush() => _stream.Flush();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Flush();
        if (_ownsStream) _stream.Dispose();
    }

    private void WriteSectionHeader(string application)
    {
        var options = new MemoryStream();
        WriteOption(options, 4, Encoding.UTF8.GetBytes(application)); // shb_userappl
        WriteOption(options, 0, []);                                   // opt_endofopt
        var total = 28 + (int)options.Length;
        WriteUInt32(0x0A0D0D0A);
        WriteUInt32((uint)total);
        WriteUInt32(0x1A2B3C4D);   // byte-order magic
        WriteUInt16(1);            // versão 1.0
        WriteUInt16(0);
        WriteUInt64(ulong.MaxValue); // tamanho da seção desconhecido (-1)
        _stream.Write(options.ToArray());
        WriteUInt32((uint)total);
    }

    private void WriteInterfaceDescription(ushort linkType, int snapLength, string? name, string? description)
    {
        var options = new MemoryStream();
        if (!string.IsNullOrEmpty(name)) WriteOption(options, 2, Encoding.UTF8.GetBytes(name));               // if_name
        if (!string.IsNullOrEmpty(description)) WriteOption(options, 3, Encoding.UTF8.GetBytes(description)); // if_description
        WriteOption(options, 9, [6]);                                                                          // if_tsresol = 10^-6
        WriteOption(options, 0, []);
        var total = 20 + (int)options.Length;
        WriteUInt32(1);
        WriteUInt32((uint)total);
        WriteUInt16(linkType);
        WriteUInt16(0);
        WriteUInt32((uint)snapLength);
        _stream.Write(options.ToArray());
        WriteUInt32((uint)total);
    }

    private static void WriteOption(Stream s, ushort code, byte[] value)
    {
        Span<byte> h = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(h, code);
        BinaryPrimitives.WriteUInt16LittleEndian(h[2..], (ushort)value.Length);
        s.Write(h);
        s.Write(value);
        for (var i = value.Length; i < Pad4(value.Length); i++) s.WriteByte(0);
    }

    private void WriteUInt16(ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); _stream.Write(b); }
    private void WriteUInt32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); _stream.Write(b); }
    private void WriteUInt64(ulong v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, v); _stream.Write(b); }
    private void WriteZeros(int n) { for (var i = 0; i < n; i++) _stream.WriteByte(0); }
    internal static int Pad4(int n) => (n + 3) & ~3;
}

// =====================================================================
// Leitura PCAP e PCAPNG em fluxo (sem carregar o arquivo inteiro)
// =====================================================================

/// <summary>
/// Lê pacotes de PCAP (µs/ns, ambas as ordens de bytes) ou PCAPNG (SHB/IDB/EPB/SPB/OPB, várias seções e interfaces).
/// Arquivo truncado ou corrompido: os pacotes válidos anteriores são entregues e <see cref="TruncationReason"/> explica o problema.
/// </summary>
public sealed class CaptureFileReader : IDisposable
{
    private const uint MaxBlockLength = 256 * 1024 * 1024;
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly bool _isPcapNg;

    // pcap clássico
    private bool _bigEndian;
    private bool _nanoseconds;
    private ushort _pcapLinkType;

    // pcapng
    private readonly List<(ushort LinkType, long TicksPerUnit, long UnitsPerSecond, uint SnapLen)> _interfaces = [];

    private long _number;

    public string Format { get; }
    public string? TruncationReason { get; private set; }
    public IReadOnlyList<ushort> LinkTypes => _isPcapNg ? _interfaces.Select(i => i.LinkType).ToList() : [_pcapLinkType];

    public CaptureFileReader(string path)
        : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16), ownsStream: true)
    {
    }

    public CaptureFileReader(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        Span<byte> magic = stackalloc byte[4];
        if (!ReadExactly(magic)) throw new CaptureFormatException("Arquivo vazio ou menor que o cabeçalho.");
        var m = BinaryPrimitives.ReadUInt32LittleEndian(magic);
        if (m == 0x0A0D0D0A)
        {
            _isPcapNg = true;
            Format = "pcapng";
            _stream.Position -= 4;
        }
        else
        {
            Format = "pcap";
            ReadPcapHeader(m);
        }
    }

    public static string? Detect(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> b = stackalloc byte[4];
            if (fs.Read(b) < 4) return null;
            return BinaryPrimitives.ReadUInt32LittleEndian(b) switch
            {
                0x0A0D0D0A => "pcapng",
                0xA1B2C3D4 or 0xD4C3B2A1 or 0xA1B23C4D or 0x4D3CB2A1 => "pcap",
                _ => null,
            };
        }
        catch (IOException) { return null; }
    }

    /// <summary>Posiciona no registro de um pacote já conhecido (para paginação) e lê só ele.</summary>
    public CapturedFrame? ReadAt(long offset, long number)
    {
        if (_isPcapNg) ScanInterfacesUpTo(offset);
        _stream.Position = offset;
        _number = number - 1;
        return Next();
    }

    public IEnumerable<CapturedFrame> ReadAll(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var frame = Next();
            if (frame is null) yield break;
            yield return frame;
        }
    }

    public CapturedFrame? Next() => _isPcapNg ? NextPcapNg() : NextPcap();

    private long _headerScanPosition;

    /// <summary>
    /// Acesso aleatório em PCAPNG: as interfaces (IDB) e a ordem de bytes de cada seção (SHB) vêm antes dos pacotes.
    /// Percorre só os cabeçalhos de bloco até o deslocamento pedido, sem ler os dados dos pacotes.
    /// Páginas são lidas em ordem crescente, então o arquivo é percorrido uma única vez por leitor.
    /// </summary>
    private void ScanInterfacesUpTo(long offset)
    {
        if (offset < _headerScanPosition) _headerScanPosition = 0;
        if (_headerScanPosition == 0) _interfaces.Clear();
        _stream.Position = _headerScanPosition;
        Span<byte> h = stackalloc byte[8];
        while (_stream.Position < offset)
        {
            var blockStart = _stream.Position;
            if (_stream.ReadAtLeast(h, 8, throwOnEndOfStream: false) < 8) break;
            if (BinaryPrimitives.ReadUInt32LittleEndian(h) == 0x0A0D0D0A)
            {
                if (!ReadSectionHeader(blockStart)) break;
                continue;
            }
            var type = U32(h);
            var length = U32(h[4..]);
            if (length < 12 || length % 4 != 0 || length > MaxBlockLength) break;
            if (type == 1)
            {
                var body = new byte[length - 8];
                if (!ReadExactly(body)) break;
                ParseInterface(body);
            }
            else
            {
                _stream.Position = blockStart + length;
            }
        }
        _headerScanPosition = Math.Max(_headerScanPosition, Math.Min(_stream.Position, offset));
    }

    private void ReadPcapHeader(uint magic)
    {
        (_bigEndian, _nanoseconds) = magic switch
        {
            0xA1B2C3D4 => (false, false),
            0xD4C3B2A1 => (true, false),
            0xA1B23C4D => (false, true),
            0x4D3CB2A1 => (true, true),
            _ => throw new CaptureFormatException("Formato não reconhecido: não é PCAP nem PCAPNG."),
        };
        Span<byte> h = stackalloc byte[20];
        if (!ReadExactly(h)) throw new CaptureFormatException("Cabeçalho PCAP incompleto.");
        _pcapLinkType = (ushort)U32(h[16..]);
    }

    private CapturedFrame? NextPcap()
    {
        var offset = _stream.Position;
        Span<byte> h = stackalloc byte[16];
        var read = _stream.ReadAtLeast(h, 16, throwOnEndOfStream: false);
        if (read == 0) return null;
        if (read < 16) { TruncationReason = $"registro incompleto no byte {offset}"; return null; }
        var sec = U32(h);
        var frac = U32(h[4..]);
        var caplen = U32(h[8..]);
        var origlen = U32(h[12..]);
        if (caplen > MaxBlockLength) { TruncationReason = $"tamanho de pacote inválido ({caplen}) no byte {offset}"; return null; }
        var data = new byte[caplen];
        if (!ReadExactly(data)) { TruncationReason = $"pacote {_number + 1} cortado no byte {offset}"; return null; }
        var ticks = sec * TimeSpan.TicksPerSecond + (_nanoseconds ? frac / 100 : frac * 10);
        return new CapturedFrame(++_number, offset, new DateTimeOffset(DateTime.UnixEpoch.AddTicks((long)ticks)), _pcapLinkType,
            (int)origlen, data);
    }

    private CapturedFrame? NextPcapNg()
    {
        Span<byte> h = stackalloc byte[8];
        while (true)
        {
            var offset = _stream.Position;
            var read = _stream.ReadAtLeast(h, 8, throwOnEndOfStream: false);
            if (read == 0) return null;
            if (read < 8) { TruncationReason = $"cabeçalho de bloco incompleto no byte {offset}"; return null; }

            var rawType = BinaryPrimitives.ReadUInt32LittleEndian(h);
            if (rawType == 0x0A0D0D0A)
            {
                if (!ReadSectionHeader(offset)) return null;
                continue;
            }
            var type = U32(h);
            var length = U32(h[4..]);
            if (length < 12 || length % 4 != 0 || length > MaxBlockLength)
            {
                TruncationReason = $"bloco com tamanho inválido ({length}) no byte {offset}";
                return null;
            }
            var body = new byte[length - 8];
            if (!ReadExactly(body)) { TruncationReason = $"bloco cortado no byte {offset}"; return null; }
            if (U32(body.AsSpan((int)length - 12)) != length)
            {
                TruncationReason = $"bloco corrompido no byte {offset} (tamanhos não conferem)";
                return null;
            }

            switch (type)
            {
                case 1: // IDB
                    ParseInterface(body);
                    break;
                case 6: // EPB
                {
                    var iface = (int)U32(body);
                    var ts = ((ulong)U32(body.AsSpan(4)) << 32) | U32(body.AsSpan(8));
                    var caplen = (int)U32(body.AsSpan(12));
                    var origlen = (int)U32(body.AsSpan(16));
                    return MakeFrame(offset, iface, ts, body.AsSpan(20, Math.Min(caplen, body.Length - 24)), origlen);
                }
                case 3: // SPB
                {
                    var origlen = (int)U32(body);
                    var snap = _interfaces.Count > 0 ? (int)_interfaces[0].SnapLen : int.MaxValue;
                    var caplen = Math.Min(Math.Min(origlen, snap == 0 ? int.MaxValue : snap), body.Length - 8);
                    return MakeFrame(offset, 0, null, body.AsSpan(4, caplen), origlen);
                }
                case 2: // OPB (obsoleto)
                {
                    var iface = U16(body);
                    var ts = ((ulong)U32(body.AsSpan(4)) << 32) | U32(body.AsSpan(8));
                    var caplen = (int)U32(body.AsSpan(12));
                    var origlen = (int)U32(body.AsSpan(16));
                    return MakeFrame(offset, iface, ts, body.AsSpan(20, Math.Min(caplen, body.Length - 24)), origlen);
                }
                default:
                    break; // NRB, ISB, DSB, blocos personalizados: ignorados
            }
        }
    }

    private bool ReadSectionHeader(long offset)
    {
        Span<byte> bom = stackalloc byte[4];
        if (!ReadExactly(bom)) { TruncationReason = $"cabeçalho de seção incompleto no byte {offset}"; return false; }
        var b = BinaryPrimitives.ReadUInt32LittleEndian(bom);
        if (b == 0x1A2B3C4D) _bigEndian = false;
        else if (b == 0x4D3C2B1A) _bigEndian = true;
        else { TruncationReason = $"marca de ordem de bytes inválida no byte {offset}"; return false; }

        _stream.Position = offset + 4;
        Span<byte> len = stackalloc byte[4];
        ReadExactly(len);
        var length = U32(len);
        if (length < 28 || length > MaxBlockLength) { TruncationReason = $"seção com tamanho inválido no byte {offset}"; return false; }
        _stream.Position = offset + length;
        if (_stream.Position > _stream.Length) { TruncationReason = $"seção cortada no byte {offset}"; return false; }
        _interfaces.Clear(); // interfaces são numeradas por seção
        return true;
    }

    private void ParseInterface(byte[] body)
    {
        var linkType = U16(body);
        var snap = U32(body.AsSpan(4));
        long unitsPerSecond = 1_000_000; // padrão: microssegundos
        var pos = 8;
        while (pos + 4 <= body.Length - 4)
        {
            var code = U16(body.AsSpan(pos));
            var len = U16(body.AsSpan(pos + 2));
            if (code == 0) break;
            if (code == 9 && len >= 1)
            {
                var v = body[pos + 4];
                unitsPerSecond = (v & 0x80) == 0 ? (long)Math.Pow(10, v) : 1L << (v & 0x7F);
            }
            pos += 4 + PcapNgWriter.Pad4(len);
        }
        _interfaces.Add((linkType, 0, unitsPerSecond, snap));
    }

    private CapturedFrame? MakeFrame(long offset, int iface, ulong? ts, ReadOnlySpan<byte> data, int origlen)
    {
        if (iface < 0 || iface >= _interfaces.Count)
        {
            TruncationReason = $"pacote no byte {offset} referencia interface inexistente ({iface})";
            return null;
        }
        var info = _interfaces[iface];
        var timestamp = DateTimeOffset.UnixEpoch;
        if (ts is { } t)
        {
            var seconds = (long)(t / (ulong)info.UnitsPerSecond);
            var remainder = (long)(t % (ulong)info.UnitsPerSecond);
            var ticks = seconds * TimeSpan.TicksPerSecond + remainder * TimeSpan.TicksPerSecond / info.UnitsPerSecond;
            timestamp = new DateTimeOffset(DateTime.UnixEpoch.AddTicks(ticks));
        }
        return new CapturedFrame(++_number, offset, timestamp, info.LinkType, origlen, data.ToArray());
    }

    private bool ReadExactly(Span<byte> buffer) => _stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
    private uint U32(ReadOnlySpan<byte> b) => _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(b) : BinaryPrimitives.ReadUInt32LittleEndian(b);
    private ushort U16(ReadOnlySpan<byte> b) => _bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(b) : BinaryPrimitives.ReadUInt16LittleEndian(b);

    public void Dispose()
    {
        if (_ownsStream) _stream.Dispose();
    }
}
