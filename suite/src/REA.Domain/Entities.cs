namespace REA.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum ProjectStatus
{
    Active = 0,
    Archived = 1,
}

/// <summary>Projeto de investigação. Cada projeto isola seus ambientes, evidências e descobertas.</summary>
public sealed class ResearchProject : Entity
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 4000;

    public string Name { get; set; } = "";
    /// <summary>Nome em maiúsculas sem espaços extras, usado para unicidade.</summary>
    public string NormalizedName { get; set; } = "";
    public string? Description { get; set; }
    public string? GameName { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public DateTimeOffset? ArchivedAt { get; set; }

    public List<ResearchEnvironment> Environments { get; set; } = [];

    public static string Normalize(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}

public enum EnvironmentKind
{
    Official = 0,
    PrivateServer = 1,
    LocalLaboratory = 2,
    Emulator = 3,
    Custom = 4,
}

/// <summary>Perfil de ambiente investigado (ex.: cliente oficial, laboratório local).</summary>
public sealed class ResearchEnvironment : Entity
{
    public Guid ProjectId { get; set; }
    public ResearchProject? Project { get; set; }

    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public EnvironmentKind Kind { get; set; }
    public string? Description { get; set; }
    public string? ClientVersion { get; set; }
    public string? ClientDirectory { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Registro de auditoria legível: quem, quando, o quê, antes e depois.</summary>
public sealed class ActivityEntry
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Operator { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Summary { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }
}

public sealed class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
