namespace REA.Domain;

public enum CaptureSource
{
    Live = 0,
    Imported = 1,
}

public enum CaptureStatus
{
    Capturing = 0,
    Completed = 1,
    /// <summary>Encerrada por erro ou fechamento inesperado; o arquivo vale até o último bloco gravado.</summary>
    Interrupted = 2,
    Failed = 3,
}

public enum CaptureAnalysisStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
}

/// <summary>
/// Captura de tráfego (evidência). O arquivo PCAP/PCAPNG fica na pasta de capturas do REA;
/// o original importado nunca é alterado (é copiado e tem o SHA-256 registrado).
/// </summary>
public sealed class Capture : Entity
{
    public Guid ProjectId { get; set; }
    public ResearchProject? Project { get; set; }
    public Guid? EnvironmentId { get; set; }
    public ResearchEnvironment? Environment { get; set; }

    public string Name { get; set; } = "";
    public CaptureSource Source { get; set; }
    public CaptureStatus Status { get; set; }
    public string? Notes { get; set; }

    /// <summary>Caminho relativo à pasta de capturas do REA.</summary>
    public string StoredFile { get; set; } = "";
    public string FileFormat { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? Sha256 { get; set; }
    /// <summary>Para importações: caminho do arquivo de origem (informativo).</summary>
    public string? OriginalPath { get; set; }

    public string? InterfaceName { get; set; }
    public string? InterfaceDescription { get; set; }
    public string? CaptureFilter { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long DroppedPackets { get; set; }

    public CaptureAnalysisStatus AnalysisStatus { get; set; }
    public string? AnalysisError { get; set; }
    public long PacketCount { get; set; }
    public long TotalBytes { get; set; }
    public int TcpStreamCount { get; set; }
    public DateTimeOffset? FirstPacketAt { get; set; }
    public DateTimeOffset? LastPacketAt { get; set; }
    /// <summary>Resumo legível de protocolos, ex.: "TCP: 120 · UDP: 8 · ARP: 2".</summary>
    public string? ProtocolSummary { get; set; }
}
