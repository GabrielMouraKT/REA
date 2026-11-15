using REA.Domain;

namespace REA.Core;

// ---------- Persistência ----------

public sealed record CaptureQuery(Guid? ProjectId, string? Search, PageRequest Page);

public sealed record CaptureSummary(
    Guid Id, Guid ProjectId, string ProjectName, Guid? EnvironmentId, string? EnvironmentName,
    string Name, CaptureSource Source, CaptureStatus Status, CaptureAnalysisStatus AnalysisStatus,
    long PacketCount, long FileSizeBytes, int TcpStreamCount, string? Sha256, DateTimeOffset CreatedAt);

public interface ICaptureRepository
{
    Task<PagedResult<CaptureSummary>> ListAsync(CaptureQuery query, CancellationToken ct = default);
    /// <summary>Busca sempre junto com o projeto quando informado.</summary>
    Task<Capture?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Capture>> ListByStatusAsync(CaptureStatus status, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<long> TotalPacketsAsync(CancellationToken ct = default);
    Task<int> TotalTcpStreamsAsync(CancellationToken ct = default);
    Task<int> CountByEnvironmentAsync(Guid environmentId, CancellationToken ct = default);
    Task<int> CountByProjectAsync(Guid projectId, CancellationToken ct = default);
    void Add(Capture capture);
}

/// <summary>Arquivos de captura dentro da pasta de dados do REA.</summary>
public interface ICaptureStorage
{
    string Root { get; }
    /// <summary>Caminho relativo para um novo arquivo de captura do projeto.</summary>
    string NewRelativePath(Guid projectId, Guid captureId, string extension);
    string Resolve(string relativePath);
}

// ---------- Análise de arquivos ----------

public sealed record CaptureAnalysis(
    string Format,
    long PacketCount,
    long TotalBytes,
    int TcpStreamCount,
    DateTimeOffset? FirstPacketAt,
    DateTimeOffset? LastPacketAt,
    IReadOnlyDictionary<string, long> Protocols,
    bool Truncated,
    string? TruncationReason);

public interface ICaptureFileAnalyzer
{
    /// <summary>Identifica o formato (pcap/pcapng) ou null se não for captura suportada.</summary>
    string? DetectFormat(string path);
    Task<CaptureAnalysis> AnalyzeAsync(string path, CancellationToken ct = default);
}

public interface IFileHasher
{
    Task<string> Sha256Async(string path, CancellationToken ct = default);
}

// ---------- Captura ao vivo ----------

public sealed record CaptureInterfaceInfo(
    string Name, string Description, string? FriendlyName, IReadOnlyList<string> Addresses, bool IsLoopback);

public sealed record LiveCaptureOptions(
    string InterfaceName,
    string? BpfFilter,
    long? MaxPackets,
    long? MaxBytes,
    TimeSpan? MaxDuration,
    int SnapLength = 65535,
    bool Promiscuous = false);

public sealed record LivePacket(long Number, DateTimeOffset Timestamp, int Length, string Summary);

public sealed record LiveCaptureStats(long Packets, long Bytes, long Dropped, long InterfaceDropped, TimeSpan Elapsed);

public enum LiveCaptureEndReason { Stopped, PacketLimit, ByteLimit, DurationLimit, Error }

public sealed record LiveCaptureResult(LiveCaptureEndReason Reason, LiveCaptureStats Stats, string? Error);

public interface ILiveCapture : IAsyncDisposable
{
    LiveCaptureStats Stats { get; }
    /// <summary>Pacotes novos desde a última leitura (para a interface atualizar em lotes).</summary>
    IReadOnlyList<LivePacket> DrainNewPackets(int max);
    Task<LiveCaptureResult> Completion { get; }
    Task StopAsync();
}

public interface ILiveCaptureEngine
{
    /// <summary>Null quando o Npcap não está disponível; a mensagem explica o motivo.</summary>
    string? UnavailableReason { get; }
    IReadOnlyList<CaptureInterfaceInfo> ListInterfaces();
    /// <summary>Valida a expressão BPF compilando-a na interface. Retorna a mensagem de erro ou null.</summary>
    string? ValidateFilter(string interfaceName, string filter);
    ILiveCapture Start(LiveCaptureOptions options, string outputPcapngPath);
}
