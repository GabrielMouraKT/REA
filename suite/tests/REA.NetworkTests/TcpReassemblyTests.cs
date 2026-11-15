using System.Text;
using REA.PacketAnalyzer;

namespace REA.NetworkTests;

public class TcpReassemblyTests
{
    private const string C = "10.0.0.1", S = "10.0.0.2";
    private static byte[] B(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>Conversa completa: handshake, "HELLO"+"WORLD" do cliente, "OK" do servidor, FIN dos dois lados.</summary>
    private static List<byte[]> FullConversation() =>
    [
        Frames.Tcp(C, 5000, S, 7000, 100, 0, "S"),
        Frames.Tcp(S, 7000, C, 5000, 900, 101, "SA"),
        Frames.Tcp(C, 5000, S, 7000, 101, 901, "A"),
        Frames.Tcp(C, 5000, S, 7000, 101, 901, "PA", B("HELLO")),
        Frames.Tcp(C, 5000, S, 7000, 106, 901, "PA", B("WORLD")),
        Frames.Tcp(S, 7000, C, 5000, 901, 111, "PA", B("OK")),
        Frames.Tcp(C, 5000, S, 7000, 111, 903, "FA"),
        Frames.Tcp(S, 7000, C, 5000, 903, 112, "FA"),
        Frames.Tcp(C, 5000, S, 7000, 112, 904, "A"),
    ];

    private static TcpStreamResult Reassemble(IEnumerable<byte[]> frames)
    {
        var r = new TcpReassembler();
        var n = 0;
        foreach (var f in frames) r.Add(Frames.Seg(f, ++n));
        return Assert.Single(r.Build());
    }

    [Fact]
    public void Complete_conversation_is_rebuilt_and_marked_complete()
    {
        var s = Reassemble(FullConversation());
        Assert.Equal("HELLOWORLD", Encoding.ASCII.GetString(s.ClientToServer.Data));
        Assert.Equal("OK", Encoding.ASCII.GetString(s.ServerToClient.Data));
        Assert.Equal(TcpStreamCompleteness.Complete, s.Completeness);
        Assert.Equal("10.0.0.1:5000", s.Client.ToString());
        Assert.Equal(9, s.Packets);
        Assert.Equal(9, s.Timeline.Count);
    }

    [Fact]
    public void Out_of_order_segments_are_reordered()
    {
        var frames = FullConversation();
        (frames[3], frames[4]) = (frames[4], frames[3]); // WORLD chega antes de HELLO
        var s = Reassemble(frames);
        Assert.Equal("HELLOWORLD", Encoding.ASCII.GetString(s.ClientToServer.Data));
        Assert.Equal(TcpStreamCompleteness.Complete, s.Completeness);
    }

    [Fact]
    public void Retransmission_is_detected_and_not_duplicated()
    {
        var frames = FullConversation();
        frames.Insert(5, Frames.Tcp(C, 5000, S, 7000, 101, 901, "PA", B("HELLO"))); // reenvio
        var s = Reassemble(frames);
        Assert.Equal("HELLOWORLD", Encoding.ASCII.GetString(s.ClientToServer.Data));
        Assert.Equal(1, s.ClientToServer.Retransmissions);
    }

    [Fact]
    public void Missing_segment_is_a_gap_and_stream_is_never_complete()
    {
        var frames = FullConversation();
        frames.RemoveAt(3); // HELLO perdido
        var s = Reassemble(frames);
        Assert.Equal(TcpStreamCompleteness.Incomplete, s.Completeness);
        var gap = Assert.Single(s.ClientToServer.Gaps);
        Assert.Equal(0, gap.Offset);
        Assert.Equal(5, gap.Length);
        Assert.Equal("\0\0\0\0\0WORLD", Encoding.ASCII.GetString(s.ClientToServer.Data));
        Assert.StartsWith("Incompleto", s.CompletenessText);
    }

    [Fact]
    public void Bytes_acknowledged_but_not_captured_are_reported()
    {
        // Servidor confirma 10 bytes do cliente, mas a captura só tem os 5 primeiros.
        var frames = new List<byte[]>
        {
            Frames.Tcp(C, 5000, S, 7000, 100, 0, "S"),
            Frames.Tcp(S, 7000, C, 5000, 900, 101, "SA"),
            Frames.Tcp(C, 5000, S, 7000, 101, 901, "PA", B("HELLO")),
            Frames.Tcp(S, 7000, C, 5000, 901, 111, "A"),
        };
        var s = Reassemble(frames);
        Assert.Equal(TcpStreamCompleteness.Incomplete, s.Completeness);
        var gap = Assert.Single(s.ClientToServer.Gaps);
        Assert.Equal(5, gap.Offset);
        Assert.Equal(5, gap.Length);
        Assert.Contains("confirmado", gap.Reason);
    }

    [Fact]
    public void Stream_without_handshake_is_not_called_complete()
    {
        var s = Reassemble(FullConversation().Skip(3));
        Assert.Equal(TcpStreamCompleteness.NoGapsPartialConnection, s.Completeness);
        Assert.Equal("HELLOWORLD", Encoding.ASCII.GetString(s.ClientToServer.Data));
    }

    [Fact]
    public void Separate_connections_become_separate_streams_in_order_of_appearance()
    {
        var r = new TcpReassembler();
        r.Add(Frames.Seg(Frames.Tcp(C, 5001, S, 7000, 1, 0, "S"), 1));
        r.Add(Frames.Seg(Frames.Tcp(C, 5000, S, 7000, 1, 0, "S"), 2));
        r.Add(Frames.Seg(Frames.Tcp(S, 7000, C, 5001, 50, 2, "SA"), 3));
        var streams = r.Build();
        Assert.Equal(2, streams.Count);
        Assert.Equal(5001, streams[0].Client.Port);
        Assert.Equal(2, streams[0].Packets);
        Assert.Equal(5000, streams[1].Client.Port);
    }
}
