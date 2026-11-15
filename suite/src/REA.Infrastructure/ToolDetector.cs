using System.Diagnostics;
using REA.Core;

namespace REA.Infrastructure;

/// <summary>Detecta ferramentas externas de verdade (arquivo presente + versão reportada pela própria ferramenta).</summary>
internal sealed class ToolDetector : IToolDetector
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public async Task<IReadOnlyList<ToolStatus>> DetectAsync(CancellationToken ct = default)
    {
        var tasks = new[]
        {
            DetectNpcap(),
            DetectTshark(ct),
            DetectGhidra(),
            DetectJava(ct),
            DetectReaCli(ct),
            DetectWsl(ct),
            DetectWix(ct),
        };
        return await Task.WhenAll(tasks);
    }

    private static Task<ToolStatus> DetectNpcap()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap");
        var dll = Path.Combine(dir, "wpcap.dll");
        // wpcap.dll traz a versão do libpcap; a versão do Npcap está no Packet.dll.
        var packet = Path.Combine(dir, "Packet.dll");
        string? version = File.Exists(packet) ? FileVersionInfo.GetVersionInfo(packet).ProductVersion : null;
        return Task.FromResult(new ToolStatus("Npcap", File.Exists(dll), version, File.Exists(dll) ? dir : null,
            "Captura de pacotes (Network Capture). Instalar de npcap.com com suporte a loopback."));
    }

    private static async Task<ToolStatus> DetectTshark(CancellationToken ct)
    {
        var path = FindOnPath("tshark.exe") ?? Existing(Path.Combine(ProgramFiles(), "Wireshark", "tshark.exe"));
        var version = path is null ? null : FirstLine(await Run(path, "--version", ct));
        return new ToolStatus("TShark (Wireshark)", path is not null && version is not null, version, path,
            "Leitura e validação de PCAP/PCAPNG, análise HTTP.");
    }

    private static Task<ToolStatus> DetectGhidra()
    {
        var home = Environment.GetEnvironmentVariable("GHIDRA_INSTALL_DIR");
        var headless = home is null ? null : Existing(Path.Combine(home, "support", "analyzeHeadless.bat"));
        string? version = null;
        var props = home is null ? null : Path.Combine(home, "Ghidra", "application.properties");
        if (props is not null && File.Exists(props))
        {
            version = File.ReadLines(props).FirstOrDefault(l => l.StartsWith("application.version=", StringComparison.Ordinal))
                ?.Split('=', 2)[1];
        }
        return Task.FromResult(new ToolStatus("Ghidra", headless is not null, version, headless,
            "Análise estática (Ghidra Integration). Defina GHIDRA_INSTALL_DIR."));
    }

    private static async Task<ToolStatus> DetectJava(CancellationToken ct)
    {
        var path = FindOnPath("java.exe");
        var output = path is null ? null : await Run(path, "-version", ct);
        return new ToolStatus("Java (JDK)", output is not null, FirstLine(output), path, "Requisito do Ghidra (JDK 21+).");
    }

    private static async Task<ToolStatus> DetectReaCli(CancellationToken ct)
    {
        var node = FindOnPath("node.exe");
        var script = LocateReaScript();
        string? version = null;
        if (node is not null && script is not null)
            version = FirstLine(await Run(node, $"\"{script}\" --version", ct));
        return new ToolStatus("REA CLI", version is not null, version, script,
            "Motor de engenharia reversa (rea-agents). Requer Node.js 22.19+/24.11+ e `npm ci && npm run build:cached` no repositório.");
    }

    private static async Task<ToolStatus> DetectWsl(CancellationToken ct)
    {
        var wsl = Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"));
        string? distros = null;
        if (wsl is not null)
        {
            var output = await Run(wsl, "-l -q", ct);
            // wsl.exe escreve em UTF-16; remove NULs e linhas vazias.
            distros = output is null ? null : string.Join(", ",
                output.Replace("\0", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        var hasDistro = !string.IsNullOrWhiteSpace(distros);
        return new ToolStatus("WSL / Kali", hasDistro, hasDistro ? distros : null, wsl,
            "Opcional: Kali Tools. Instalar com `wsl --install -d kali-linux`.");
    }

    private static async Task<ToolStatus> DetectWix(CancellationToken ct)
    {
        var path = FindOnPath("wix.exe");
        var version = path is null ? null : FirstLine(await Run(path, "--version", ct));
        return new ToolStatus("WiX Toolset", version is not null, version, path,
            "Geração de instalador MSI. Instalar com `dotnet tool install --global wix`.");
    }

    internal static string? LocateReaScript()
    {
        var configured = Environment.GetEnvironmentVariable("REA_CLI_SCRIPT");
        if (!string.IsNullOrWhiteSpace(configured)) return Existing(configured);
        // Procura o repositório REA subindo a partir do executável (instalação de desenvolvimento).
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "rea.mjs");
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "package.json"))) return candidate;
        }
        return null;
    }

    private static async Task<string?> Run(string file, string args, CancellationToken ct)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    // O REA CLI é um servidor MCP via stdio: sem stdin fechado ele fica esperando entrada.
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            p.Start();
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return null;
            }
            if (p.ExitCode != 0) return null;
            var text = (await stdout).Trim();
            return text.Length > 0 ? text : (await stderr).Trim(); // java -version escreve em stderr
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? FirstLine(string? text) =>
        text?.Replace("\0", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(d => Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim().Trim('"')), exe))
        .FirstOrDefault(File.Exists);

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    private static string ProgramFiles() => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
}
