using REA.Domain;

namespace REA.Core;

public sealed record AnalyzedFileQuery(Guid? ProjectId, string? Search, PageRequest Page);

public sealed record AnalyzedFileSummary(
    Guid Id, Guid ProjectId, string ProjectName, Guid? EnvironmentId, string? EnvironmentName,
    string Name, string Format, string? Architecture, long SizeBytes, string Sha256,
    GhidraAnalysisStatus GhidraStatus, DateTimeOffset CreatedAt);

public interface IAnalyzedFileRepository
{
    Task<PagedResult<AnalyzedFileSummary>> ListAsync(AnalyzedFileQuery query, CancellationToken ct = default);
    Task<AnalyzedFile?> GetAsync(Guid id, CancellationToken ct = default);
    Task<AnalyzedFile?> FindBySha256Async(Guid projectId, string sha256, CancellationToken ct = default);
    Task<IReadOnlyList<AnalyzedFile>> ListByGhidraStatusAsync(GhidraAnalysisStatus status, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<int> CountByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<int> CountByEnvironmentAsync(Guid environmentId, CancellationToken ct = default);
    void Add(AnalyzedFile file);
}

/// <summary>Pasta de arquivos-evidência do REA (separada das capturas).</summary>
public interface IFileEvidenceStorage
{
    string Root { get; }
    string NewRelativePath(Guid projectId, Guid fileId, string suffix);
    string Resolve(string relativePath);
}

/// <summary>Fatos lidos do arquivo pelos bytes. Implementado pelo módulo de análise binária.</summary>
public sealed record BinaryFacts(string Format, string Description, string? Architecture, string? Summary);

public interface IBinaryInspector
{
    Task<BinaryFacts> InspectAsync(string path, CancellationToken ct = default);
}

public sealed record GhidraInfo(string Directory, string Version);

/// <param name="ResultWritten">O JSON do Ghidra foi gravado no caminho pedido.</param>
public sealed record GhidraOutcome(
    GhidraAnalysisStatus Status, string? Error, string? Version, bool ResultWritten, int? FunctionCount, int? StringCount);

/// <summary>Execução do Ghidra headless. Implementado pelo módulo de integração de ferramentas.</summary>
public interface IGhidraAnalyzer
{
    /// <summary>Instalação encontrada, ou null (o Ghidra é do usuário; o REA não instala).</summary>
    GhidraInfo? Locate();
    /// <param name="displayName">Nome com que o programa aparece no Ghidra (o arquivo guardado tem nome técnico).</param>
    Task<GhidraOutcome> AnalyzeAsync(string binaryPath, string displayName, string resultJsonPath, Action<string>? onLog, CancellationToken ct);
}

public sealed record ImportFileInput(Guid ProjectId, Guid? EnvironmentId, string SourcePath, string? Name);

public sealed class FileEvidenceService(
    IAnalyzedFileRepository files,
    IProjectRepository projects,
    IEnvironmentRepository environments,
    IFileEvidenceStorage storage,
    IBinaryInspector inspector,
    IFileHasher hasher,
    IGhidraAnalyzer ghidra,
    IActivityLog activity,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOperatorContext operatorContext)
{
    public const int NameMaxLength = 160;

    public Task<PagedResult<AnalyzedFileSummary>> ListAsync(AnalyzedFileQuery query, CancellationToken ct = default) =>
        files.ListAsync(query, ct);

    public Task<AnalyzedFile?> GetAsync(Guid id, CancellationToken ct = default) => files.GetAsync(id, ct);

    public string ResolvePath(AnalyzedFile file) => storage.Resolve(file.StoredFile);

    public string? ResolveGhidraResult(AnalyzedFile file) =>
        file.GhidraResultFile is null ? null : storage.Resolve(file.GhidraResultFile);

    /// <summary>
    /// Copia o arquivo para a pasta do REA (original intocado), confere o SHA-256 da cópia e registra os fatos lidos
    /// dos bytes. Importar o mesmo conteúdo duas vezes no mesmo projeto devolve o registro existente.
    /// </summary>
    public async Task<OperationResult<Guid>> ImportAsync(ImportFileInput input, CancellationToken ct = default)
    {
        var scopeError = await ValidateScopeAsync(input.ProjectId, input.EnvironmentId, ct);
        if (scopeError is not null) return OperationResult<Guid>.Fail(scopeError);
        if (!File.Exists(input.SourcePath)) return OperationResult<Guid>.Fail("Arquivo não encontrado.");

        string sourceHash;
        try { sourceHash = await hasher.Sha256Async(input.SourcePath, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<Guid>.Fail($"Não foi possível ler o arquivo: {ex.Message}");
        }
        if (await files.FindBySha256Async(input.ProjectId, sourceHash, ct) is { } existing)
            return OperationResult<Guid>.Ok(existing.Id);

        var now = clock.Now;
        var file = new AnalyzedFile
        {
            ProjectId = input.ProjectId,
            EnvironmentId = input.EnvironmentId,
            Name = CleanName(input.Name) ?? CleanName(Path.GetFileName(input.SourcePath)) ?? "arquivo",
            OriginalPath = input.SourcePath,
            CreatedAt = now,
            UpdatedAt = now,
        };
        file.StoredFile = storage.NewRelativePath(file.ProjectId, file.Id, ".bin");
        var target = storage.Resolve(file.StoredFile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var temp = target + ".partial";
        try
        {
            File.Copy(input.SourcePath, temp, overwrite: true);
            var copyHash = await hasher.Sha256Async(temp, ct);
            if (copyHash != sourceHash)
            {
                File.Delete(temp);
                return OperationResult<Guid>.Fail("O arquivo mudou durante a cópia ou a cópia não confere (SHA-256 diferente). Tente novamente.");
            }
            File.Move(temp, target);
            file.Sha256 = copyHash;
            file.SizeBytes = new FileInfo(target).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(temp)) File.Delete(temp);
            return OperationResult<Guid>.Fail($"Não foi possível copiar o arquivo: {ex.Message}");
        }

        try
        {
            var facts = await inspector.InspectAsync(target, ct);
            file.Format = facts.Format;
            file.FormatDescription = facts.Description;
            file.Architecture = facts.Architecture;
            file.Summary = facts.Summary;
        }
        catch (OperationCanceledException) { File.Delete(target); throw; }
        catch (Exception ex)
        {
            file.Format = "Desconhecido";
            file.FormatDescription = $"Falha ao ler a estrutura: {ex.Message}";
        }

        files.Add(file);
        Audit("Importar arquivo", file,
            $"Arquivo \"{file.Name}\" importado ({file.Format}, {file.SizeBytes} bytes, SHA-256 {file.Sha256}).");
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult<Guid>.Ok(file.Id);
    }

    public GhidraInfo? LocateGhidra() => ghidra.Locate();

    /// <summary>Executa o Ghidra headless na cópia guardada e registra o resultado (também em falha ou cancelamento).</summary>
    public async Task<OperationResult> RunGhidraAsync(Guid fileId, Action<string>? onLog, CancellationToken ct = default)
    {
        var file = await files.GetAsync(fileId, ct);
        if (file is null) return OperationResult.Fail("Arquivo não encontrado.");
        if (file.GhidraStatus == GhidraAnalysisStatus.Running) return OperationResult.Fail("O Ghidra já está analisando este arquivo.");
        if (ghidra.Locate() is null)
            return OperationResult.Fail("Ghidra não encontrado. Informe a pasta do Ghidra em Configurações › Integrações ou defina GHIDRA_INSTALL_DIR.");

        file.GhidraStatus = GhidraAnalysisStatus.Running;
        file.GhidraError = null;
        file.UpdatedAt = clock.Now;
        await unitOfWork.SaveChangesAsync(ct);

        var resultRelative = storage.NewRelativePath(file.ProjectId, file.Id, ".ghidra.json");
        var resultPath = storage.Resolve(resultRelative);
        GhidraOutcome outcome;
        try
        {
            outcome = await ghidra.AnalyzeAsync(storage.Resolve(file.StoredFile), file.Name, resultPath, onLog, ct);
        }
        catch (OperationCanceledException)
        {
            outcome = new GhidraOutcome(GhidraAnalysisStatus.Cancelled, "Análise cancelada.", null, false, null, null);
        }
        catch (Exception ex)
        {
            outcome = new GhidraOutcome(GhidraAnalysisStatus.Failed, ex.Message, null, false, null, null);
        }

        // Grava o desfecho mesmo se o chamador cancelou: o registro não pode ficar "Em análise".
        file.GhidraStatus = outcome.Status;
        file.GhidraError = outcome.Error;
        file.GhidraVersion = outcome.Version ?? file.GhidraVersion;
        if (outcome.Status == GhidraAnalysisStatus.Completed && outcome.ResultWritten)
        {
            file.GhidraResultFile = resultRelative;
            file.GhidraFunctionCount = outcome.FunctionCount;
            file.GhidraStringCount = outcome.StringCount;
            file.GhidraAnalyzedAt = clock.Now;
        }
        file.UpdatedAt = clock.Now;
        Audit("Analisar com Ghidra", file, outcome.Status == GhidraAnalysisStatus.Completed
            ? $"Ghidra {outcome.Version} analisou \"{file.Name}\": {outcome.FunctionCount} função(ões), {outcome.StringCount} string(s)."
            : $"Análise do Ghidra em \"{file.Name}\" {StatusText(outcome.Status).ToLowerInvariant()}: {outcome.Error}");
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return outcome.Status == GhidraAnalysisStatus.Completed ? OperationResult.Ok() : OperationResult.Fail(outcome.Error ?? StatusText(outcome.Status));
    }

    /// <summary>Na inicialização: análises que ficaram "Em análise" (app fechado) passam a "Falhou" com o motivo.</summary>
    public async Task<int> RecoverInterruptedAsync(CancellationToken ct = default)
    {
        var stale = await files.ListByGhidraStatusAsync(GhidraAnalysisStatus.Running, ct);
        foreach (var f in stale)
        {
            f.GhidraStatus = GhidraAnalysisStatus.Failed;
            f.GhidraError = "O aplicativo foi fechado durante a análise do Ghidra. Execute novamente.";
            f.UpdatedAt = clock.Now;
            Audit("Recuperar análise", f, $"Análise do Ghidra em \"{f.Name}\" marcada como interrompida.");
        }
        if (stale.Count > 0) await unitOfWork.SaveChangesAsync(ct);
        return stale.Count;
    }

    public static string StatusText(GhidraAnalysisStatus s) => s switch
    {
        GhidraAnalysisStatus.NotRun => "Não executada",
        GhidraAnalysisStatus.Running => "Em análise",
        GhidraAnalysisStatus.Completed => "Concluída",
        GhidraAnalysisStatus.Cancelled => "Cancelada",
        _ => "Falhou",
    };

    private async Task<string?> ValidateScopeAsync(Guid projectId, Guid? environmentId, CancellationToken ct)
    {
        var project = await projects.GetAsync(projectId, includeEnvironments: false, ct);
        if (project is null) return "Projeto não encontrado.";
        if (project.Status == ProjectStatus.Archived) return "O projeto está arquivado. Reative-o para adicionar arquivos.";
        if (environmentId is { } envId && await environments.GetAsync(projectId, envId, ct) is null)
            return "Ambiente não encontrado neste projeto.";
        return null;
    }

    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim();
        return n.Length > NameMaxLength ? n[..NameMaxLength] : n;
    }

    private void Audit(string action, AnalyzedFile file, string summary) =>
        activity.Record(new ActivityEntry
        {
            Timestamp = clock.Now,
            Operator = operatorContext.OperatorName,
            Action = action,
            EntityType = "Arquivo",
            EntityId = file.Id,
            ProjectId = file.ProjectId,
            Summary = summary.Length > 2000 ? summary[..2000] : summary,
        });
}
