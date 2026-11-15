using REA.Domain;

namespace REA.Core;

public interface ISettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    /// <summary>Grava ou, com valor nulo/vazio, remove a chave.</summary>
    Task SetAsync(string key, string? value, CancellationToken ct = default);
}

/// <summary>
/// Caminhos de ferramentas externas escolhidos pelo usuário (Configurações › Integrações). Fica em memória para
/// leitura rápida pelos adaptadores e é carregado do banco na inicialização.
/// </summary>
public sealed class IntegrationSettings
{
    private volatile string? _ghidraDirectory;
    public string? GhidraDirectory { get => _ghidraDirectory; set => _ghidraDirectory = value; }
}

public sealed record IntegrationSettingsInput(string? GhidraDirectory);

public sealed class SettingsService(
    ISettingsRepository settings,
    IntegrationSettings integrations,
    IActivityLog activity,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOperatorContext operatorContext)
{
    public const string GhidraDirectoryKey = "integrations.ghidra.directory";

    public async Task LoadAsync(CancellationToken ct = default) =>
        integrations.GhidraDirectory = await settings.GetAsync(GhidraDirectoryKey, ct);

    public async Task<OperationResult> SaveIntegrationsAsync(IntegrationSettingsInput input, CancellationToken ct = default)
    {
        var dir = string.IsNullOrWhiteSpace(input.GhidraDirectory) ? null : input.GhidraDirectory.Trim().Trim('"');
        if (dir is not null)
        {
            if (!Directory.Exists(dir)) return OperationResult.Fail("A pasta do Ghidra não existe.");
            if (!File.Exists(Path.Combine(dir, "support", "analyzeHeadless.bat")))
                return OperationResult.Fail("Esta pasta não parece ser do Ghidra: falta support\\analyzeHeadless.bat.");
        }
        var before = integrations.GhidraDirectory;
        if (string.Equals(before, dir, StringComparison.OrdinalIgnoreCase)) return OperationResult.Ok();

        await settings.SetAsync(GhidraDirectoryKey, dir, ct);
        activity.Record(new ActivityEntry
        {
            Timestamp = clock.Now,
            Operator = operatorContext.OperatorName,
            Action = "Alterar integrações",
            EntityType = "Configuração",
            Summary = dir is null ? "Pasta do Ghidra removida; será usada GHIDRA_INSTALL_DIR, se existir." : $"Pasta do Ghidra definida: {dir}.",
            Before = before is null ? "Pasta do Ghidra: (não definida)" : $"Pasta do Ghidra: {before}",
            After = dir is null ? "Pasta do Ghidra: (não definida)" : $"Pasta do Ghidra: {dir}",
        });
        await unitOfWork.SaveChangesAsync(ct);
        integrations.GhidraDirectory = dir;
        return OperationResult.Ok();
    }
}
