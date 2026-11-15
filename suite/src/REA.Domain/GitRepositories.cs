namespace REA.Domain;

/// <summary>
/// Repositório Git ligado a um projeto do REA. Um projeto pode ter vários. O REA guarda só o vínculo (pasta local,
/// remoto e branch principal); o estado real (branch atual, alterações, sincronia) é sempre lido do Git na hora.
/// </summary>
public sealed class GitRepositoryLink : Entity
{
    public Guid ProjectId { get; set; }
    public ResearchProject? Project { get; set; }

    public string Name { get; set; } = "";
    /// <summary>Pasta raiz do repositório (onde está a pasta .git).</summary>
    public string LocalPath { get; set; } = "";
    /// <summary>URL do remoto "origin" no momento do vínculo (informativo; o Git é a fonte da verdade).</summary>
    public string? RemoteUrl { get; set; }
    public string DefaultBranch { get; set; } = "main";
    /// <summary>Conta do provedor usada (ex.: login do GitHub), quando conhecida.</summary>
    public string? Account { get; set; }
    public string? Notes { get; set; }
}
