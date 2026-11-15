using System.Text;
using System.Text.RegularExpressions;
using REA.Domain;

namespace REA.Core;

/// <summary>
/// Operações Git do REA. É o mesmo serviço para a tela manual e para o Claude: as proteções valem para os dois.
/// Regras fixas: nunca push forçado; pull só com avanço rápido; exclusão de branch não mesclado, commit com possível
/// segredo e operações que descartam trabalho exigem confirmação explícita do chamador. Tudo é auditado.
/// </summary>
public sealed partial class GitService(
    IGitClient git,
    IGitHubClient github,
    IGitRepositoryRepository repos,
    IProjectRepository projects,
    IActivityLog activity,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOperatorContext operatorContext)
{
    public const int MaxDiffChars = 400_000;

    public string? GitPath => git.Locate();
    public string? GitHubCliPath => github.Locate();

    // ------------------------------------------------------------------ repositórios

    public Task<IReadOnlyList<GitRepositorySummary>> ListAsync(Guid? projectId, CancellationToken ct = default) => repos.ListAsync(projectId, ct);
    public Task<GitRepositoryLink?> GetAsync(Guid id, CancellationToken ct = default) => repos.GetAsync(id, ct);

    /// <summary>Liga uma pasta que já é repositório Git a um projeto.</summary>
    public async Task<OperationResult<Guid>> LinkAsync(Guid projectId, string folder, string? name, CancellationToken ct = default)
    {
        if (await ValidateProjectAsync(projectId, ct) is { } err) return OperationResult<Guid>.Fail(err);
        var root = await git.FindRootAsync(folder, ct);
        if (root is null) return OperationResult<Guid>.Fail("Esta pasta não é um repositório Git. Use Iniciar repositório para criar um.");
        return await AddLinkAsync(projectId, root, name, "Vincular repositório", ct);
    }

    /// <summary>Cria um repositório novo (git init) numa pasta e liga ao projeto.</summary>
    public async Task<OperationResult<Guid>> InitAsync(Guid projectId, string folder, string? name, string defaultBranch = "main", CancellationToken ct = default)
    {
        if (await ValidateProjectAsync(projectId, ct) is { } err) return OperationResult<Guid>.Fail(err);
        if (!await IsValidBranchNameAsync(defaultBranch, ct)) return OperationResult<Guid>.Fail("Nome de branch inválido.");
        Directory.CreateDirectory(folder);
        if (await git.FindRootAsync(folder, ct) is { } existing && PathEquals(existing, folder))
            return OperationResult<Guid>.Fail("Esta pasta já é um repositório Git. Use Vincular repositório.");
        var r = await git.RunAsync(folder, ["init", "-b", defaultBranch], ct);
        if (!r.Success) return OperationResult<Guid>.Fail(r.Message);
        return await AddLinkAsync(projectId, Path.GetFullPath(folder), name, "Iniciar repositório", ct, defaultBranch);
    }

    /// <summary>Clona um remoto para uma pasta nova (ou vazia) e liga ao projeto.</summary>
    public async Task<OperationResult<Guid>> CloneAsync(Guid projectId, string url, string targetFolder, string? name, CancellationToken ct = default)
    {
        if (await ValidateProjectAsync(projectId, ct) is { } err) return OperationResult<Guid>.Fail(err);
        if (!IsAllowedRemoteUrl(url)) return OperationResult<Guid>.Fail("Endereço inválido. Use https://…, git@host:dono/repo.git ou file:///pasta.");
        if (Directory.Exists(targetFolder) && Directory.EnumerateFileSystemEntries(targetFolder).Any())
            return OperationResult<Guid>.Fail("A pasta de destino já existe e não está vazia.");
        var parent = Path.GetDirectoryName(Path.GetFullPath(targetFolder))!;
        Directory.CreateDirectory(parent);
        var r = await git.RunAsync(parent, ["clone", "--", url.Trim(), Path.GetFullPath(targetFolder)], ct);
        if (!r.Success) return OperationResult<Guid>.Fail(r.Message);
        return await AddLinkAsync(projectId, Path.GetFullPath(targetFolder), name, "Clonar repositório", ct);
    }

    /// <summary>Desfaz só o vínculo com o projeto. Nenhum arquivo é apagado.</summary>
    public async Task<OperationResult> UnlinkAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        repos.Remove(link);
        Audit("Desvincular repositório", link, $"Repositório \"{link.Name}\" desvinculado do projeto. A pasta {link.LocalPath} não foi alterada.");
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    private async Task<OperationResult<Guid>> AddLinkAsync(Guid projectId, string root, string? name, string action, CancellationToken ct, string? defaultBranch = null)
    {
        if (await repos.FindByPathAsync(projectId, root, ct) is { } dup) return OperationResult<Guid>.Ok(dup.Id);
        var remotes = await git.RemotesAsync(root, ct);
        var status = await git.StatusAsync(root, ct);
        var now = clock.Now;
        var link = new GitRepositoryLink
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(root.TrimEnd('\\', '/')) : name.Trim()[..Math.Min(name.Trim().Length, 120)],
            LocalPath = root,
            RemoteUrl = remotes.FirstOrDefault(r => r.Name == "origin")?.FetchUrl ?? remotes.FirstOrDefault()?.FetchUrl,
            DefaultBranch = defaultBranch ?? status.Branch ?? "main",
            CreatedAt = now,
            UpdatedAt = now,
        };
        repos.Add(link);
        Audit(action, link, $"{action}: \"{link.Name}\" em {root}" + (link.RemoteUrl is null ? "." : $" (remoto {RedactUrl(link.RemoteUrl)})."));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult<Guid>.Ok(link.Id);
    }

    /// <summary>Cria o repositório no GitHub (conta do gh) e configura como "origin". Não envia nada sozinho.</summary>
    public async Task<OperationResult<GitHubRepoInfo>> CreateGitHubRepoAsync(Guid id, string name, bool isPrivate, string? owner, string? description, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult<GitHubRepoInfo>.Fail("Repositório não encontrado.");
        var remotes = await git.RemotesAsync(link.LocalPath, ct);
        if (remotes.Any(r => r.Name == "origin")) return OperationResult<GitHubRepoInfo>.Fail("Este repositório já tem um remoto \"origin\".");
        OperationResult<GitHubRepoInfo> created;
        try { created = await github.CreateRepoAsync(name, isPrivate, owner, description, ct); }
        catch (ArgumentException ex) { return OperationResult<GitHubRepoInfo>.Fail(ex.Message); }
        if (!created.Success) return created;
        var url = created.Value!.Url + ".git";
        var r = await git.RunAsync(link.LocalPath, ["remote", "add", "origin", url], ct);
        if (!r.Success) return OperationResult<GitHubRepoInfo>.Fail($"Repositório criado no GitHub, mas o remoto não foi configurado: {r.Message}");
        link.RemoteUrl = url;
        link.UpdatedAt = clock.Now;
        Audit("Criar repositório no GitHub", link, $"Repositório {(isPrivate ? "privado" : "público")} {created.Value.NameWithOwner} criado e configurado como origin.");
        await unitOfWork.SaveChangesAsync(ct);
        return created;
    }

    // ------------------------------------------------------------------ leitura

    public async Task<OperationResult<GitStatus>> StatusAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult<GitStatus>.Fail("Repositório não encontrado.");
        if (!Directory.Exists(link.LocalPath)) return OperationResult<GitStatus>.Fail($"A pasta {link.LocalPath} não existe mais.");
        try { return OperationResult<GitStatus>.Ok(await git.StatusAsync(link.LocalPath, ct)); }
        catch (InvalidOperationException ex) { return OperationResult<GitStatus>.Fail(ex.Message); }
    }

    public async Task<OperationResult<IReadOnlyList<GitCommitInfo>>> LogAsync(Guid id, int max = 200, string? revision = null, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult<IReadOnlyList<GitCommitInfo>>.Fail("Repositório não encontrado.");
        try { return OperationResult<IReadOnlyList<GitCommitInfo>>.Ok(await git.LogAsync(link.LocalPath, max, revision, ct: ct)); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return OperationResult<IReadOnlyList<GitCommitInfo>>.Fail(ex.Message); }
    }

    public async Task<OperationResult<IReadOnlyList<GitBranchInfo>>> BranchesAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult<IReadOnlyList<GitBranchInfo>>.Fail("Repositório não encontrado.");
        try { return OperationResult<IReadOnlyList<GitBranchInfo>>.Ok(await git.BranchesAsync(link.LocalPath, ct)); }
        catch (InvalidOperationException ex) { return OperationResult<IReadOnlyList<GitBranchInfo>>.Fail(ex.Message); }
    }

    /// <summary>Diferenças de um arquivo. Arquivo novo (não rastreado) mostra o conteúdo como adição.</summary>
    public async Task<OperationResult<string>> DiffAsync(Guid id, GitFileChange change, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult<string>.Fail("Repositório não encontrado.");
        if (change.Kind == GitChangeKind.Untracked)
        {
            var full = SafeJoin(link.LocalPath, change.Path);
            if (full is null || !File.Exists(full)) return OperationResult<string>.Fail("Arquivo não encontrado.");
            var bytes = await File.ReadAllBytesAsync(full, ct);
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).IndexOf((byte)0) >= 0)
                return OperationResult<string>.Ok($"(arquivo binário novo, {bytes.Length} bytes)");
            var text = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, MaxDiffChars));
            return OperationResult<string>.Ok($"Arquivo novo: {change.Path}\n" + string.Join('\n', text.Split('\n').Select(l => "+" + l)));
        }
        var args = new List<string> { "diff", "--no-ext-diff" };
        if (change.Staged && !change.Unstaged) args.Add("--cached");
        else if (change.Staged && change.Unstaged) args.Add("HEAD");
        args.Add("--");
        if (change.OldPath is not null) args.Add(change.OldPath);
        args.Add(change.Path);
        var r = await git.RunAsync(link.LocalPath, args, ct);
        if (!r.Success) return OperationResult<string>.Fail(r.Message);
        var output = r.Output.Length > MaxDiffChars ? r.Output[..MaxDiffChars] + "\n… diferença muito grande; exibição cortada." : r.Output;
        return OperationResult<string>.Ok(output.Length == 0 ? "(sem diferença de conteúdo; só permissões ou fim de linha)" : output);
    }

    /// <summary>Detalhes de um commit (autor, mensagem, arquivos e diferenças), limitado em tamanho.</summary>
    public async Task<OperationResult<string>> ShowAsync(Guid id, string hash, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult<string>.Fail("Repositório não encontrado.");
        if (hash.Length is < 4 or > 64 || !hash.All(Uri.IsHexDigit)) return OperationResult<string>.Fail("Commit inválido.");
        var r = await git.RunAsync(link.LocalPath, ["show", "--stat", "--patch", "--no-ext-diff", "--format=fuller", hash], ct);
        if (!r.Success) return OperationResult<string>.Fail(r.Message);
        return OperationResult<string>.Ok(r.Output.Length > MaxDiffChars ? r.Output[..MaxDiffChars] + "\n… exibição cortada." : r.Output);
    }

    /// <summary>Commits locais ainda não enviados e commits do remoto ainda não recebidos (desde o último fetch).</summary>
    public async Task<OperationResult<(IReadOnlyList<GitCommitInfo> ToSend, IReadOnlyList<GitCommitInfo> ToReceive)>> PendingAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult<(IReadOnlyList<GitCommitInfo>, IReadOnlyList<GitCommitInfo>)>.Fail("Repositório não encontrado.");
        var st = await git.StatusAsync(link.LocalPath, ct);
        if (st.Upstream is null || st.HeadCommit is null)
            return OperationResult<(IReadOnlyList<GitCommitInfo>, IReadOnlyList<GitCommitInfo>)>.Ok(([], []));
        var send = await git.LogAsync(link.LocalPath, 500, "@{u}..HEAD", ct: ct);
        var receive = await git.LogAsync(link.LocalPath, 500, "HEAD..@{u}", ct: ct);
        return OperationResult<(IReadOnlyList<GitCommitInfo>, IReadOnlyList<GitCommitInfo>)>.Ok((send, receive));
    }

    // ------------------------------------------------------------------ commit

    public async Task<OperationResult> StageAsync(Guid id, IReadOnlyList<string> paths, CancellationToken ct = default) =>
        await PathOpAsync(id, paths, ["add", "--"], "Selecionar para commit", ct);

    public async Task<OperationResult> UnstageAsync(Guid id, IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        var st = await git.StatusAsync(link.LocalPath, ct);
        // Sem nenhum commit ainda não existe HEAD para restaurar: remove só do índice.
        string[] cmd = st.HeadCommit is null ? ["rm", "--cached", "-q", "--"] : ["restore", "--staged", "--"];
        return await PathOpAsync(id, paths, cmd, "Tirar da seleção", ct);
    }

    private async Task<OperationResult> PathOpAsync(Guid id, IReadOnlyList<string> paths, string[] cmd, string action, CancellationToken ct)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        if (paths.Count == 0) return OperationResult.Fail("Nenhum arquivo selecionado.");
        if (paths.Any(p => SafeJoin(link.LocalPath, p) is null)) return OperationResult.Fail("Caminho fora do repositório.");
        var r = await git.RunAsync(link.LocalPath, [.. cmd, .. paths], ct);
        if (!r.Success) return OperationResult.Fail(r.Message);
        Audit(action, link, $"{action}: {paths.Count} arquivo(s) em \"{link.Name}\" ({string.Join(", ", paths.Take(5))}{(paths.Count > 5 ? "…" : "")}).");
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    /// <summary>Procura credenciais e arquivos sensíveis nas alterações selecionadas para o commit.</summary>
    public async Task<OperationResult<IReadOnlyList<SecretFinding>>> ScanStagedAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult<IReadOnlyList<SecretFinding>>.Fail("Repositório não encontrado.");
        var names = await git.RunAsync(link.LocalPath, ["diff", "--cached", "--name-only", "--diff-filter=ACMR"], ct);
        var diff = await git.RunAsync(link.LocalPath, ["diff", "--cached", "--no-ext-diff", "-U0", "--diff-filter=ACMR"], ct);
        if (!names.Success) return OperationResult<IReadOnlyList<SecretFinding>>.Fail(names.Message);
        return OperationResult<IReadOnlyList<SecretFinding>>.Ok(SecretScanner.Scan(
            names.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), diff.Output));
    }

    /// <summary>
    /// Registra o commit. Se a verificação encontrar possíveis segredos, recusa — a menos que o usuário tenha visto
    /// os achados e confirmado (<paramref name="confirmedFindings"/>).
    /// </summary>
    public async Task<OperationResult<string>> CommitAsync(Guid id, string message, bool confirmedFindings = false, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult<string>.Fail("Repositório não encontrado.");
        message = message.Trim();
        if (message.Length == 0) return OperationResult<string>.Fail("Escreva uma mensagem descrevendo o que mudou.");
        var st = await git.StatusAsync(link.LocalPath, ct);
        if (st.HasConflicts) return OperationResult<string>.Fail("Há conflitos não resolvidos. Resolva-os antes do commit.");
        if (!st.Changes.Any(c => c.Staged)) return OperationResult<string>.Fail("Nenhum arquivo selecionado para o commit.");
        var scan = await ScanStagedAsync(id, ct);
        if (!scan.Success) return OperationResult<string>.Fail(scan.Error!);
        if (scan.Value!.Count > 0 && !confirmedFindings)
            return OperationResult<string>.Fail($"Possíveis segredos encontrados ({scan.Value.Count}). Revise antes de continuar: " +
                string.Join("; ", scan.Value.Take(5).Select(f => $"{f.Path}{(f.Line is { } l ? $":{l}" : "")} — {f.Rule}")));
        var r = await git.RunAsync(link.LocalPath, ["commit", "-m", message], ct);
        if (!r.Success) return OperationResult<string>.Fail(r.Message);
        var head = (await git.LogAsync(link.LocalPath, 1, ct: ct)).FirstOrDefault();
        Audit("Commit", link, $"Commit {head?.ShortHash} em \"{link.Name}\" ({st.Branch ?? "sem branch"}): {FirstLine(message)}" +
                              (scan.Value.Count > 0 ? $" — confirmado apesar de {scan.Value.Count} alerta(s) de segredo." : ""));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult<string>.Ok(head?.Hash ?? "");
    }

    // ------------------------------------------------------------------ branches

    public async Task<OperationResult> CreateBranchAsync(Guid id, string name, bool switchTo, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        if (!await IsValidBranchNameAsync(name, ct)) return OperationResult.Fail("Nome de branch inválido (sem espaços, sem ~ ^ : ? * [ e sem começar com '-').");
        var r = await git.RunAsync(link.LocalPath, switchTo ? ["switch", "-c", name] : ["branch", name], ct);
        return await AuditedAsync(link, r, "Criar branch", $"Branch \"{name}\" criado em \"{link.Name}\"{(switchTo ? " e ativado" : "")}.", ct);
    }

    /// <summary>Troca de branch. Alterações sem commit vão junto; se conflitarem, o Git recusa e nada é perdido.</summary>
    public async Task<OperationResult> SwitchBranchAsync(Guid id, string name, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        if (!await IsValidBranchNameAsync(name, ct)) return OperationResult.Fail("Nome de branch inválido.");
        var r = await git.RunAsync(link.LocalPath, ["switch", name], ct);
        return await AuditedAsync(link, r, "Trocar branch", $"Branch ativo de \"{link.Name}\" agora é \"{name}\".", ct);
    }

    public async Task<OperationResult> RenameBranchAsync(Guid id, string oldName, string newName, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        if (!await IsValidBranchNameAsync(oldName, ct) || !await IsValidBranchNameAsync(newName, ct)) return OperationResult.Fail("Nome de branch inválido.");
        var r = await git.RunAsync(link.LocalPath, ["branch", "-m", oldName, newName], ct);
        return await AuditedAsync(link, r, "Renomear branch", $"Branch \"{oldName}\" renomeado para \"{newName}\" em \"{link.Name}\".", ct);
    }

    /// <summary>
    /// Exclui um branch local. Sem <paramref name="confirmUnmerged"/>, o Git recusa se o branch tiver commits que não
    /// estão em outro lugar (proteção contra perda de trabalho).
    /// </summary>
    public async Task<OperationResult> DeleteBranchAsync(Guid id, string name, bool confirmUnmerged, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        if (!await IsValidBranchNameAsync(name, ct)) return OperationResult.Fail("Nome de branch inválido.");
        var st = await git.StatusAsync(link.LocalPath, ct);
        if (st.Branch == name) return OperationResult.Fail("Não é possível excluir o branch ativo. Troque de branch antes.");
        var r = await git.RunAsync(link.LocalPath, ["branch", confirmUnmerged ? "-D" : "-d", name], ct);
        return await AuditedAsync(link, r, "Excluir branch",
            $"Branch \"{name}\" excluído de \"{link.Name}\"{(confirmUnmerged ? " (confirmado pelo usuário mesmo sem merge)" : "")}.", ct);
    }

    /// <summary>Merge de outro branch no ativo. Em conflito, a operação fica pendente para resolução (nada é descartado).</summary>
    public async Task<OperationResult> MergeAsync(Guid id, string branch, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        if (!await IsValidBranchNameAsync(branch, ct)) return OperationResult.Fail("Nome de branch inválido.");
        var st = await git.StatusAsync(link.LocalPath, ct);
        if (st.HasUncommittedChanges) return OperationResult.Fail("Faça commit (ou stash) das alterações antes do merge.");
        var r = await git.RunAsync(link.LocalPath, ["merge", "--no-edit", branch], ct);
        return await AuditedAsync(link, r, "Merge", $"Merge de \"{branch}\" em \"{st.Branch}\" ({link.Name}).", ct);
    }

    public async Task<OperationResult> AbortMergeAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        var r = await git.RunAsync(link.LocalPath, ["merge", "--abort"], ct);
        return await AuditedAsync(link, r, "Cancelar merge", $"Merge cancelado em \"{link.Name}\"; arquivos voltaram ao estado anterior.", ct);
    }

    // ------------------------------------------------------------------ sincronização

    public async Task<OperationResult> FetchAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        var r = await git.RunAsync(link.LocalPath, ["fetch", "--all", "--prune"], ct);
        return await AuditedAsync(link, r, "Buscar do remoto", $"Fetch em \"{link.Name}\".", ct);
    }

    /// <summary>Pull só com avanço rápido: se houver divergência, recusa e explica (sem merge automático escondido).</summary>
    public async Task<OperationResult> PullAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        var r = await git.RunAsync(link.LocalPath, ["pull", "--ff-only"], ct);
        return await AuditedAsync(link, r, "Receber do remoto", $"Pull (avanço rápido) em \"{link.Name}\".", ct);
    }

    /// <summary>Envia o branch ativo. Nunca força. Sem upstream, configura origin/branch.</summary>
    public async Task<OperationResult> PushAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        var st = await git.StatusAsync(link.LocalPath, ct);
        if (st.Branch is null) return OperationResult.Fail("Nenhum branch ativo (HEAD destacado). Troque para um branch antes de enviar.");
        if (st.HeadCommit is null) return OperationResult.Fail("Ainda não há commits para enviar.");
        var remotes = await git.RemotesAsync(link.LocalPath, ct);
        if (remotes.Count == 0) return OperationResult.Fail("Este repositório não tem remoto. Crie um no GitHub ou adicione um remoto.");
        string[] args = st.Upstream is null ? ["push", "-u", remotes.Any(r => r.Name == "origin") ? "origin" : remotes[0].Name, st.Branch] : ["push"];
        var r = await git.RunAsync(link.LocalPath, args, ct);
        return await AuditedAsync(link, r, "Enviar para o remoto",
            $"Push de \"{st.Branch}\" em \"{link.Name}\" ({(st.Upstream is null ? "upstream configurado agora" : $"{st.Ahead} commit(s)")}).", ct);
    }

    // ------------------------------------------------------------------ stash

    public async Task<OperationResult> StashAsync(Guid id, string? message, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        var args = new List<string> { "stash", "push", "--include-untracked" };
        if (!string.IsNullOrWhiteSpace(message)) { args.Add("-m"); args.Add(message.Trim()); }
        var r = await git.RunAsync(link.LocalPath, args, ct);
        return await AuditedAsync(link, r, "Guardar alterações (stash)", $"Alterações guardadas em stash em \"{link.Name}\".", ct);
    }

    public async Task<OperationResult> StashPopAsync(Guid id, CancellationToken ct = default)
    {
        var link = await repos.GetAsync(id, ct);
        if (link is null) return OperationResult.Fail("Repositório não encontrado.");
        var r = await git.RunAsync(link.LocalPath, ["stash", "pop"], ct);
        return await AuditedAsync(link, r, "Recuperar alterações (stash)", $"Último stash reaplicado em \"{link.Name}\".", ct);
    }

    // ------------------------------------------------------------------ auxiliares

    public async Task<bool> IsValidBranchNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.Length > 200) return false;
        var r = await git.RunAsync(Environment.CurrentDirectory, ["check-ref-format", "--branch", name], ct);
        return r.Success;
    }

    public static bool IsAllowedRemoteUrl(string url)
    {
        var u = url.Trim();
        if (u.StartsWith('-') || u.Any(char.IsWhiteSpace)) return false;
        if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "ssh" or "file") return true;
        return ScpLikeUrl().IsMatch(u);
    }

    /// <summary>Remove usuário/senha embutidos numa URL antes de registrar em log.</summary>
    public static string RedactUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.UserInfo.Length > 0 ? url.Replace(u.UserInfo + "@", "***@", StringComparison.Ordinal) : url;

    private static string? SafeJoin(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return full.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string FirstLine(string s) { var i = s.IndexOf('\n'); var l = i < 0 ? s : s[..i]; return l.Length > 200 ? l[..200] + "…" : l; }

    private async Task<string?> ValidateProjectAsync(Guid projectId, CancellationToken ct)
    {
        var project = await projects.GetAsync(projectId, includeEnvironments: false, ct);
        if (project is null) return "Projeto não encontrado.";
        if (project.Status == ProjectStatus.Archived) return "O projeto está arquivado. Reative-o para vincular repositórios.";
        if (git.Locate() is null) return "O Git não está instalado (git-scm.com).";
        return null;
    }

    private async Task<OperationResult> AuditedAsync(GitRepositoryLink link, GitResult r, string action, string summary, CancellationToken ct)
    {
        Audit(r.Success ? action : $"{action} (falhou)", link, r.Success ? summary : $"{summary} Falhou: {r.Message}");
        await unitOfWork.SaveChangesAsync(ct);
        return r.Success ? OperationResult.Ok() : OperationResult.Fail(r.Message);
    }

    private void Audit(string action, GitRepositoryLink link, string summary) =>
        activity.Record(new ActivityEntry
        {
            Timestamp = clock.Now,
            Operator = operatorContext.OperatorName,
            Action = action,
            EntityType = "Git",
            EntityId = link.Id,
            ProjectId = link.ProjectId,
            Summary = summary.Length > 2000 ? summary[..2000] : summary,
        });

    [GeneratedRegex(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[A-Za-z0-9._/-]+$")]
    private static partial Regex ScpLikeUrl();
}

/// <summary>
/// Verificação de segredos em alterações a serem registradas. Regras explícitas (chaves e tokens com formato
/// conhecido, chaves privadas, arquivos de credenciais), para não inferir segredo por qualquer palavra.
/// </summary>
public static partial class SecretScanner
{
    private static readonly (string Rule, Regex Pattern)[] ContentRules =
    [
        ("Chave privada (PEM)", PrivateKey()),
        ("Token do GitHub", GitHubToken()),
        ("Chave de acesso AWS", AwsKey()),
        ("Chave de API da Anthropic", AnthropicKey()),
        ("Chave de API (sk-…)", OpenAiKey()),
        ("Token do Slack", SlackToken()),
        ("Chave de API do Google", GoogleKey()),
        ("Senha atribuída no código", PasswordAssignment()),
        ("Cadeia de conexão com senha", ConnectionStringPassword()),
    ];

    private static readonly string[] SensitiveNames = [".env", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", ".npmrc", ".pypirc", "credentials", ".git-credentials"];
    private static readonly string[] SensitiveExtensions = [".pem", ".key", ".pfx", ".p12", ".jks", ".keystore", ".ppk"];

    public static IReadOnlyList<SecretFinding> Scan(IEnumerable<string> stagedPaths, string unifiedDiffU0)
    {
        var findings = new List<SecretFinding>();
        foreach (var p in stagedPaths)
        {
            var name = Path.GetFileName(p);
            if (SensitiveNames.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".example", StringComparison.OrdinalIgnoreCase))
                findings.Add(new SecretFinding(p, null, "Arquivo de credenciais/configuração sensível", name));
            else if (SensitiveExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
                findings.Add(new SecretFinding(p, null, "Arquivo de chave ou certificado", name));
        }

        string? file = null;
        var line = 0;
        foreach (var raw in unifiedDiffU0.Split('\n'))
        {
            if (raw.StartsWith("+++ ", StringComparison.Ordinal)) { file = raw.StartsWith("+++ b/", StringComparison.Ordinal) ? raw[6..] : null; continue; }
            if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                var m = HunkHeader().Match(raw);
                line = m.Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                continue;
            }
            if (file is null || !raw.StartsWith('+') || raw.StartsWith("+++", StringComparison.Ordinal)) continue;
            var content = raw[1..];
            foreach (var (rule, pattern) in ContentRules)
            {
                var match = pattern.Match(content);
                if (match.Success) { findings.Add(new SecretFinding(file, line, rule, Mask(match.Value))); break; }
            }
            line++;
        }
        return findings;
    }

    /// <summary>Mostra só o começo do valor encontrado (o achado em si não pode virar outro vazamento).</summary>
    private static string Mask(string v) => v.Length <= 8 ? new string('*', v.Length) : v[..Math.Min(6, v.Length / 3)] + new string('*', 8);

    [GeneratedRegex(@"-----BEGIN (RSA |EC |DSA |OPENSSH |ENCRYPTED |PGP )?PRIVATE KEY( BLOCK)?-----")] private static partial Regex PrivateKey();
    [GeneratedRegex(@"\b(gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{60,})\b")] private static partial Regex GitHubToken();
    [GeneratedRegex(@"\b(AKIA|ASIA)[A-Z0-9]{16}\b")] private static partial Regex AwsKey();
    [GeneratedRegex(@"\bsk-ant-[A-Za-z0-9_-]{20,}")] private static partial Regex AnthropicKey();
    [GeneratedRegex(@"\bsk-(proj-)?[A-Za-z0-9]{32,}\b")] private static partial Regex OpenAiKey();
    [GeneratedRegex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}")] private static partial Regex SlackToken();
    [GeneratedRegex(@"\bAIza[0-9A-Za-z_-]{35}\b")] private static partial Regex GoogleKey();
    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|senha|secret|api[_-]?key|access[_-]?token)\b\s*[:=]\s*[""'][^""'\s]{6,}[""']")] private static partial Regex PasswordAssignment();
    [GeneratedRegex(@"(?i)\b(password|pwd)\s*=\s*[^;""'\s]{6,}\s*;")] private static partial Regex ConnectionStringPassword();
    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)")] private static partial Regex HunkHeader();
}
