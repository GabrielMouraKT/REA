using REA.Domain;

namespace REA.Core;

public sealed record StartCaptureInput(Guid ProjectId, Guid? EnvironmentId, string Name, LiveCaptureOptions Options);

public sealed record ImportCaptureInput(Guid ProjectId, Guid? EnvironmentId, string SourcePath, string? Name);

/// <summary>Captura em andamento: o registro já existe no banco com situação "Em captura".</summary>
public sealed record ActiveCapture(Guid CaptureId, string FilePath, ILiveCapture Session);

public sealed class CaptureService(
    ICaptureRepository captures,
    IProjectRepository projects,
    IEnvironmentRepository environments,
    ICaptureStorage storage,
    ICaptureFileAnalyzer analyzer,
    IFileHasher hasher,
    ILiveCaptureEngine engine,
    IActivityLog activity,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOperatorContext operatorContext)
{
    public const int NameMaxLength = 160;

    public Task<PagedResult<CaptureSummary>> ListAsync(CaptureQuery query, CancellationToken ct = default) =>
        captures.ListAsync(query, ct);

    public Task<Capture?> GetAsync(Guid id, CancellationToken ct = default) => captures.GetAsync(id, ct);

    public string ResolvePath(Capture capture) => storage.Resolve(capture.StoredFile);

    public async Task<OperationResult<ActiveCapture>> StartLiveAsync(StartCaptureInput input, CancellationToken ct = default)
    {
        if (engine.UnavailableReason is { } reason) return OperationResult<ActiveCapture>.Fail(reason);
        var scopeError = await ValidateScopeAsync(input.ProjectId, input.EnvironmentId, ct);
        if (scopeError is not null) return OperationResult<ActiveCapture>.Fail(scopeError);
        var name = CleanName(input.Name);
        if (name is null) return OperationResult<ActiveCapture>.Fail("Informe um nome para a captura.");
        var iface = engine.ListInterfaces().FirstOrDefault(i => i.Name == input.Options.InterfaceName);
        if (iface is null) return OperationResult<ActiveCapture>.Fail("Interface de rede não encontrada. Atualize a lista de interfaces.");
        if (!string.IsNullOrWhiteSpace(input.Options.BpfFilter)
            && engine.ValidateFilter(iface.Name, input.Options.BpfFilter) is { } filterError)
            return OperationResult<ActiveCapture>.Fail($"Filtro BPF inválido: {filterError}");

        var now = clock.Now;
        var capture = new Capture
        {
            ProjectId = input.ProjectId,
            EnvironmentId = input.EnvironmentId,
            Name = name,
            Source = CaptureSource.Live,
            Status = CaptureStatus.Capturing,
            FileFormat = "pcapng",
            InterfaceName = iface.Name,
            InterfaceDescription = iface.FriendlyName ?? iface.Description,
            CaptureFilter = string.IsNullOrWhiteSpace(input.Options.BpfFilter) ? null : input.Options.BpfFilter.Trim(),
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        capture.StoredFile = storage.NewRelativePath(capture.ProjectId, capture.Id, ".pcapng");
        var path = storage.Resolve(capture.StoredFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        ILiveCapture session;
        try
        {
            session = engine.Start(input.Options with { BpfFilter = capture.CaptureFilter }, path);
        }
        catch (Exception ex)
        {
            return OperationResult<ActiveCapture>.Fail($"Não foi possível iniciar a captura: {ex.Message}");
        }

        captures.Add(capture);
        Audit("Iniciar captura", capture, $"Captura \"{capture.Name}\" iniciada em {capture.InterfaceDescription}" +
                                          (capture.CaptureFilter is null ? "." : $" com filtro \"{capture.CaptureFilter}\"."));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult<ActiveCapture>.Ok(new ActiveCapture(capture.Id, path, session));
    }

    /// <summary>Registra o resultado da captura, calcula o SHA-256 e gera estatísticas.</summary>
    public async Task<OperationResult> FinalizeLiveAsync(Guid captureId, LiveCaptureResult result, CancellationToken ct = default)
    {
        var capture = await captures.GetAsync(captureId, ct);
        if (capture is null) return OperationResult.Fail("Captura não encontrada.");
        capture.Status = result.Reason == LiveCaptureEndReason.Error ? CaptureStatus.Interrupted : CaptureStatus.Completed;
        capture.EndedAt = clock.Now;
        capture.DroppedPackets = result.Stats.Dropped + result.Stats.InterfaceDropped;
        capture.Notes = result.Error is null ? capture.Notes : $"Erro durante a captura: {result.Error}";
        await RefreshFileFactsAsync(capture, ct);
        await AnalyzeCoreAsync(capture, ct);
        capture.UpdatedAt = clock.Now;
        Audit("Encerrar captura", capture,
            $"Captura \"{capture.Name}\" encerrada ({EndReasonText(result.Reason)}): {capture.PacketCount} pacote(s), " +
            $"SHA-256 {capture.Sha256}.");
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    public async Task<OperationResult<Guid>> ImportAsync(ImportCaptureInput input, CancellationToken ct = default)
    {
        var scopeError = await ValidateScopeAsync(input.ProjectId, input.EnvironmentId, ct);
        if (scopeError is not null) return OperationResult<Guid>.Fail(scopeError);
        if (!File.Exists(input.SourcePath)) return OperationResult<Guid>.Fail("Arquivo não encontrado.");
        var format = analyzer.DetectFormat(input.SourcePath);
        if (format is null) return OperationResult<Guid>.Fail("O arquivo não é PCAP nem PCAPNG.");

        var now = clock.Now;
        var capture = new Capture
        {
            ProjectId = input.ProjectId,
            EnvironmentId = input.EnvironmentId,
            Name = CleanName(input.Name) ?? Path.GetFileName(input.SourcePath),
            Source = CaptureSource.Imported,
            Status = CaptureStatus.Completed,
            FileFormat = format,
            OriginalPath = input.SourcePath,
            CreatedAt = now,
            UpdatedAt = now,
        };
        capture.StoredFile = storage.NewRelativePath(capture.ProjectId, capture.Id, format == "pcapng" ? ".pcapng" : ".pcap");
        var target = storage.Resolve(capture.StoredFile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Copia para um arquivo temporário e só então move: o original nunca é tocado.
        var temp = target + ".partial";
        try
        {
            var sourceHash = await hasher.Sha256Async(input.SourcePath, ct);
            File.Copy(input.SourcePath, temp, overwrite: true);
            var copyHash = await hasher.Sha256Async(temp, ct);
            if (copyHash != sourceHash)
            {
                File.Delete(temp);
                return OperationResult<Guid>.Fail("A cópia não confere com o original (SHA-256 diferente). Tente novamente.");
            }
            File.Move(temp, target);
            capture.Sha256 = copyHash;
            capture.FileSizeBytes = new FileInfo(target).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(temp)) File.Delete(temp);
            return OperationResult<Guid>.Fail($"Não foi possível copiar o arquivo: {ex.Message}");
        }

        await AnalyzeCoreAsync(capture, ct);
        captures.Add(capture);
        Audit("Importar captura", capture,
            $"Captura \"{capture.Name}\" importada ({format.ToUpperInvariant()}, {capture.PacketCount} pacote(s), SHA-256 {capture.Sha256}).");
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult<Guid>.Ok(capture.Id);
    }

    public async Task<OperationResult> ReanalyzeAsync(Guid captureId, CancellationToken ct = default)
    {
        var capture = await captures.GetAsync(captureId, ct);
        if (capture is null) return OperationResult.Fail("Captura não encontrada.");
        if (capture.Status == CaptureStatus.Capturing) return OperationResult.Fail("A captura ainda está em andamento.");
        await AnalyzeCoreAsync(capture, ct);
        capture.UpdatedAt = clock.Now;
        await unitOfWork.SaveChangesAsync(ct);
        return capture.AnalysisStatus == CaptureAnalysisStatus.Completed
            ? OperationResult.Ok()
            : OperationResult.Fail(capture.AnalysisError ?? "Falha na análise.");
    }

    /// <summary>
    /// Na inicialização: capturas que ficaram "Em captura" (app fechado à força) passam a "Interrompida",
    /// com hash e estatísticas do que foi gravado.
    /// </summary>
    public async Task<int> RecoverInterruptedAsync(CancellationToken ct = default)
    {
        var stale = await captures.ListByStatusAsync(CaptureStatus.Capturing, ct);
        foreach (var capture in stale)
        {
            capture.Status = CaptureStatus.Interrupted;
            capture.EndedAt ??= clock.Now;
            await RefreshFileFactsAsync(capture, ct);
            await AnalyzeCoreAsync(capture, ct);
            capture.UpdatedAt = clock.Now;
            Audit("Recuperar captura", capture,
                $"Captura \"{capture.Name}\" marcada como interrompida (o aplicativo foi fechado durante a captura); " +
                $"{capture.PacketCount} pacote(s) preservado(s).");
        }
        if (stale.Count > 0) await unitOfWork.SaveChangesAsync(ct);
        return stale.Count;
    }

    private async Task RefreshFileFactsAsync(Capture capture, CancellationToken ct)
    {
        var path = storage.Resolve(capture.StoredFile);
        if (!File.Exists(path))
        {
            capture.FileSizeBytes = 0;
            capture.Sha256 = null;
            return;
        }
        capture.FileSizeBytes = new FileInfo(path).Length;
        capture.Sha256 = await hasher.Sha256Async(path, ct);
    }

    private async Task AnalyzeCoreAsync(Capture capture, CancellationToken ct)
    {
        var path = storage.Resolve(capture.StoredFile);
        try
        {
            var a = await analyzer.AnalyzeAsync(path, ct);
            capture.FileFormat = a.Format;
            capture.PacketCount = a.PacketCount;
            capture.TotalBytes = a.TotalBytes;
            capture.TcpStreamCount = a.TcpStreamCount;
            capture.FirstPacketAt = a.FirstPacketAt;
            capture.LastPacketAt = a.LastPacketAt;
            capture.ProtocolSummary = a.Protocols.Count == 0 ? null
                : string.Join(" · ", a.Protocols.OrderByDescending(p => p.Value).Select(p => $"{p.Key}: {p.Value}"));
            capture.AnalysisStatus = CaptureAnalysisStatus.Completed;
            capture.AnalysisError = a.Truncated ? $"Arquivo incompleto: {a.TruncationReason}" : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            capture.AnalysisStatus = CaptureAnalysisStatus.Failed;
            capture.AnalysisError = ex.Message;
        }
    }

    private async Task<string?> ValidateScopeAsync(Guid projectId, Guid? environmentId, CancellationToken ct)
    {
        var project = await projects.GetAsync(projectId, includeEnvironments: false, ct);
        if (project is null) return "Projeto não encontrado.";
        if (project.Status == ProjectStatus.Archived) return "O projeto está arquivado. Reative-o para adicionar capturas.";
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

    public static string EndReasonText(LiveCaptureEndReason reason) => reason switch
    {
        LiveCaptureEndReason.Stopped => "parada pelo usuário",
        LiveCaptureEndReason.PacketLimit => "limite de pacotes",
        LiveCaptureEndReason.ByteLimit => "limite de tamanho",
        LiveCaptureEndReason.DurationLimit => "limite de tempo",
        _ => "erro",
    };

    public static string StatusText(CaptureStatus status) => status switch
    {
        CaptureStatus.Capturing => "Em captura",
        CaptureStatus.Completed => "Concluída",
        CaptureStatus.Interrupted => "Interrompida",
        _ => "Falhou",
    };

    private void Audit(string action, Capture capture, string summary) =>
        activity.Record(new ActivityEntry
        {
            Timestamp = clock.Now,
            Operator = operatorContext.OperatorName,
            Action = action,
            EntityType = "Captura",
            EntityId = capture.Id,
            ProjectId = capture.ProjectId,
            Summary = summary,
        });
}
