using Microsoft.EntityFrameworkCore;
using REA.Core;
using REA.Domain;

namespace REA.Infrastructure;

internal sealed class SettingsRepository(ReaDbContext db) : ISettingsRepository
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct))?.Value;

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (string.IsNullOrEmpty(value))
        {
            if (existing is not null) db.Settings.Remove(existing);
            return;
        }
        if (existing is null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else existing.Value = value;
    }
}
