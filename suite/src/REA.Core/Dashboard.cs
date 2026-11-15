using REA.Domain;

namespace REA.Core;

public sealed record DashboardSnapshot(
    int ActiveProjects,
    int ArchivedProjects,
    int Environments,
    int Captures,
    long PacketsAnalyzed,
    int TcpStreams,
    IReadOnlyList<ActivityEntry> RecentActivity,
    int ImplementedModules,
    int TotalModules,
    IReadOnlyList<ToolStatus> Tools);

public sealed record ToolStatus(string Name, bool Available, string? Version, string? Location, string Requirement);

public interface IToolDetector
{
    Task<IReadOnlyList<ToolStatus>> DetectAsync(CancellationToken ct = default);
}

public sealed class DashboardService(
    IProjectRepository projects,
    IEnvironmentRepository environments,
    ICaptureRepository captures,
    IActivityLog activity,
    IToolDetector tools)
{
    /// <param name="includeTools">false = só contagens do banco (rápido); a detecção de ferramentas executa processos externos.</param>
    public async Task<DashboardSnapshot> GetAsync(CancellationToken ct = default, bool includeTools = true)
    {
        var active = await projects.CountAsync(ProjectStatus.Active, ct);
        var archived = await projects.CountAsync(ProjectStatus.Archived, ct);
        var envs = await environments.CountAsync(null, ct);
        var captureCount = await captures.CountAsync(ct);
        var packets = await captures.TotalPacketsAsync(ct);
        var streams = await captures.TotalTcpStreamsAsync(ct);
        var recent = await activity.RecentAsync(15, null, ct);
        var detected = includeTools ? await tools.DetectAsync(ct) : [];
        var items = NavigationCatalog.AllItems.ToList();
        return new DashboardSnapshot(active, archived, envs, captureCount, packets, streams, recent,
            items.Count(i => i.Implemented), items.Count, detected);
    }
}
