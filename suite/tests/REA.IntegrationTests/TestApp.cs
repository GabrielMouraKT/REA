using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using REA.BinaryAnalysis;
using REA.Core;
using REA.Infrastructure;
using REA.NetworkCapture;
using REA.PacketAnalyzer;
using REA.ToolIntegration;

namespace REA.IntegrationTests;

/// <summary>Pasta de dados temporária com banco SQLite real em disco.</summary>
public sealed class TempDataRoot : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rea-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Monta a mesma composição de serviços que o aplicativo desktop usa.</summary>
public sealed class TestApp : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    public ReaPaths Paths { get; }

    private TestApp(ServiceProvider provider, ReaPaths paths)
    {
        _provider = provider;
        Paths = paths;
    }

    public static async Task<TestApp> StartAsync(string root)
    {
        var paths = new ReaPaths(root);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReaCore();
        services.AddReaInfrastructure(paths);
        services.AddReaPacketAnalyzer();
        services.AddReaNetworkCapture();
        services.AddReaBinaryAnalysis();
        services.AddReaToolIntegration();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        return new TestApp(provider, paths);
    }

    public T Singleton<T>() where T : notnull => _provider.GetRequiredService<T>();

    public async Task<TResult> Run<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}
