using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using REA.Core;

namespace REA.ToolIntegration;

/// <summary>
/// GitHub pelo GitHub CLI oficial (`gh`), reaproveitando o login que ele guarda no cofre do Windows. O REA nunca
/// pede senha, nunca lê nem guarda o token. Para conectar/desconectar, o usuário usa `gh auth login/logout`.
/// </summary>
public sealed class GitHubCli : IGitHubClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public string? Locate()
    {
        var fromPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim().Trim('"')), "gh.exe")).FirstOrDefault(File.Exists);
        if (fromPath is not null) return fromPath;
        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe");
        return File.Exists(pf) ? pf : null;
    }

    private async Task<(bool Ok, string Output)> RunAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var gh = Locate();
        if (gh is null) return (false, "O GitHub CLI (gh) não está instalado. Instale em cli.github.com e rode 'gh auth login'.");
        var psi = new ProcessStartInfo(gh);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["GH_SPINNER_DISABLED"] = "1";
        var r = await ProcessRunner.RunAsync(psi, timeout: Timeout, ct: ct);
        var text = string.Join('\n', r.Lines);
        if (r.TimedOut) return (false, "O GitHub não respondeu a tempo.");
        return (r.ExitCode == 0, text);
    }

    public async Task<OperationResult<GitHubAccount>> AccountAsync(CancellationToken ct = default)
    {
        var (ok, user) = await RunAsync(["api", "user"], ct);
        if (!ok) return OperationResult<GitHubAccount>.Fail(Friendly(user));
        using var doc = JsonDocument.Parse(user);
        var root = doc.RootElement;
        var login = root.GetProperty("login").GetString() ?? "";
        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;

        var scopes = new List<string>();
        var (_, status) = await RunAsync(["auth", "status", "--active"], ct);
        var line = status.Split('\n').FirstOrDefault(l => l.Contains("Token scopes", StringComparison.OrdinalIgnoreCase));
        if (line is not null)
            scopes.AddRange(line[(line.IndexOf(':') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.Trim('\'')));

        var orgs = new List<string>();
        var (okOrgs, orgJson) = await RunAsync(["api", "user/orgs"], ct);
        if (okOrgs)
        {
            using var od = JsonDocument.Parse(orgJson);
            orgs.AddRange(od.RootElement.EnumerateArray().Select(o => o.GetProperty("login").GetString() ?? "").Where(s => s.Length > 0));
        }
        return OperationResult<GitHubAccount>.Ok(new GitHubAccount(login, name, url, scopes, orgs));
    }

    private const string RepoFields = "nameWithOwner,description,url,visibility,isArchived,updatedAt,diskUsage,defaultBranchRef";

    public async Task<OperationResult<IReadOnlyList<GitHubRepoInfo>>> ListReposAsync(string? owner, int limit, CancellationToken ct = default)
    {
        var args = new List<string> { "repo", "list" };
        if (!string.IsNullOrWhiteSpace(owner)) { ValidateName(owner); args.Add(owner); }
        args.AddRange(["--limit", Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture), "--json", RepoFields]);
        var (ok, output) = await RunAsync(args, ct);
        if (!ok) return OperationResult<IReadOnlyList<GitHubRepoInfo>>.Fail(Friendly(output));
        using var doc = JsonDocument.Parse(output);
        return OperationResult<IReadOnlyList<GitHubRepoInfo>>.Ok(doc.RootElement.EnumerateArray().Select(ParseRepo).ToList());
    }

    public async Task<OperationResult<GitHubRepoInfo>> CreateRepoAsync(string name, bool isPrivate, string? owner, string? description, CancellationToken ct = default)
    {
        ValidateName(name);
        if (owner is not null) ValidateName(owner);
        var full = owner is null ? name : $"{owner}/{name}";
        var args = new List<string> { "repo", "create", full, isPrivate ? "--private" : "--public" };
        if (!string.IsNullOrWhiteSpace(description)) { args.Add("--description"); args.Add(description.Trim()); }
        var (ok, output) = await RunAsync(args, ct);
        if (!ok) return OperationResult<GitHubRepoInfo>.Fail(Friendly(output));
        var (okView, view) = await RunAsync(["repo", "view", full, "--json", RepoFields], ct);
        if (!okView) return OperationResult<GitHubRepoInfo>.Fail($"Repositório criado, mas não foi possível consultá-lo: {Friendly(view)}");
        using var doc = JsonDocument.Parse(view);
        return OperationResult<GitHubRepoInfo>.Ok(ParseRepo(doc.RootElement));
    }

    public static GitHubRepoInfo ParseRepo(JsonElement e)
    {
        string? Str(string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        DateTimeOffset? updated = DateTimeOffset.TryParse(Str("updatedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        long? disk = e.TryGetProperty("diskUsage", out var du) && du.TryGetInt64(out var kb) ? kb : null;
        string? branch = e.TryGetProperty("defaultBranchRef", out var b) && b.ValueKind == JsonValueKind.Object && b.TryGetProperty("name", out var bn) ? bn.GetString() : null;
        return new GitHubRepoInfo(Str("nameWithOwner") ?? "", Str("description"), Str("url") ?? "",
            (Str("visibility") ?? "").ToLowerInvariant() switch { "private" => "Privado", "public" => "Público", "internal" => "Interno", var v => v },
            e.TryGetProperty("isArchived", out var a) && a.ValueKind == JsonValueKind.True, updated, disk, string.IsNullOrEmpty(branch) ? null : branch);
    }

    /// <summary>Nomes de conta/repositório do GitHub: letras, números, '-', '_' e '.'.</summary>
    public static void ValidateName(string name)
    {
        if (name.Length is 0 or > 100 || name.StartsWith('-') || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new ArgumentException($"Nome inválido para o GitHub: '{name}'. Use letras, números, '-', '_' ou '.'.");
    }

    private static string Friendly(string output)
    {
        if (output.Contains("auth login", StringComparison.OrdinalIgnoreCase) || output.Contains("not logged", StringComparison.OrdinalIgnoreCase))
            return "Nenhuma conta do GitHub conectada. Abra o PowerShell e rode: gh auth login";
        if (output.Contains("Name already exists", StringComparison.OrdinalIgnoreCase)) return "Já existe um repositório com esse nome nesta conta.";
        if (output.Contains("HTTP 403", StringComparison.Ordinal) || output.Contains("Resource not accessible", StringComparison.OrdinalIgnoreCase))
            return "A conta conectada não tem permissão para esta operação.";
        if (output.Contains("Could not resolve", StringComparison.OrdinalIgnoreCase) || output.Contains("connect", StringComparison.OrdinalIgnoreCase) && output.Contains("error", StringComparison.OrdinalIgnoreCase))
            return "Sem conexão com o GitHub.";
        return output.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "Falha no GitHub CLI.";
    }
}
