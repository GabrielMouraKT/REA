using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using REA.Core;
using REA.Domain;

namespace REA.ToolIntegration;

/// <summary>Liga o <see cref="GhidraHeadless"/> ao contrato do núcleo; o caminho vem de Configurações › Integrações.</summary>
internal sealed class GhidraAnalyzerAdapter(IntegrationSettings settings) : IGhidraAnalyzer
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(60);

    public GhidraInfo? Locate() =>
        GhidraHeadless.Locate(settings.GhidraDirectory) is { } g ? new GhidraInfo(g.Directory, g.Version) : null;

    public async Task<GhidraOutcome> AnalyzeAsync(string binaryPath, string displayName, string resultJsonPath, Action<string>? onLog, CancellationToken ct)
    {
        var install = GhidraHeadless.Locate(settings.GhidraDirectory);
        if (install is null) return new GhidraOutcome(GhidraAnalysisStatus.Failed, "Ghidra não encontrado.", null, false, null, null);

        var r = await GhidraHeadless.AnalyzeAsync(install, binaryPath, onLog, Timeout, importName: displayName, ct: ct);
        var status = r.Status switch
        {
            GhidraRunStatus.Concluido => GhidraAnalysisStatus.Completed,
            GhidraRunStatus.Cancelado => GhidraAnalysisStatus.Cancelled,
            _ => GhidraAnalysisStatus.Failed,
        };
        if (r.Export is null) return new GhidraOutcome(status, r.Error, install.Version, false, null, null);

        Directory.CreateDirectory(Path.GetDirectoryName(resultJsonPath)!);
        var temp = resultJsonPath + ".partial";
        await using (var fs = File.Create(temp))
            await JsonSerializer.SerializeAsync(fs, r.Export, GhidraResultJson.Options, CancellationToken.None);
        File.Move(temp, resultJsonPath, overwrite: true);
        return new GhidraOutcome(status, null, install.Version, true, r.Export.Functions.Count, r.Export.Strings.Count);
    }
}

/// <summary>Leitura do JSON guardado do Ghidra, para as telas.</summary>
public static class GhidraResultJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<GhidraExport?> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GhidraExport>(fs, Options, ct);
    }
}

public static class ToolIntegrationServiceCollectionExtensions
{
    public static IServiceCollection AddReaToolIntegration(this IServiceCollection services)
    {
        services.AddSingleton<IGhidraAnalyzer, GhidraAnalyzerAdapter>();
        return services;
    }
}
