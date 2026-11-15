using System.Diagnostics;
using System.Globalization;
using System.Text;
using REA.Core;

namespace REA.ToolIntegration;

/// <summary>
/// Git pela linha de comando oficial. Argumentos sempre em lista (nada de texto do usuário montando comando),
/// sem prompts interativos (GIT_TERMINAL_PROMPT=0) e com mensagens do Git em inglês para leitura estável;
/// o REA traduz as falhas comuns para linguagem simples.
/// </summary>
public sealed class GitCli : IGitClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);
    private const char Us = '\u001F';
    private const char Rs = '\u001E';

    public string? Locate()
    {
        var fromPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim().Trim('"')), "git.exe")).FirstOrDefault(File.Exists);
        if (fromPath is not null) return fromPath;
        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
        return File.Exists(pf) ? pf : null;
    }

    public async Task<GitResult> RunAsync(string repoPath, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var git = Locate();
        var display = "git " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        if (git is null) return new GitResult(false, "", "git não encontrado", "O Git não está instalado (git-scm.com).", display);

        var psi = new ProcessStartInfo(git) { WorkingDirectory = Directory.Exists(repoPath) ? repoPath : Environment.CurrentDirectory };
        psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(repoPath);
        psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("core.quotepath=off");
        psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("color.ui=false");
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANG"] = "C";
        psi.Environment["GIT_EDITOR"] = "true"; // nunca abrir editor (merge/rebase usam mensagens padrão)

        var outcome = await ProcessRunner.RunAsync(psi, timeout: DefaultTimeout, ct: ct);
        var text = string.Join('\n', outcome.Lines);
        if (outcome.TimedOut) return new GitResult(false, text, "timeout", "O Git não respondeu a tempo e foi encerrado.", display);
        return outcome.ExitCode == 0
            ? new GitResult(true, text, null, null, display)
            : new GitResult(false, text, text, Friendly(text), display);
    }

    /// <summary>Saída bruta separada por NUL (para status -z), sem misturar stderr.</summary>
    private static async Task<(int Exit, string Out, string Err)> RunRawAsync(string git, string repoPath, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(git)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(repoPath);
        psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("core.quotepath=off");
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar o Git.");
        p.StandardInput.Close();
        var o = p.StandardOutput.ReadToEndAsync(ct);
        var e = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
        return (p.ExitCode, await o, await e);
    }

    private async Task<string> RequireAsync(string repoPath, IEnumerable<string> args, CancellationToken ct)
    {
        var git = Locate() ?? throw new InvalidOperationException("O Git não está instalado.");
        var (exit, output, err) = await RunRawAsync(git, repoPath, args, ct);
        if (exit != 0) throw new InvalidOperationException(Friendly(err) ?? err.Trim());
        return output;
    }

    public async Task<string?> FindRootAsync(string path, CancellationToken ct = default)
    {
        var git = Locate();
        if (git is null || !Directory.Exists(path)) return null;
        var (exit, output, _) = await RunRawAsync(git, path, ["rev-parse", "--show-toplevel"], ct);
        return exit == 0 ? Path.GetFullPath(output.Trim()) : null;
    }

    public async Task<GitStatus> StatusAsync(string repoPath, CancellationToken ct = default)
    {
        var raw = await RequireAsync(repoPath, ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all"], ct);
        var gitDir = (await RequireAsync(repoPath, ["rev-parse", "--absolute-git-dir"], ct)).Trim();
        return ParseStatus(raw, OperationInProgress(gitDir));
    }

    private static string? OperationInProgress(string gitDir)
    {
        if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply"))) return "Rebase";
        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))) return "Merge";
        if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))) return "Cherry-pick";
        if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD"))) return "Revert";
        if (File.Exists(Path.Combine(gitDir, "BISECT_LOG"))) return "Bisect";
        return null;
    }

    /// <summary>Lê `git status --porcelain=v2 --branch -z`.</summary>
    public static GitStatus ParseStatus(string raw, string? operation = null)
    {
        string? branch = null, oid = null, upstream = null;
        var detached = false;
        int ahead = 0, behind = 0;
        var changes = new List<GitFileChange>();
        var fields = raw.Split('\0');
        for (var i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            if (f.Length == 0) continue;
            if (f.StartsWith("# branch.oid ", StringComparison.Ordinal)) { var v = f[13..]; oid = v == "(initial)" ? null : v; }
            else if (f.StartsWith("# branch.head ", StringComparison.Ordinal)) { var v = f[14..]; detached = v == "(detached)"; branch = detached ? null : v; }
            else if (f.StartsWith("# branch.upstream ", StringComparison.Ordinal)) upstream = f[18..];
            else if (f.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                var p = f[12..].Split(' ');
                ahead = int.Parse(p[0].TrimStart('+'), CultureInfo.InvariantCulture);
                behind = int.Parse(p[1].TrimStart('-'), CultureInfo.InvariantCulture);
            }
            else if (f[0] == '1' || f[0] == '2')
            {
                var parts = f.Split(' ', f[0] == '1' ? 9 : 10);
                var xy = parts[1];
                var path = parts[^1];
                string? old = null;
                if (f[0] == '2') old = fields[++i];
                changes.Add(new GitFileChange(path, old, KindOf(xy, f[0] == '2'), xy[0] != '.', xy[1] != '.'));
            }
            else if (f[0] == 'u')
            {
                var parts = f.Split(' ', 11);
                changes.Add(new GitFileChange(parts[^1], null, GitChangeKind.Conflict, false, true));
            }
            else if (f[0] == '?') changes.Add(new GitFileChange(f[2..], null, GitChangeKind.Untracked, false, true));
        }
        return new GitStatus(branch, detached, oid, upstream, ahead, behind, changes, operation);
    }

    private static GitChangeKind KindOf(string xy, bool renamedOrCopied)
    {
        var c = xy[0] != '.' ? xy[0] : xy[1];
        if (renamedOrCopied) return c == 'C' ? GitChangeKind.Copied : GitChangeKind.Renamed;
        return c switch
        {
            'A' => GitChangeKind.Added,
            'D' => GitChangeKind.Deleted,
            'T' => GitChangeKind.TypeChanged,
            _ => GitChangeKind.Modified,
        };
    }

    public async Task<IReadOnlyList<GitCommitInfo>> LogAsync(string repoPath, int max, string? revision = null, string? path = null, CancellationToken ct = default)
    {
        var args = new List<string> { "log", $"-n{Math.Clamp(max, 1, 5000)}", $"--format=%H{Us}%h{Us}%an{Us}%ae{Us}%aI{Us}%s{Us}%D{Rs}" };
        if (revision is not null)
        {
            if (revision.StartsWith('-')) throw new ArgumentException("Revisão inválida.", nameof(revision));
            args.Add(revision);
        }
        if (path is not null) { args.Add("--"); args.Add(path); }
        string raw;
        try { raw = await RequireAsync(repoPath, args, ct); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ainda não tem commits", StringComparison.Ordinal)) { return []; }
        return ParseLog(raw);
    }

    public static List<GitCommitInfo> ParseLog(string raw) =>
        raw.Split(Rs, StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim('\n', '\r'))
            .Where(r => r.Length > 0)
            .Select(r => r.Split(Us))
            .Where(p => p.Length >= 7)
            .Select(p => new GitCommitInfo(p[0], p[1], p[2], p[3],
                DateTimeOffset.Parse(p[4], CultureInfo.InvariantCulture), p[5], p[6]))
            .ToList();

    public async Task<IReadOnlyList<GitBranchInfo>> BranchesAsync(string repoPath, CancellationToken ct = default)
    {
        var raw = await RequireAsync(repoPath,
        [
            "for-each-ref", $"--format=%(refname){Us}%(refname:short){Us}%(HEAD){Us}%(upstream:short){Us}%(upstream:track){Us}%(objectname:short){Us}%(committerdate:iso-strict){Us}%(contents:subject){Rs}",
            "refs/heads", "refs/remotes",
        ], ct);
        return ParseBranches(raw);
    }

    public static List<GitBranchInfo> ParseBranches(string raw)
    {
        var list = new List<GitBranchInfo>();
        foreach (var rec in raw.Split(Rs, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = rec.Trim('\n', '\r').Split(Us);
            if (p.Length < 8) continue;
            var isRemote = p[0].StartsWith("refs/remotes/", StringComparison.Ordinal);
            if (isRemote && p[0].EndsWith("/HEAD", StringComparison.Ordinal)) continue;
            int ahead = 0, behind = 0;
            var track = p[4];
            var gone = track.Contains("gone", StringComparison.Ordinal);
            foreach (var part in track.Trim('[', ']').Split(',', StringSplitOptions.TrimEntries))
            {
                if (part.StartsWith("ahead ", StringComparison.Ordinal)) ahead = int.Parse(part[6..], CultureInfo.InvariantCulture);
                if (part.StartsWith("behind ", StringComparison.Ordinal)) behind = int.Parse(part[7..], CultureInfo.InvariantCulture);
            }
            DateTimeOffset? date = DateTimeOffset.TryParse(p[6], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            list.Add(new GitBranchInfo(p[1], p[2] == "*", isRemote, p[3].Length == 0 ? null : p[3], ahead, behind, gone, p[5], date, p[7]));
        }
        return list;
    }

    public async Task<IReadOnlyList<GitRemote>> RemotesAsync(string repoPath, CancellationToken ct = default)
    {
        var raw = await RequireAsync(repoPath, ["remote", "-v"], ct);
        var map = new Dictionary<string, (string? Fetch, string? Push)>();
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) continue;
            map.TryGetValue(p[0], out var cur);
            map[p[0]] = p[2] == "(push)" ? (cur.Fetch, p[1]) : (p[1], cur.Push);
        }
        return map.Select(kv => new GitRemote(kv.Key, kv.Value.Fetch ?? "", kv.Value.Push == kv.Value.Fetch ? null : kv.Value.Push)).ToList();
    }

    /// <summary>Traduz as falhas mais comuns do Git para linguagem simples. Retorna null quando não reconhece.</summary>
    public static string? Friendly(string stderr)
    {
        var s = stderr;
        bool Has(string x) => s.Contains(x, StringComparison.OrdinalIgnoreCase);
        if (Has("not a git repository")) return "Esta pasta não é um repositório Git.";
        if (Has("does not have any commits yet") || Has("bad default revision 'HEAD'") || Has("ambiguous argument 'HEAD'"))
            return "O repositório ainda não tem commits.";
        if (Has("would be overwritten by checkout") || Has("would be overwritten by merge") || Has("Please commit your changes or stash them"))
            return "Há alterações não salvas (sem commit) que seriam perdidas. Faça um commit ou guarde-as (stash) antes.";
        if (Has("CONFLICT")) return "Há conflitos: o mesmo trecho foi alterado dos dois lados. Resolva os arquivos marcados e conclua a operação.";
        if (Has("non-fast-forward") || Has("[rejected]") || Has("fetch first"))
            return "O remoto tem commits que você ainda não tem. Receba (pull) antes de enviar. O REA nunca força o envio.";
        if (Has("Not possible to fast-forward") || Has("diverging branches"))
            return "Seu branch e o remoto divergiram. É preciso fazer merge ou rebase (escolha em Branch Manager).";
        if (Has("Authentication failed") || Has("could not read Username") || Has("terminal prompts disabled") || Has("Permission denied (publickey)"))
            return "O Git não conseguiu se autenticar no remoto. Confira a conta em Git › Accounts (login do GitHub CLI).";
        if (Has("Repository not found")) return "Repositório remoto não encontrado ou sem permissão de acesso.";
        if (Has("Could not resolve host") || Has("unable to access")) return "Sem conexão com o servidor remoto.";
        if (Has("already exists")) return "Já existe um item com esse nome.";
        if (Has("is not fully merged")) return "Este branch tem commits que não estão em nenhum outro branch. Excluir agora perderia esse trabalho.";
        if (Has("nothing to commit")) return "Não há nada para registrar: nenhuma alteração selecionada.";
        if (Has("Please tell me who you are") || Has("empty ident name"))
            return "O Git não sabe seu nome/e-mail. Configure em Git Settings (user.name e user.email).";
        if (Has("pathspec") && Has("did not match")) return "Arquivo ou branch não encontrado.";
        if (Has("invalid reference") || Has("not a valid object name") || Has("unknown revision")) return "Branch, tag ou commit não encontrado.";
        if (Has("index.lock")) return "Outro programa Git está usando este repositório (index.lock). Feche-o e tente de novo.";
        return null;
    }
}
