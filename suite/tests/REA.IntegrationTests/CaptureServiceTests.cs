using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using PacketDotNet;
using REA.Core;
using REA.Domain;
using REA.PacketAnalyzer;

namespace REA.IntegrationTests;

internal static class CaptureFixtures
{
    public static string MakePcapNg(string dir, int packets = 3)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"origem-{Guid.NewGuid():N}.pcapng");
        using var w = new PcapNgWriter(path, LinkTypes.Ethernet, 65535, "teste", null);
        for (var i = 0; i < packets; i++)
        {
            var tcp = new TcpPacket(5000, 7000) { SequenceNumber = (uint)(100 + i), Synchronize = i == 0, PayloadData = i == 0 ? [] : [(byte)i] };
            var ip = new IPv4Packet(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")) { PayloadPacket = tcp };
            var eth = new EthernetPacket(PhysicalAddress.Parse("00-11-22-33-44-55"), PhysicalAddress.Parse("66-77-88-99-AA-BB"), EthernetType.IPv4)
                { PayloadPacket = ip };
            tcp.UpdateCalculatedValues();
            ip.UpdateCalculatedValues();
            w.WritePacket(DateTimeOffset.UtcNow, eth.Bytes, eth.Bytes.Length);
        }
        return path;
    }

    public static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static async Task<Guid> Project(TestApp app, string name) =>
        (await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput(name, null, null)))).Value;

    public static async Task<Guid> Environment(TestApp app, Guid project, string name) =>
        (await app.Run<EnvironmentService, OperationResult<Guid>>(s =>
            s.CreateAsync(project, new EnvironmentInput(name, EnvironmentKind.LocalLaboratory, null, null, null, null)))).Value;
}

public class CaptureImportTests
{
    [Fact]
    public async Task Import_copies_hashes_analyzes_audits_and_survives_restart_without_touching_the_original()
    {
        using var root = new TempDataRoot();
        var source = CaptureFixtures.MakePcapNg(Path.Combine(root.Path, "externo"), packets: 4);
        var originalHash = CaptureFixtures.Sha(source);
        Guid captureId, projectId;

        await using (var app = await TestApp.StartAsync(root.Path))
        {
            projectId = await CaptureFixtures.Project(app, "Captura importada");
            var env = await CaptureFixtures.Environment(app, projectId, "Lab");
            var r = await app.Run<CaptureService, OperationResult<Guid>>(s => s.ImportAsync(new ImportCaptureInput(projectId, env, source, "Sessão 1")));
            Assert.True(r.Success, r.Error);
            captureId = r.Value;
        }

        Assert.Equal(originalHash, CaptureFixtures.Sha(source)); // original intacto
        await using var reopened = await TestApp.StartAsync(root.Path);
        var c = await reopened.Run<CaptureService, Capture?>(s => s.GetAsync(captureId));
        Assert.NotNull(c);
        Assert.Equal(CaptureSource.Imported, c.Source);
        Assert.Equal(originalHash, c.Sha256);
        Assert.Equal(4, c.PacketCount);
        Assert.Equal(1, c.TcpStreamCount);
        Assert.Equal(CaptureAnalysisStatus.Completed, c.AnalysisStatus);
        Assert.Equal("TCP: 4", c.ProtocolSummary);
        var stored = await reopened.Run<CaptureService, string>(s => Task.FromResult(s.ResolvePath(c)));
        Assert.StartsWith(reopened.Paths.CaptureDirectory, stored);
        Assert.Equal(originalHash, CaptureFixtures.Sha(stored));

        var log = await reopened.Run<IActivityLog, IReadOnlyList<ActivityEntry>>(a => a.RecentAsync(10, projectId));
        Assert.Contains(log, e => e.Action == "Importar captura" && e.Summary.Contains(originalHash));
    }

