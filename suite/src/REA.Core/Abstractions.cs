using REA.Domain;

namespace REA.Core;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

/// <summary>Quem está operando a ferramenta (usuário do Windows), registrado na auditoria.</summary>
public interface IOperatorContext
{
    string OperatorName { get; }
}

public sealed class WindowsOperatorContext : IOperatorContext
{
    public string OperatorName => $"{Environment.UserDomainName}\\{Environment.UserName}";
}

/// <summary>Grava numa única transação tudo o que os repositórios e a auditoria registraram.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record ProjectQuery(string? Search, ProjectStatus? Status, PageRequest Page);

public sealed record ProjectSummary(
    Guid Id, string Name, string? GameName, ProjectStatus Status, int EnvironmentCount,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IProjectRepository
{
    Task<PagedResult<ProjectSummary>> ListAsync(ProjectQuery query, CancellationToken ct = default);
    Task<ResearchProject?> GetAsync(Guid id, bool includeEnvironments, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string normalizedName, Guid? exceptId, CancellationToken ct = default);
    Task<int> CountAsync(ProjectStatus? status, CancellationToken ct = default);
    void Add(ResearchProject project);
    void Remove(ResearchProject project);
}

public interface IEnvironmentRepository
{
    Task<IReadOnlyList<ResearchEnvironment>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    /// <summary>Busca sempre junto com o projeto: ambiente de outro projeto responde como não encontrado.</summary>
    Task<ResearchEnvironment?> GetAsync(Guid projectId, Guid id, CancellationToken ct = default);
    Task<bool> NameExistsAsync(Guid projectId, string normalizedName, Guid? exceptId, CancellationToken ct = default);
    Task<int> CountAsync(Guid? projectId, CancellationToken ct = default);
    void Add(ResearchEnvironment environment);
    void Remove(ResearchEnvironment environment);
}

public interface IActivityLog
{
    void Record(ActivityEntry entry);
    Task<IReadOnlyList<ActivityEntry>> RecentAsync(int take, Guid? projectId, CancellationToken ct = default);
}

public sealed record BackupInfo(string FileName, string FullPath, DateTimeOffset CreatedAt, long SizeBytes, string Reason);

public interface IBackupService
{
    Task<BackupInfo> CreateAsync(string reason, CancellationToken ct = default);
    Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken ct = default);
    /// <summary>Cria backup automático se o último tiver mais de 24 h. Retorna null se não precisou.</summary>
    Task<BackupInfo?> EnsureRecentAsync(CancellationToken ct = default);
    /// <summary>Faz backup do estado atual e depois restaura o arquivo indicado.</summary>
    Task<OperationResult> RestoreAsync(string fileName, CancellationToken ct = default);
}

public sealed record DatabaseInfo(string Path, long SizeBytes, IReadOnlyList<string> AppliedMigrations, IReadOnlyList<string> PendingMigrations);

public interface IDatabaseInfoProvider
{
    Task<DatabaseInfo> GetAsync(CancellationToken ct = default);
}
