using System.Buffers.Binary;
using System.Text;
using REA.PacketAnalyzer;

namespace REA.NetworkTests;

public class PcapNgRoundTripTests
{
    [Fact]
    public void Written_pcapng_reads_back_with_same_bytes_timestamps_and_link_type()
    {
        using var dir = new TempDir();
        var a = Frames.Tcp("10.0.0.1", 5000, "10.0.0.2", 7000, 100, 0, "S");
        var b = Frames.Tcp("10.0.0.2", 7000, "10.0.0.1", 5000, 900, 101, "SA");
        var c = Frames.Udp("10.0.0.1", 53000, "10.0.0.2", 53, Encoding.ASCII.GetBytes("consulta"));
        var path = Frames.WritePcapNg(dir.File("a.pcapng"), a, b, c);

        Assert.Equal("pcapng", CaptureFileReader.Detect(path));
        using var r = new CaptureFileReader(path);
        var frames = r.ReadAll(TestContext.Current.CancellationToken).ToList();
        Assert.Equal(3, frames.Count);
        Assert.Null(r.TruncationReason);
        Assert.Equal(a, frames[0].Data);
        Assert.Equal(c, frames[2].Data);
        Assert.Equal(Frames.T0.AddMilliseconds(2), frames[2].Timestamp);
        Assert.All(frames, f => Assert.Equal(LinkTypes.Ethernet, f.LinkType));
        Assert.Equal([1L, 2, 3], frames.Select(f => f.Number));
    }

    [Fact]
    public void Random_access_by_offset_returns_the_same_packet()
    {
        using var dir = new TempDir();
        var frames = Enumerable.Range(0, 10).Select(i => Frames.Udp("10.0.0.1", 1000 + i, "10.0.0.2", 53, [(byte)i])).ToArray();
        var path = Frames.WritePcapNg(dir.File("b.pcapng"), frames);
        using var r = new CaptureFileReader(path);
        var all = r.ReadAll(TestContext.Current.CancellationToken).ToList();
        var seventh = r.ReadAt(all[6].Offset, 7);
        Assert.NotNull(seventh);
        Assert.Equal(frames[6], seventh.Data);
        Assert.Equal(7, seventh.Number);
    }

    [Fact]
    public void Truncated_file_keeps_previous_packets_and_explains_the_problem()
    {
        using var dir = new TempDir();
        var path = Frames.WritePcapNg(dir.File("c.pcapng"),
            Frames.Udp("1.1.1.1", 1, "2.2.2.2", 2, [1]), Frames.Udp("1.1.1.1", 1, "2.2.2.2", 2, [2]), Frames.Udp("1.1.1.1", 1, "2.2.2.2", 2, [3]));
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^10]); // corta o último bloco

        using var r = new CaptureFileReader(path);
        Assert.Equal(2, r.ReadAll(TestContext.Current.CancellationToken).Count());
        Assert.Contains("cortado", r.TruncationReason);
    }

    [Fact]
    public void Corrupted_block_length_is_reported_without_exception()
    {
        using var dir = new TempDir();
        var path = Frames.WritePcapNg(dir.File("d.pcapng"), Frames.Udp("1.1.1.1", 1, "2.2.2.2", 2, [1]), Frames.Udp("1.1.1.1", 1, "2.2.2.2", 2, [2]));
        using var r0 = new CaptureFileReader(path);
        var second = r0.ReadAll(TestContext.Current.CancellationToken).Last().Offset;
        r0.Dispose();
        var bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)second + 4), 7); // tamanho impossível
        File.WriteAllBytes(path, bytes);

        using var r = new CaptureFileReader(path);
        Assert.Single(r.ReadAll(TestContext.Current.CancellationToken));
        Assert.Contains("tamanho inválido", r.TruncationReason);
    }

    [Fact]
    public void Non_capture_files_are_rejected()
    {
        using var dir = new TempDir();
        var path = dir.File("x.txt");
        File.WriteAllText(path, "isto não é captura");
        Assert.Null(CaptureFileReader.Detect(path));
        Assert.Throws<CaptureFormatException>(() => new CaptureFileReader(path));
        File.WriteAllBytes(path, []);
        Assert.Throws<CaptureFormatException>(() => new CaptureFileReader(path));
    }
}

public class ClassicPcapTests
{
    private static byte[] Pcap(bool bigEndian, bool nano, byte[] frame, uint sec, uint frac)
    {
        var ms = new MemoryStream();
        void U32(uint v) { var b = new byte[4]; if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(b, v); else BinaryPrimitives.WriteUInt32LittleEndian(b, v); ms.Write(b); }
        void U16(ushort v) { var b = new byte[2]; if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(b, v); else BinaryPrimitives.WriteUInt16LittleEndian(b, v); ms.Write(b); }
        U32(nano ? 0xA1B23C4D : 0xA1B2C3D4);
        U16(2); U16(4); U32(0); U32(0); U32(65535); U32(1);
        U32(sec); U32(frac); U32((uint)frame.Length); U32((uint)frame.Length);
        ms.Write(frame);
        return ms.ToArray();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Reads_all_classic_pcap_variants(bool bigEndian, bool nano)
    {
        using var dir = new TempDir();
        var frame = Frames.Udp("10.0.0.1", 1, "10.0.0.2", 2, [9, 9]);
        var path = dir.File("v.pcap");
        File.WriteAllBytes(path, Pcap(bigEndian, nano, frame, 1_700_000_000, nano ? 500_000_000u : 500_000u));

        Assert.Equal("pcap", CaptureFileReader.Detect(path));
        using var r = new CaptureFileReader(path);
        var f = Assert.Single(r.ReadAll(TestContext.Current.CancellationToken));
        Assert.Equal(frame, f.Data);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddMilliseconds(500), f.Timestamp);
    }
}

