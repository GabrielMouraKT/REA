namespace REA.Domain;

public enum GhidraAnalysisStatus
{
    NotRun = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>
/// Arquivo analisado (evidência): executável, biblioteca, pacote de dados etc. O original nunca é alterado:
/// é copiado para a pasta de arquivos do REA com o SHA-256 conferido. Resultados do Ghidra ficam num JSON ao lado.
/// </summary>
public sealed class AnalyzedFile : Entity
{
    public Guid ProjectId { get; set; }
    public ResearchProject? Project { get; set; }
    public Guid? EnvironmentId { get; set; }
    public ResearchEnvironment? Environment { get; set; }

    public string Name { get; set; } = "";
    public string? OriginalPath { get; set; }
    /// <summary>Caminho relativo à pasta de arquivos do REA.</summary>
    public string StoredFile { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    /// <summary>Formato identificado pelos bytes (ex.: PE, ELF, PAK (Unreal Engine)).</summary>
    public string Format { get; set; } = "";
    public string? FormatDescription { get; set; }
    public string? Architecture { get; set; }
    /// <summary>Resumo legível (subsistema, nº de seções/imports, versão do produto…).</summary>
    public string? Summary { get; set; }
    public string? Notes { get; set; }

    public GhidraAnalysisStatus GhidraStatus { get; set; }
    public string? GhidraError { get; set; }
    public string? GhidraVersion { get; set; }
    /// <summary>JSON exportado pelo Ghidra, relativo à pasta de arquivos.</summary>
    public string? GhidraResultFile { get; set; }
    public int? GhidraFunctionCount { get; set; }
    public int? GhidraStringCount { get; set; }
    public DateTimeOffset? GhidraAnalyzedAt { get; set; }
}
