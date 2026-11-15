using REA.Domain;

namespace REA.Core;

public sealed record DashboardSnapshot(
    int ActiveProjects,
    int ArchivedProjects,
    int Environments,
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
    IActivityLog activity,
    IToolDetector tools)
{
    public async Task<DashboardSnapshot> GetAsync(CancellationToken ct = default)
    {
        var active = await projects.CountAsync(ProjectStatus.Active, ct);
        var archived = await projects.CountAsync(ProjectStatus.Archived, ct);
        var envs = await environments.CountAsync(null, ct);
        var recent = await activity.RecentAsync(15, null, ct);
        var detected = await tools.DetectAsync(ct);
        var items = NavigationCatalog.AllItems.ToList();
        return new DashboardSnapshot(active, archived, envs, recent,
            items.Count(i => i.Implemented), items.Count, detected);
    }
}
