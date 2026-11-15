using System.Net;
using System.Net.Sockets;
using System.Text;
using REA.Core;
using REA.NetworkCapture;
using REA.PacketAnalyzer;

namespace REA.NetworkTests;

/// <summary>
/// Critério obrigatório da Fase 2, com tráfego REAL:
/// 1) capturar loopback, 2) salvar PCAPNG, 3) fechar, 4) reabrir, 5) visualizar pacotes,
/// 6) reconstruir o fluxo de teste, 7) comparar com o TShark.
/// Pulado (com motivo) quando o Npcap não está instalado.
/// </summary>
public class LiveLoopbackCaptureTests
{
    [Fact]
    public async Task Real_loopback_capture_saves_reopens_reassembles_and_matches_tshark()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = new SharpPcapCaptureEngine();
        if (engine.UnavailableReason is { } reason) Assert.Skip($"Npcap indisponível: {reason}");
        var loopback = engine.ListInterfaces().FirstOrDefault(i => i.IsLoopback);
        if (loopback is null) Assert.Skip("Npcap instalado sem suporte a loopback.");

        using var dir = new TempDir();
        var path = dir.File("loopback.pcapng");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // 1) captura real
        Assert.Null(engine.ValidateFilter(loopback.Name, $"tcp port {port}"));
        Assert.NotNull(engine.ValidateFilter(loopback.Name, "isto não é bpf ((("));
        var capture = engine.Start(new LiveCaptureOptions(loopback.Name, $"tcp port {port}", null, null, TimeSpan.FromSeconds(30)), path);
        await Task.Delay(500, ct); // driver pronto

        var request = Encoding.ASCII.GetBytes("REA-PING:" + new string('x', 3000)); // maior que um segmento típico
        var response = Encoding.ASCII.GetBytes("REA-PONG:ok");
        var server = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync(ct);
            var s = c.GetStream();
            var buffer = new byte[request.Length];
            await s.ReadExactlyAsync(buffer, ct);
            await s.WriteAsync(response, ct);
            c.Client.Shutdown(SocketShutdown.Send);
            await s.ReadAtLeastAsync(new byte[1], 1, throwOnEndOfStream: false, ct); // espera o FIN do cliente
        }, ct);
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port, ct);
            var s = client.GetStream();
            await s.WriteAsync(request, ct);
            var got = new byte[response.Length];
            await s.ReadExactlyAsync(got, ct);
            Assert.Equal(response, got);
            client.Client.Shutdown(SocketShutdown.Send);
            await s.ReadAtLeastAsync(new byte[1], 1, throwOnEndOfStream: false, ct);
        }
        await server;
        listener.Stop();
        await Task.Delay(800, ct); // últimos pacotes saem do buffer do driver

        // 2) + 3) salvar e fechar
        await capture.StopAsync();
        var result = await capture.Completion;
        Assert.Equal(LiveCaptureEndReason.Stopped, result.Reason);
        Assert.True(result.Stats.Packets >= 6, $"poucos pacotes capturados: {result.Stats.Packets}");
        await capture.DisposeAsync();

        // 4) + 5) reabrir e visualizar
        var doc = await CaptureDocument.OpenAsync(path, ct);
        Assert.Null(doc.Index.TruncationReason);
        Assert.Equal(result.Stats.Packets, doc.PacketCount);
        var page = await doc.ReadSummariesAsync(Enumerable.Range(1, doc.PacketCount).Select(i => (long)i), ct);
        Assert.All(page, p => Assert.Equal("TCP", p.Protocol));
        Assert.Contains(page, p => p.TcpFlags == "SYN");

        // 6) reconstruir o fluxo
        var stream = Assert.Single(await doc.ReassembleTcpAsync(ct));
        Assert.Equal(port, stream.Server.Port);
        Assert.Equal(request, stream.ClientToServer.Data);
        Assert.Equal(response, stream.ServerToClient.Data);
        Assert.Equal(TcpStreamCompleteness.Complete, stream.Completeness);

        // 7) comparar com ferramenta independente
        if (TsharkCrossCheck.Locate() is null) Assert.Skip("TShark não instalado: etapa 7 não verificada.");
        var report = await TsharkCrossCheck.RunAsync(path, ct: ct);
        Assert.True(report.PacketsMatch, $"REA {report.ReaPackets} × TShark {report.TsharkPackets}");
        Assert.True(report.StreamsMatch, $"REA {report.ReaTcpStreams} × TShark {report.TsharkTcpConversations}");
        Assert.True(report.PayloadsMatch);
    }

    [Fact]
    public async Task Packet_limit_stops_the_capture_by_itself()
    {
        var ct = TestContext.Current.CancellationToken;
        var engine = new SharpPcapCaptureEngine();
        if (engine.UnavailableReason is { } reason) Assert.Skip($"Npcap indisponível: {reason}");
        var loopback = engine.ListInterfaces().FirstOrDefault(i => i.IsLoopback);
        if (loopback is null) Assert.Skip("Npcap instalado sem suporte a loopback.");

        using var dir = new TempDir();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var capture = engine.Start(new LiveCaptureOptions(loopback.Name, $"tcp port {port}", 3, null, TimeSpan.FromSeconds(20)), dir.File("limit.pcapng"));
        await Task.Delay(500, ct);
        for (var i = 0; i < 3; i++)
        {
            using var c = new TcpClient();
            await c.ConnectAsync(IPAddress.Loopback, port, ct);
            using var accepted = await listener.AcceptTcpClientAsync(ct);
        }
        listener.Stop();
        var result = await capture.Completion.WaitAsync(TimeSpan.FromSeconds(15), ct);
        Assert.Equal(LiveCaptureEndReason.PacketLimit, result.Reason);
        using var r = new CaptureFileReader(dir.File("limit.pcapng"));
        Assert.Equal(3, r.ReadAll(ct).Count());
    }
}