    [Fact]
    public async Task Import_rejects_non_capture_archived_project_and_foreign_environment()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var a = await CaptureFixtures.Project(app, "Projeto A");
        var b = await CaptureFixtures.Project(app, "Projeto B");
        var envB = await CaptureFixtures.Environment(app, b, "Só do B");
        var text = Path.Combine(root.Path, "nao-e-captura.txt");
        await File.WriteAllTextAsync(text, "texto", TestContext.Current.CancellationToken);
        var pcap = CaptureFixtures.MakePcapNg(Path.Combine(root.Path, "externo"));

        var notCapture = await app.Run<CaptureService, OperationResult<Guid>>(s => s.ImportAsync(new ImportCaptureInput(a, null, text, null)));
        Assert.Equal("O arquivo não é PCAP nem PCAPNG.", notCapture.Error);

        var foreignEnv = await app.Run<CaptureService, OperationResult<Guid>>(s => s.ImportAsync(new ImportCaptureInput(a, envB, pcap, null)));
        Assert.Equal("Ambiente não encontrado neste projeto.", foreignEnv.Error);

        await app.Run<ProjectService, OperationResult>(s => s.ArchiveAsync(a));
        var archived = await app.Run<CaptureService, OperationResult<Guid>>(s => s.ImportAsync(new ImportCaptureInput(a, null, pcap, null)));
        Assert.False(archived.Success);
        Assert.Contains("arquivado", archived.Error);
    }

    [Fact]
    public async Task Environment_and_project_with_captures_cannot_be_deleted()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var p = await CaptureFixtures.Project(app, "Com captura");
        var env = await CaptureFixtures.Environment(app, p, "Lab");
        var pcap = CaptureFixtures.MakePcapNg(Path.Combine(root.Path, "externo"));
        Assert.True((await app.Run<CaptureService, OperationResult<Guid>>(s => s.ImportAsync(new ImportCaptureInput(p, env, pcap, null)))).Success);

        var delEnv = await app.Run<EnvironmentService, OperationResult>(s => s.DeleteAsync(p, env));
        Assert.False(delEnv.Success);
        Assert.Contains("capturas", delEnv.Error);

        // Mesmo sem ambiente, captura vinculada ao projeto bloqueia exclusão.
        var p2 = await CaptureFixtures.Project(app, "Só captura");
        await app.Run<CaptureService, OperationResult<Guid>>(s => s.ImportAsync(new ImportCaptureInput(p2, null, pcap, null)));
        var delProject = await app.Run<ProjectService, OperationResult>(s => s.DeleteAsync(p2));
        Assert.False(delProject.Success);
        Assert.Contains("capturas", delProject.Error);
    }

    [Fact]
    public async Task Dashboard_counts_captures_packets_and_streams()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var p = await CaptureFixtures.Project(app, "Dash");
        await app.Run<CaptureService, OperationResult<Guid>>(s =>
            s.ImportAsync(new ImportCaptureInput(p, null, CaptureFixtures.MakePcapNg(Path.Combine(root.Path, "x"), 5), null)));
        var d = await app.Run<DashboardService, DashboardSnapshot>(s => s.GetAsync());
        Assert.Equal(1, d.Captures);
        Assert.Equal(5, d.PacketsAnalyzed);
        Assert.Equal(1, d.TcpStreams);
    }
}