public class DecoderTests
{
    [Fact]
    public void Tcp_packet_summary_has_addresses_ports_flags_and_payload()
    {
        var data = Frames.Tcp("192.168.0.10", 51000, "192.168.0.20", 7000, 1000, 2000, "PA", Encoding.ASCII.GetBytes("LOGIN"));
        var s = PacketDecoder.Summarize(Frames.Frame(data));
        Assert.Equal("TCP", s.Protocol);
        Assert.Equal("192.168.0.10", s.Source);
        Assert.Equal(7000, s.DestinationPort);
        Assert.Equal("PSH, ACK", s.TcpFlags);
        Assert.Equal(5, s.PayloadLength);

        var d = PacketDecoder.Detail(Frames.Frame(data));
        Assert.Equal("LOGIN", Encoding.ASCII.GetString(d.Payload));
        Assert.Contains(d.Layers, l => l.Title == "TCP");
        Assert.Contains(d.Layers, l => l.Title == "IPv4");
    }

    [Fact]
    public void Loopback_null_link_type_is_decoded()
    {
        var eth = Frames.Tcp("127.0.0.1", 40000, "127.0.0.1", 7000, 1, 0, "S");
        var ipOnly = eth[14..];
        var nullFrame = new byte[4 + ipOnly.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(nullFrame, 2); // AF_INET
        ipOnly.CopyTo(nullFrame, 4);
        var s = PacketDecoder.Summarize(new CapturedFrame(1, 0, Frames.T0, LinkTypes.Null, nullFrame.Length, nullFrame));
        Assert.Equal("TCP", s.Protocol);
        Assert.Equal("SYN", s.TcpFlags);
    }

    [Fact]
    public void Garbage_is_reported_as_malformed_not_thrown()
    {
        var s = PacketDecoder.Summarize(new CapturedFrame(1, 0, Frames.T0, LinkTypes.Ethernet, 3, [1, 2, 3]));
        Assert.NotNull(s.Protocol);
    }

    [Fact]
    public void Hex_dump_has_offset_hex_and_ascii()
    {
        var dump = HexDump.Format(Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQR"));
        var lines = dump.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("000000  41 42 43", lines[0]);
        Assert.EndsWith("ABCDEFGHIJKLMNOP", lines[0]);
        Assert.StartsWith("000010  51 52", lines[1]);
    }
}

public class FilterTests
{
    [Fact]
    public async Task Display_filter_by_protocol_port_text_and_payload()
    {
        using var dir = new TempDir();
        var path = Frames.WritePcapNg(dir.File("f.pcapng"),
            Frames.Tcp("10.0.0.1", 5000, "10.0.0.2", 7000, 1, 0, "S"),
            Frames.Tcp("10.0.0.1", 5000, "10.0.0.2", 7000, 2, 1, "PA", Encoding.ASCII.GetBytes("hello world")),
            Frames.Udp("10.0.0.3", 53000, "10.0.0.2", 53, Encoding.ASCII.GetBytes("dns")));
        var doc = await CaptureDocument.OpenAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(3, doc.PacketCount);

        Assert.Equal([1L, 2], await doc.FilterAsync(new PacketFilter(Protocol: "tcp"), TestContext.Current.CancellationToken));
        Assert.Equal([3L], await doc.FilterAsync(new PacketFilter(Port: 53), TestContext.Current.CancellationToken));
        Assert.Equal([2L], await doc.FilterAsync(new PacketFilter(Text: "HELLO"), TestContext.Current.CancellationToken));
        Assert.Equal([2L, 3], await doc.FilterAsync(new PacketFilter(OnlyWithPayload: true), TestContext.Current.CancellationToken));
        Assert.Equal([3L], await doc.FilterAsync(new PacketFilter(Address: "10.0.0.3"), TestContext.Current.CancellationToken));

        var page = await doc.ReadSummariesAsync([2L, 3], TestContext.Current.CancellationToken);
        Assert.Equal(["TCP", "UDP"], page.Select(p => p.Protocol));
    }

    [Fact]
    public async Task Analyzer_counts_packets_protocols_and_streams()
    {
        using var dir = new TempDir();
        var path = Frames.WritePcapNg(dir.File("g.pcapng"),
            Frames.Tcp("10.0.0.1", 5000, "10.0.0.2", 7000, 1, 0, "S"),
            Frames.Tcp("10.0.0.2", 7000, "10.0.0.1", 5000, 9, 2, "SA"),
            Frames.Tcp("10.0.0.1", 5001, "10.0.0.2", 7000, 1, 0, "S"),
            Frames.Udp("10.0.0.1", 1, "10.0.0.2", 2, [1]));
        var a = await new CaptureFileAnalyzer().AnalyzeAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal("pcapng", a.Format);
        Assert.Equal(4, a.PacketCount);
        Assert.Equal(3, a.Protocols["TCP"]);
        Assert.Equal(1, a.Protocols["UDP"]);
        Assert.Equal(2, a.TcpStreamCount);
        Assert.Equal(Frames.T0, a.FirstPacketAt);
        Assert.False(a.Truncated);
    }

    [Fact]
    public void Tshark_follow_raw_output_is_parsed_per_direction()
    {
        const string output = """
            ===================================================================
            Follow: tcp,raw
            Filter: tcp.stream eq 0
            Node 0: 127.0.0.1:60571
            Node 1: 127.0.0.1:47123
            70696e67
            	706f6e67
            21
            ===================================================================
            """;
        var (c2s, s2c) = TsharkCrossCheck.ParseFollowRaw(output);
        Assert.Equal("ping!", Encoding.ASCII.GetString(c2s));
        Assert.Equal("pong", Encoding.ASCII.GetString(s2c));
    }
}
