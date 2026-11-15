using System.Net;
using System.Net.NetworkInformation;
using PacketDotNet;
using REA.PacketAnalyzer;

namespace REA.NetworkTests;

/// <summary>Monta quadros Ethernet/IPv4/TCP reais (bytes de verdade, gerados pelo PacketDotNet) para os testes.</summary>
internal static class Frames
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    public static byte[] Tcp(string src, int sport, string dst, int dport, uint seq, uint ack, string flags, byte[]? payload = null)
    {
        var tcp = new TcpPacket((ushort)sport, (ushort)dport)
        {
            SequenceNumber = seq,
            AcknowledgmentNumber = ack,
            Synchronize = flags.Contains('S'),
            Acknowledgment = flags.Contains('A'),
            Finished = flags.Contains('F'),
            Push = flags.Contains('P'),
            Reset = flags.Contains('R'),
            WindowSize = 65535,
            PayloadData = payload ?? [],
        };
        var ip = new IPv4Packet(IPAddress.Parse(src), IPAddress.Parse(dst)) { PayloadPacket = tcp, TimeToLive = 64 };
        var eth = new EthernetPacket(PhysicalAddress.Parse("00-11-22-33-44-55"), PhysicalAddress.Parse("66-77-88-99-AA-BB"),
            EthernetType.IPv4) { PayloadPacket = ip };
        tcp.UpdateCalculatedValues();
        ip.UpdateCalculatedValues();
        tcp.UpdateTcpChecksum();
        ip.UpdateIPChecksum();
        return eth.Bytes;
    }

    public static byte[] Udp(string src, int sport, string dst, int dport, byte[] payload)
    {
        var udp = new UdpPacket((ushort)sport, (ushort)dport) { PayloadData = payload };
        var ip = new IPv4Packet(IPAddress.Parse(src), IPAddress.Parse(dst)) { PayloadPacket = udp, TimeToLive = 64 };
        var eth = new EthernetPacket(PhysicalAddress.Parse("00-11-22-33-44-55"), PhysicalAddress.Parse("66-77-88-99-AA-BB"),
            EthernetType.IPv4) { PayloadPacket = ip };
        udp.UpdateCalculatedValues();
        ip.UpdateCalculatedValues();
        return eth.Bytes;
    }

    /// <summary>Grava um PCAPNG Ethernet com os quadros dados (1 ms entre eles).</summary>
    public static string WritePcapNg(string path, params byte[][] frames)
    {
        using var w = new PcapNgWriter(path, LinkTypes.Ethernet, 65535, "teste", "interface de teste");
        for (var i = 0; i < frames.Length; i++) w.WritePacket(T0.AddMilliseconds(i), frames[i], frames[i].Length);
        return path;
    }

    public static CapturedFrame Frame(byte[] data, long number = 1) =>
        new(number, 0, T0, LinkTypes.Ethernet, data.Length, data);

    public static TcpSegmentInfo Seg(byte[] frame, long number) =>
        PacketDecoder.TcpSegment(Frame(frame, number), PacketDecoder.Parse(Frame(frame, number)))!;
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rea-net-tests", Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
