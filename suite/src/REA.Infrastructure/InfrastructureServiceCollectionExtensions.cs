using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using REA.Core;

namespace REA.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddReaInfrastructure(this IServiceCollection services, ReaPaths paths)
    {
        paths.EnsureCreated();
        services.AddSingleton(paths);
        services.AddDbContext<ReaDbContext>(o => o.UseSqlite(paths.ConnectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IEnvironmentRepository, EnvironmentRepository>();
        services.AddScoped<IActivityLog, ActivityLog>();
        services.AddScoped<ICaptureRepository, CaptureRepository>();
        services.AddSingleton<ICaptureStorage, CaptureStorage>();
        services.AddScoped<IDatabaseInfoProvider, DatabaseInfoProvider>();
        services.AddSingleton<IBackupService, SqliteBackupService>();
        services.AddSingleton<IToolDetector, ToolDetector>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }
}

/// <summary>Aplica as migrações pendentes (somente aditivas) na inicialização.</summary>
public sealed class DatabaseInitializer(IServiceScopeFactory scopes)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReaDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}

internal sealed class DatabaseInfoProvider(ReaDbContext db, ReaPaths paths) : IDatabaseInfoProvider
{
    public async Task<DatabaseInfo> GetAsync(CancellationToken ct = default)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var size = File.Exists(paths.DatabasePath) ? new FileInfo(paths.DatabasePath).Length : 0;
        return new DatabaseInfo(paths.DatabasePath, size, applied, pending);
    }
}
