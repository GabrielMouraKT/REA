using Microsoft.EntityFrameworkCore;
using REA.Core;
using REA.Domain;

namespace REA.Infrastructure;

internal sealed class CaptureRepository(ReaDbContext db) : ICaptureRepository
{
    public async Task<PagedResult<CaptureSummary>> ListAsync(CaptureQuery query, CancellationToken ct = default)
    {
        var q = db.Captures.AsNoTracking();
        if (query.ProjectId is { } p) q = q.Where(c => c.ProjectId == p);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{ProjectRepository.EscapeLike(query.Search.Trim())}%";
            q = q.Where(c => EF.Functions.Like(c.Name, pattern, "\\") || EF.Functions.Like(c.Sha256!, pattern, "\\"));
        }
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(c => c.CreatedAt)
            .Skip(query.Page.Skip).Take(query.Page.Size)
            .Select(c => new CaptureSummary(c.Id, c.ProjectId, c.Project!.Name, c.EnvironmentId,
                c.Environment != null ? c.Environment.Name : null, c.Name, c.Source, c.Status, c.AnalysisStatus,
                c.PacketCount, c.FileSizeBytes, c.TcpStreamCount, c.Sha256, c.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<CaptureSummary>(items, total, query.Page);
    }

    public Task<Capture?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Captures.Include(c => c.Project).Include(c => c.Environment).FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Capture>> ListByStatusAsync(CaptureStatus status, CancellationToken ct = default) =>
        await db.Captures.Where(c => c.Status == status).ToListAsync(ct);

    public Task<int> CountAsync(CancellationToken ct = default) => db.Captures.CountAsync(ct);

    public async Task<long> TotalPacketsAsync(CancellationToken ct = default) =>
        await db.Captures.SumAsync(c => (long?)c.PacketCount, ct) ?? 0;

    public async Task<int> TotalTcpStreamsAsync(CancellationToken ct = default) =>
        await db.Captures.SumAsync(c => (int?)c.TcpStreamCount, ct) ?? 0;

    public Task<int> CountByEnvironmentAsync(Guid environmentId, CancellationToken ct = default) =>
        db.Captures.CountAsync(c => c.EnvironmentId == environmentId, ct);

    public Task<int> CountByProjectAsync(Guid projectId, CancellationToken ct = default) =>
        db.Captures.CountAsync(c => c.ProjectId == projectId, ct);

    public void Add(Capture capture) => db.Captures.Add(capture);
}

internal sealed class CaptureStorage(ReaPaths paths) : ICaptureStorage
{
    public string Root => paths.CaptureDirectory;

    public string NewRelativePath(Guid projectId, Guid captureId, string extension) =>
        Path.Combine(projectId.ToString("N"), $"{captureId:N}{extension}");

    public string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        // Nunca resolve para fora da pasta de capturas.
        if (!full.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Caminho de captura inválido.");
        return full;
    }
}
