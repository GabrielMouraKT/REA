using System.Diagnostics;
using REA.ToolIntegration;

namespace REA.ToolTests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task Lines_are_streamed_and_exit_code_returned()
    {
        var seen = new List<string>();
        var psi = new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/c", "echo linha1&& echo linha2 1>&2&& exit 3" } };
        var r = await ProcessRunner.RunAsync(psi, l => { lock (seen) seen.Add(l.Trim()); }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, r.ExitCode);
        Assert.False(r.TimedOut);
        Assert.Contains("linha1", seen);
        Assert.Contains("linha2", seen);
    }

    [Fact]
    public async Task Timeout_kills_the_whole_process_tree()
    {
        // cmd → ping (processo filho). O tempo esgota e a árvore inteira precisa morrer.
        var psi = new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/c", "ping -n 60 127.0.0.1 >nul" } };
        var before = Process.GetProcessesByName("PING").Select(p => p.Id).ToHashSet();
        var sw = Stopwatch.StartNew();
        var r = await ProcessRunner.RunAsync(psi, timeout: TimeSpan.FromSeconds(2), ct: TestContext.Current.CancellationToken);
        Assert.True(r.TimedOut);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30));
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Process.GetProcessesByName("PING"), p => !before.Contains(p.Id));
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var psi = new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/c", "ping -n 30 127.0.0.1 >nul" } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync(psi, ct: cts.Token));
    }
}

public class GhidraHeadlessTests
{
    private static readonly string Sample = Path.Combine(Environment.SystemDirectory, "choice.exe");

    [Fact]
    public void Inspect_rejects_folders_that_are_not_ghidra()
    {
        Assert.Null(GhidraHeadless.Inspect(Path.GetTempPath()));
    }

    [Fact]
    public async Task Real_ghidra_exports_functions_externals_and_strings()
    {
        var ghidra = GhidraHeadless.Locate();
        Assert.SkipWhen(ghidra is null, "Ghidra não encontrado (defina GHIDRA_INSTALL_DIR).");
        Assert.SkipUnless(File.Exists(Sample), "choice.exe não encontrado.");
        var before = Hash(Sample);

        var log = new List<string>();
        var r = await GhidraHeadless.AnalyzeAsync(ghidra!, Sample, l => { lock (log) log.Add(l); },
            TimeSpan.FromMinutes(10), ct: TestContext.Current.CancellationToken);

        Assert.True(r.Status == GhidraRunStatus.Concluido, $"{r.Status}: {r.Error}\n{string.Join('\n', r.Log.TakeLast(30))}");
        var e = r.Export!;
        Assert.Equal("choice.exe", e.Program, ignoreCase: true);
        Assert.StartsWith("x86:LE:64", e.Language);
        Assert.NotEmpty(e.Functions);
        Assert.Contains(e.Externals, x => x.Library.Contains("KERNEL32", StringComparison.OrdinalIgnoreCase)
                                          || x.Library.StartsWith("API-MS", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(e.Strings);
        Assert.NotEmpty(log);
        Assert.Equal(before, Hash(Sample)); // o binário original não foi alterado
    }

    [Fact]
    public async Task Cancelling_ghidra_stops_and_cleans_up()
    {
        var ghidra = GhidraHeadless.Locate();
        Assert.SkipWhen(ghidra is null, "Ghidra não encontrado (defina GHIDRA_INSTALL_DIR).");
        // Pasta própria: outros testes podem estar rodando o Ghidra ao mesmo tempo na pasta padrão.
        var workRoot = Path.Combine(Path.GetTempPath(), "rea-ghidra-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var r = await GhidraHeadless.AnalyzeAsync(ghidra!, Sample, workRoot: workRoot, ct: cts.Token);

            Assert.Equal(GhidraRunStatus.Cancelado, r.Status);
            Assert.Empty(Directory.Exists(workRoot) ? Directory.GetFileSystemEntries(workRoot) : []);
        }
        finally { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, true); }
    }

    private static string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}

public class KaliCatalogTests
{
    [Fact]
    public async Task Detection_reports_state_without_throwing()
    {
        var report = await KaliCatalog.DetectAsync(TestContext.Current.CancellationToken);
        if (!report.WslAvailable) Assert.NotNull(report.Problem);
        if (report.Distributions.Count == 0) Assert.NotNull(report.Problem);
        Assert.All(report.Distributions, d => Assert.DoesNotContain('\0', d));
    }

    [Fact]
    public async Task Unknown_distribution_is_refused_without_running_anything()
    {
        var report = new WslReport(true, ["kali-linux"], null);
        var s = await KaliCatalog.CheckAsync(report, "outra; rm -rf /", KaliCatalog.Tools[0], TestContext.Current.CancellationToken);
        Assert.False(s.Installed);
        Assert.Contains("não encontrada", s.Problem);
    }

    [Fact]
    public async Task Tools_outside_the_catalog_are_refused()
    {
        var report = new WslReport(true, ["kali-linux"], null);
        var rogue = new KaliTool("x", "x", "x", "bash", ["-c", "echo"]);
        await Assert.ThrowsAsync<ArgumentException>(() => KaliCatalog.CheckAsync(report, "kali-linux", rogue, TestContext.Current.CancellationToken));
    }
}
