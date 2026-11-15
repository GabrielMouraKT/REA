using System.Diagnostics;
using System.Text;

namespace REA.ToolIntegration;

/// <summary>Ferramenta conhecida. O comando de verificação é fixo; o REA não executa comandos arbitrários no WSL.</summary>
public sealed record KaliTool(string Id, string Name, string Purpose, string Executable, IReadOnlyList<string> VersionArgs);

public sealed record KaliToolStatus(KaliTool Tool, bool Installed, string? Version, string? Problem);

public sealed record WslReport(bool WslAvailable, IReadOnlyList<string> Distributions, string? Problem);

/// <summary>
/// Detecta o WSL e as distribuições instaladas, e verifica se ferramentas de uma lista fixa estão presentes
/// (apenas consulta de versão). Não instala nada e não executa comandos contra alvos.
/// </summary>
public static class KaliCatalog
{
    public static readonly IReadOnlyList<KaliTool> Tools =
    [
        new("nmap", "Nmap", "Inventário de portas e serviços do seu próprio servidor de laboratório", "nmap", ["--version"]),
        new("tshark", "TShark", "Análise de capturas pela linha de comando", "tshark", ["--version"]),
        new("tcpdump", "tcpdump", "Captura de tráfego dentro do Linux", "tcpdump", ["--version"]),
        new("mitmproxy", "mitmproxy", "Proxy HTTP(S) para os seus próprios clientes de teste", "mitmproxy", ["--version"]),
        new("binwalk", "binwalk", "Identificação de dados embutidos em arquivos", "binwalk", ["--help"]),
        new("radare2", "radare2", "Desmontagem e análise de binários", "r2", ["-v"]),
    ];

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    public static string? LocateWsl()
    {
        var path = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        return File.Exists(path) ? path : null;
    }

    public static async Task<WslReport> DetectAsync(CancellationToken ct = default)
    {
        var wsl = LocateWsl();
        if (wsl is null) return new WslReport(false, [], "WSL não está instalado neste Windows.");
        var r = await ProcessRunner.RunAsync(Psi(wsl, ["--list", "--quiet"]), timeout: CallTimeout, ct: ct);
        if (r.TimedOut) return new WslReport(true, [], "O WSL não respondeu a tempo.");
        var distros = r.Lines.Select(Clean).Where(l => l.Length > 0).ToList();
        if (r.ExitCode != 0 && distros.Count == 0)
            return new WslReport(true, [], "WSL instalado, mas nenhuma distribuição Linux (ex.: kali-linux) foi encontrada.");
        return new WslReport(true, distros, distros.Count == 0 ? "WSL instalado, mas sem distribuições Linux." : null);
    }

    /// <summary>Verifica uma ferramenta do catálogo numa distribuição que o próprio WSL listou.</summary>
    public static async Task<KaliToolStatus> CheckAsync(WslReport report, string distribution, KaliTool tool, CancellationToken ct = default)
    {
        if (!Tools.Contains(tool)) throw new ArgumentException("Ferramenta fora do catálogo.", nameof(tool));
        if (!report.Distributions.Contains(distribution, StringComparer.Ordinal))
            return new KaliToolStatus(tool, false, null, $"Distribuição '{distribution}' não encontrada no WSL.");
        var wsl = LocateWsl()!;
        var args = new List<string> { "-d", distribution, "--exec", tool.Executable };
        args.AddRange(tool.VersionArgs);
        var r = await ProcessRunner.RunAsync(Psi(wsl, args), timeout: CallTimeout, ct: ct);
        if (r.TimedOut) return new KaliToolStatus(tool, false, null, "A verificação não respondeu a tempo.");
        var first = r.Lines.Select(Clean).FirstOrDefault(l => l.Length > 0);
        var missing = r.ExitCode is 1 or 127 && (first is null || first.Contains("not found", StringComparison.OrdinalIgnoreCase)
                                                  || first.Contains("No such file", StringComparison.OrdinalIgnoreCase));
        if (missing || (r.ExitCode != 0 && first is null))
            return new KaliToolStatus(tool, false, null, "Não instalada nesta distribuição.");
        return new KaliToolStatus(tool, true, first, r.ExitCode == 0 ? null : $"Respondeu com código {r.ExitCode}.");
    }

    private static ProcessStartInfo Psi(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe) { StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        psi.Environment["WSL_UTF8"] = "1"; // sem isto o wsl.exe responde em UTF-16
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private static string Clean(string s) => s.Replace("\0", "").Trim();
}
