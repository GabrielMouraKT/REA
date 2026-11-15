using Microsoft.EntityFrameworkCore;
using REA.Core;
using REA.Domain;

namespace REA.Infrastructure;

internal sealed class GitRepositoryRepository(ReaDbContext db) : IGitRepositoryRepository
{
    public async Task<IReadOnlyList<GitRepositorySummary>> ListAsync(Guid? projectId, CancellationToken ct = default)
    {
        var q = db.GitRepositories.AsNoTracking();
        if (projectId is { } p) q = q.Where(r => r.ProjectId == p);
        return await q.OrderBy(r => r.Name)
            .Select(r => new GitRepositorySummary(r.Id, r.ProjectId, r.Project!.Name, r.Name, r.LocalPath, r.RemoteUrl, r.DefaultBranch, r.CreatedAt))
            .ToListAsync(ct);
    }

    public Task<GitRepositoryLink?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.GitRepositories.Include(r => r.Project).FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<GitRepositoryLink?> FindByPathAsync(Guid projectId, string localPath, CancellationToken ct = default)
    {
        static string Norm(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
        var normalized = Norm(localPath);
        var candidates = await db.GitRepositories.Where(r => r.ProjectId == projectId).ToListAsync(ct);
        return candidates.FirstOrDefault(r => string.Equals(Norm(r.LocalPath), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken ct = default) =>
        db.GitRepositories.CountAsync(r => r.ProjectId == projectId, ct);

    public void Add(GitRepositoryLink link) => db.GitRepositories.Add(link);
    public void Remove(GitRepositoryLink link) => db.GitRepositories.Remove(link);
}
