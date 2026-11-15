using Microsoft.EntityFrameworkCore;
using REA.Core;
using REA.Domain;

namespace REA.Infrastructure;

internal sealed class UnitOfWork(ReaDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct = default) => await db.SaveChangesAsync(ct);
}

internal sealed class ProjectRepository(ReaDbContext db) : IProjectRepository
{
    public async Task<PagedResult<ProjectSummary>> ListAsync(ProjectQuery query, CancellationToken ct = default)
    {
        var q = db.Projects.AsNoTracking();
        if (query.Status is { } status) q = q.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            q = q.Where(p => EF.Functions.Like(p.Name, pattern, "\\") || EF.Functions.Like(p.GameName!, pattern, "\\"));
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(p => p.UpdatedAt)
            .Skip(query.Page.Skip).Take(query.Page.Size)
            .Select(p => new ProjectSummary(p.Id, p.Name, p.GameName, p.Status, p.Environments.Count, p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResult<ProjectSummary>(items, total, query.Page);
    }

    public Task<ResearchProject?> GetAsync(Guid id, bool includeEnvironments, CancellationToken ct = default)
    {
        IQueryable<ResearchProject> q = db.Projects;
        if (includeEnvironments) q = q.Include(p => p.Environments);
        return q.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public Task<bool> NameExistsAsync(string normalizedName, Guid? exceptId, CancellationToken ct = default) =>
        db.Projects.AnyAsync(p => p.NormalizedName == normalizedName && (exceptId == null || p.Id != exceptId), ct);

    public Task<int> CountAsync(ProjectStatus? status, CancellationToken ct = default) =>
        status is { } s ? db.Projects.CountAsync(p => p.Status == s, ct) : db.Projects.CountAsync(ct);

    public void Add(ResearchProject project) => db.Projects.Add(project);
    public void Remove(ResearchProject project) => db.Projects.Remove(project);

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

internal sealed class EnvironmentRepository(ReaDbContext db) : IEnvironmentRepository
{
    public async Task<IReadOnlyList<ResearchEnvironment>> ListByProjectAsync(Guid projectId, CancellationToken ct = default) =>
        await db.Environments.AsNoTracking().Where(e => e.ProjectId == projectId).OrderBy(e => e.Name).ToListAsync(ct);

    public Task<ResearchEnvironment?> GetAsync(Guid projectId, Guid id, CancellationToken ct = default) =>
        db.Environments.FirstOrDefaultAsync(e => e.Id == id && e.ProjectId == projectId, ct);

    public Task<bool> NameExistsAsync(Guid projectId, string normalizedName, Guid? exceptId, CancellationToken ct = default) =>
        db.Environments.AnyAsync(e => e.ProjectId == projectId && e.NormalizedName == normalizedName
                                      && (exceptId == null || e.Id != exceptId), ct);

    public Task<int> CountAsync(Guid? projectId, CancellationToken ct = default) =>
        projectId is { } p ? db.Environments.CountAsync(e => e.ProjectId == p, ct) : db.Environments.CountAsync(ct);

    public void Add(ResearchEnvironment environment) => db.Environments.Add(environment);
    public void Remove(ResearchEnvironment environment) => db.Environments.Remove(environment);
}

internal sealed class ActivityLog(ReaDbContext db) : IActivityLog
{
    public void Record(ActivityEntry entry) => db.Activity.Add(entry);

    public async Task<IReadOnlyList<ActivityEntry>> RecentAsync(int take, Guid? projectId, CancellationToken ct = default)
    {
        var q = db.Activity.AsNoTracking();
        if (projectId is { } p) q = q.Where(a => a.ProjectId == p);
        return await q.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id).Take(take).ToListAsync(ct);
    }
}