public class LiveCaptureServiceTests
{
    private static async Task GenerateTrafficAsync(int port, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, ct);
        await client.GetStream().WriteAsync("ola"u8.ToArray(), ct);
        client.Client.Shutdown(SocketShutdown.Send);
    }

    [Fact]
    public async Task Live_capture_through_the_service_is_saved_hashed_and_analyzed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var engine = app.Singleton<ILiveCaptureEngine>();
        if (engine.UnavailableReason is { } reason) Assert.Skip(reason);
        var loopback = engine.ListInterfaces().FirstOrDefault(i => i.IsLoopback);
        if (loopback is null) Assert.Skip("Sem interface loopback.");

        var p = await CaptureFixtures.Project(app, "Ao vivo");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var started = await app.Run<CaptureService, OperationResult<ActiveCapture>>(s => s.StartLiveAsync(new StartCaptureInput(p, null, "Teste ao vivo",
            new LiveCaptureOptions(loopback.Name, $"tcp port {port}", null, null, TimeSpan.FromSeconds(30)))));
        Assert.True(started.Success, started.Error);
        var active = started.Value!;
        var listing = await app.Run<CaptureService, PagedResult<CaptureSummary>>(s => s.ListAsync(new CaptureQuery(p, null, new PageRequest())));
        Assert.Equal(CaptureStatus.Capturing, Assert.Single(listing.Items).Status);

        await Task.Delay(500, ct);
        var accept = listener.AcceptTcpClientAsync(ct).AsTask();
        await GenerateTrafficAsync(port, ct);
        using (var server = await accept) await server.GetStream().ReadAtLeastAsync(new byte[3], 3, throwOnEndOfStream: false, ct);
        listener.Stop();
        await Task.Delay(800, ct);

        await active.Session.StopAsync();
        var result = await active.Session.Completion;
        var fin = await app.Run<CaptureService, OperationResult>(s => s.FinalizeLiveAsync(active.CaptureId, result));
        Assert.True(fin.Success, fin.Error);

        var c = await app.Run<CaptureService, Capture?>(s => s.GetAsync(active.CaptureId));
        Assert.Equal(CaptureStatus.Completed, c!.Status);
        Assert.True(c.PacketCount >= 3);
        Assert.Equal(result.Stats.Packets, c.PacketCount);
        Assert.Equal(1, c.TcpStreamCount);
        var storedPath = await app.Run<CaptureService, string>(s => Task.FromResult(s.ResolvePath(c)));
        Assert.Equal(CaptureFixtures.Sha(storedPath), c.Sha256);
        Assert.Equal("pcapng", c.FileFormat);
    }

    [Fact]
    public async Task Capture_left_running_by_a_crash_is_recovered_as_interrupted()
    {
        var ct = TestContext.Current.CancellationToken;
        using var root = new TempDataRoot();
        Guid id;
        await using (var app = await TestApp.StartAsync(root.Path))
        {
            var engine = app.Singleton<ILiveCaptureEngine>();
            if (engine.UnavailableReason is { } reason) Assert.Skip(reason);
            var loopback = engine.ListInterfaces().FirstOrDefault(i => i.IsLoopback);
            if (loopback is null) Assert.Skip("Sem interface loopback.");
            var p = await CaptureFixtures.Project(app, "Queda");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var started = await app.Run<CaptureService, OperationResult<ActiveCapture>>(s => s.StartLiveAsync(new StartCaptureInput(p, null, "Vai cair",
                new LiveCaptureOptions(loopback.Name, $"tcp port {port}", null, null, null))));
            id = started.Value!.CaptureId;
            await Task.Delay(500, ct);
            var accept = listener.AcceptTcpClientAsync(ct).AsTask();
            await GenerateTrafficAsync(port, ct);
            (await accept).Dispose();
            listener.Stop();
            await Task.Delay(800, ct);
            // "Queda": a sessão é encerrada, mas o registro nunca é finalizado.
            await started.Value.Session.StopAsync();
        }

        await using var reopened = await TestApp.StartAsync(root.Path);
        Assert.Equal(1, await reopened.Run<CaptureService, int>(s => s.RecoverInterruptedAsync()));
        var c = await reopened.Run<CaptureService, Capture?>(s => s.GetAsync(id));
        Assert.Equal(CaptureStatus.Interrupted, c!.Status);
        Assert.True(c.PacketCount > 0);
        Assert.NotNull(c.Sha256);
        Assert.Equal(0, await reopened.Run<CaptureService, int>(s => s.RecoverInterruptedAsync())); // idempotente
    }
}
