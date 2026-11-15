using Microsoft.EntityFrameworkCore;
using REA.Core;
using REA.Domain;

namespace REA.Infrastructure;

internal sealed class AnalyzedFileRepository(ReaDbContext db) : IAnalyzedFileRepository
{
    public async Task<PagedResult<AnalyzedFileSummary>> ListAsync(AnalyzedFileQuery query, CancellationToken ct = default)
    {
        var q = db.AnalyzedFiles.AsNoTracking();
        if (query.ProjectId is { } p) q = q.Where(f => f.ProjectId == p);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{ProjectRepository.EscapeLike(query.Search.Trim())}%";
            q = q.Where(f => EF.Functions.Like(f.Name, pattern, "\\") || EF.Functions.Like(f.Sha256, pattern, "\\")
                             || EF.Functions.Like(f.Format, pattern, "\\"));
        }
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(f => f.CreatedAt)
            .Skip(query.Page.Skip).Take(query.Page.Size)
            .Select(f => new AnalyzedFileSummary(f.Id, f.ProjectId, f.Project!.Name, f.EnvironmentId,
                f.Environment != null ? f.Environment.Name : null, f.Name, f.Format, f.Architecture, f.SizeBytes,
                f.Sha256, f.GhidraStatus, f.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<AnalyzedFileSummary>(items, total, query.Page);
    }

    public Task<AnalyzedFile?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.AnalyzedFiles.Include(f => f.Project).Include(f => f.Environment).FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<AnalyzedFile?> FindBySha256Async(Guid projectId, string sha256, CancellationToken ct = default) =>
        db.AnalyzedFiles.FirstOrDefaultAsync(f => f.ProjectId == projectId && f.Sha256 == sha256, ct);

    public async Task<IReadOnlyList<AnalyzedFile>> ListByGhidraStatusAsync(GhidraAnalysisStatus status, CancellationToken ct = default) =>
        await db.AnalyzedFiles.Where(f => f.GhidraStatus == status).ToListAsync(ct);

    public Task<int> CountAsync(CancellationToken ct = default) => db.AnalyzedFiles.CountAsync(ct);

    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken ct = default) =>
        db.AnalyzedFiles.CountAsync(f => f.ProjectId == projectId, ct);

    public Task<int> CountByEnvironmentAsync(Guid environmentId, CancellationToken ct = default) =>
        db.AnalyzedFiles.CountAsync(f => f.EnvironmentId == environmentId, ct);

    public void Add(AnalyzedFile file) => db.AnalyzedFiles.Add(file);
}

internal sealed class FileEvidenceStorage(ReaPaths paths) : IFileEvidenceStorage
{
    public string Root => paths.FileDirectory;

    public string NewRelativePath(Guid projectId, Guid fileId, string suffix) =>
        Path.Combine(projectId.ToString("N"), $"{fileId:N}{suffix}");

    public string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!full.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de arquivo inválido.");
        return full;
    }
}
