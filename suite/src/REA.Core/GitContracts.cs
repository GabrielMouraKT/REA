using REA.Domain;

namespace REA.Core;

public enum GitChangeKind { Modified, Added, Deleted, Renamed, Copied, Untracked, Conflict, TypeChanged }

/// <param name="Staged">A alteração está no índice (vai no próximo commit).</param>
public sealed record GitFileChange(string Path, string? OldPath, GitChangeKind Kind, bool Staged, bool Unstaged);

/// <summary>Indicadores visuais pedidos no requisito (Clean, Modified, Untracked, Ahead, Behind, Diverged, Conflict, Synced).</summary>
[Flags]
public enum GitSyncState
{
    None = 0,
    Clean = 1,
    Modified = 2,
    Untracked = 4,
    Ahead = 8,
    Behind = 16,
    Diverged = 32,
    Conflict = 64,
    Synced = 128,
    NoUpstream = 256,
}

public sealed record GitStatus(
    string? Branch,
    bool IsDetached,
    string? HeadCommit,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<GitFileChange> Changes,
    string? OperationInProgress)
{
    public bool HasConflicts => Changes.Any(c => c.Kind == GitChangeKind.Conflict);
    public bool HasUncommittedChanges => Changes.Any(c => c.Kind != GitChangeKind.Untracked);

    public GitSyncState State
    {
        get
        {
            var s = GitSyncState.None;
            if (HasConflicts) s |= GitSyncState.Conflict;
            if (Changes.Any(c => c.Kind is not (GitChangeKind.Untracked or GitChangeKind.Conflict))) s |= GitSyncState.Modified;
            if (Changes.Any(c => c.Kind == GitChangeKind.Untracked)) s |= GitSyncState.Untracked;
            if (Changes.Count == 0) s |= GitSyncState.Clean;
            if (Upstream is null) s |= GitSyncState.NoUpstream;
            else if (Ahead > 0 && Behind > 0) s |= GitSyncState.Diverged;
            else if (Ahead > 0) s |= GitSyncState.Ahead;
            else if (Behind > 0) s |= GitSyncState.Behind;
            else s |= GitSyncState.Synced;
            return s;
        }
    }

    /// <summary>Estado em palavras (nunca só por cor).</summary>
    public string Describe()
    {
        var parts = new List<string>();
        var st = State;
        if (st.HasFlag(GitSyncState.Conflict)) parts.Add("Conflito");
        if (st.HasFlag(GitSyncState.Modified)) parts.Add("Alterado");
        if (st.HasFlag(GitSyncState.Untracked)) parts.Add("Arquivos novos");
        if (st.HasFlag(GitSyncState.Clean)) parts.Add("Limpo");
        if (st.HasFlag(GitSyncState.Diverged)) parts.Add($"Divergente ({Ahead} à frente, {Behind} atrás)");
        else if (st.HasFlag(GitSyncState.Ahead)) parts.Add($"{Ahead} commit(s) para enviar");
        else if (st.HasFlag(GitSyncState.Behind)) parts.Add($"{Behind} commit(s) para receber");
        else if (st.HasFlag(GitSyncState.Synced)) parts.Add("Sincronizado");
        if (st.HasFlag(GitSyncState.NoUpstream)) parts.Add("Sem remoto configurado");
        if (OperationInProgress is not null) parts.Add($"{OperationInProgress} em andamento");
        return string.Join(" · ", parts);
    }
}

public sealed record GitCommitInfo(string Hash, string ShortHash, string Author, string Email, DateTimeOffset Date, string Subject, string Refs);

public sealed record GitBranchInfo(
    string Name, bool IsCurrent, bool IsRemote, string? Upstream, int Ahead, int Behind, bool UpstreamGone,
    string LastCommit, DateTimeOffset? LastCommitDate, string LastSubject);

public sealed record GitRemote(string Name, string FetchUrl, string? PushUrl);

/// <summary>Resultado de um comando Git: saída bruta preservada e mensagem em linguagem simples quando falha.</summary>
public sealed record GitResult(bool Success, string Output, string? Error, string? FriendlyError, string Command)
{
    public string Message => Success ? Output : FriendlyError ?? Error ?? "Falha no comando Git.";
}

/// <summary>Achado da verificação de segredos antes do commit.</summary>
public sealed record SecretFinding(string Path, int? Line, string Rule, string Excerpt);

/// <summary>Acesso ao Git (CLI). Um só serviço para a interface manual e para o Claude.</summary>
public interface IGitClient
{
    string? Locate();
    Task<GitResult> RunAsync(string repoPath, IReadOnlyList<string> args, CancellationToken ct = default);
    Task<string?> FindRootAsync(string path, CancellationToken ct = default);
    Task<GitStatus> StatusAsync(string repoPath, CancellationToken ct = default);
    Task<IReadOnlyList<GitCommitInfo>> LogAsync(string repoPath, int max, string? revision = null, string? path = null, CancellationToken ct = default);
    Task<IReadOnlyList<GitBranchInfo>> BranchesAsync(string repoPath, CancellationToken ct = default);
    Task<IReadOnlyList<GitRemote>> RemotesAsync(string repoPath, CancellationToken ct = default);
}

/// <summary>Conta do GitHub vista pelo GitHub CLI (o REA não guarda token próprio).</summary>
public sealed record GitHubAccount(string Login, string? Name, string? Url, IReadOnlyList<string> Scopes, IReadOnlyList<string> Organizations);

public sealed record GitHubRepoInfo(
    string NameWithOwner, string? Description, string Url, string Visibility, bool IsArchived,
    DateTimeOffset? UpdatedAt, long? DiskUsageKb, string? DefaultBranch);

public interface IGitHubClient
{
    /// <summary>Null quando o GitHub CLI não está instalado.</summary>
    string? Locate();
    Task<OperationResult<GitHubAccount>> AccountAsync(CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<GitHubRepoInfo>>> ListReposAsync(string? owner, int limit, CancellationToken ct = default);
    Task<OperationResult<GitHubRepoInfo>> CreateRepoAsync(string name, bool isPrivate, string? owner, string? description, CancellationToken ct = default);
}

public sealed record GitRepositorySummary(
    Guid Id, Guid ProjectId, string ProjectName, string Name, string LocalPath, string? RemoteUrl, string DefaultBranch, DateTimeOffset CreatedAt);

public interface IGitRepositoryRepository
{
    Task<IReadOnlyList<GitRepositorySummary>> ListAsync(Guid? projectId, CancellationToken ct = default);
    Task<GitRepositoryLink?> GetAsync(Guid id, CancellationToken ct = default);
    Task<GitRepositoryLink?> FindByPathAsync(Guid projectId, string localPath, CancellationToken ct = default);
    Task<int> CountByProjectAsync(Guid projectId, CancellationToken ct = default);
    void Add(GitRepositoryLink link);
    void Remove(GitRepositoryLink link);
}
