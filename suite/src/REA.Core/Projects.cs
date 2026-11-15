using FluentValidation;
using REA.Domain;

namespace REA.Core;

public sealed record ProjectInput(string Name, string? GameName, string? Description);

public sealed class ProjectInputValidator : AbstractValidator<ProjectInput>
{
    public ProjectInputValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe o nome do projeto.")
            .Must(n => n is null || n.Trim().Length >= 3).WithMessage("O nome do projeto precisa ter pelo menos 3 caracteres.")
            .Must(n => n is null || n.Trim().Length <= ResearchProject.NameMaxLength)
            .WithMessage($"O nome do projeto pode ter no máximo {ResearchProject.NameMaxLength} caracteres.");
        RuleFor(x => x.GameName).MaximumLength(120).WithMessage("O nome do jogo pode ter no máximo 120 caracteres.");
        RuleFor(x => x.Description).MaximumLength(ResearchProject.DescriptionMaxLength)
            .WithMessage($"A descrição pode ter no máximo {ResearchProject.DescriptionMaxLength} caracteres.");
    }
}

public sealed class ProjectService(
    IProjectRepository projects,
    IEnvironmentRepository environments,
    ICaptureRepository captures,
    IAnalyzedFileRepository analyzedFiles,
    IActivityLog activity,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOperatorContext operatorContext)
{
    private readonly ProjectInputValidator _validator = new();

    public Task<PagedResult<ProjectSummary>> ListAsync(ProjectQuery query, CancellationToken ct = default) =>
        projects.ListAsync(query, ct);

    public Task<ResearchProject?> GetAsync(Guid id, CancellationToken ct = default) =>
        projects.GetAsync(id, includeEnvironments: true, ct);

    public async Task<OperationResult<Guid>> CreateAsync(ProjectInput input, CancellationToken ct = default)
    {
        var error = await ValidateAsync(input, exceptId: null, ct);
        if (error is not null) return OperationResult<Guid>.Fail(error);

        var now = clock.Now;
        var project = new ResearchProject
        {
            Name = input.Name.Trim(),
            NormalizedName = ResearchProject.Normalize(input.Name),
            GameName = Clean(input.GameName),
            Description = Clean(input.Description),
            CreatedAt = now,
            UpdatedAt = now,
        };
        projects.Add(project);
        Audit("Criar projeto", project.Id, project.Id, $"Projeto \"{project.Name}\" criado.", null, Describe(project));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult<Guid>.Ok(project.Id);
    }

    public async Task<OperationResult> UpdateAsync(Guid id, ProjectInput input, CancellationToken ct = default)
    {
        var project = await projects.GetAsync(id, includeEnvironments: false, ct);
        if (project is null) return OperationResult.Fail("Projeto não encontrado.");
        var error = await ValidateAsync(input, id, ct);
        if (error is not null) return OperationResult.Fail(error);

        var before = Describe(project);
        project.Name = input.Name.Trim();
        project.NormalizedName = ResearchProject.Normalize(input.Name);
        project.GameName = Clean(input.GameName);
        project.Description = Clean(input.Description);
        project.UpdatedAt = clock.Now;
        Audit("Editar projeto", project.Id, project.Id, $"Projeto \"{project.Name}\" editado.", before, Describe(project));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    /// <summary>Copia o projeto e seus ambientes. Evidências não são copiadas para não misturar investigações.</summary>
    public async Task<OperationResult<Guid>> DuplicateAsync(Guid id, string newName, CancellationToken ct = default)
    {
        var source = await projects.GetAsync(id, includeEnvironments: true, ct);
        if (source is null) return OperationResult<Guid>.Fail("Projeto não encontrado.");
        var input = new ProjectInput(newName, source.GameName, source.Description);
        var error = await ValidateAsync(input, exceptId: null, ct);
        if (error is not null) return OperationResult<Guid>.Fail(error);

        var now = clock.Now;
        var copy = new ResearchProject
        {
            Name = newName.Trim(),
            NormalizedName = ResearchProject.Normalize(newName),
            GameName = source.GameName,
            Description = source.Description,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var env in source.Environments)
        {
            copy.Environments.Add(new ResearchEnvironment
            {
                ProjectId = copy.Id,
                Name = env.Name,
                NormalizedName = env.NormalizedName,
                Kind = env.Kind,
                Description = env.Description,
                ClientVersion = env.ClientVersion,
                ClientDirectory = env.ClientDirectory,
                Notes = env.Notes,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        projects.Add(copy);
        Audit("Duplicar projeto", copy.Id, copy.Id,
            $"Projeto \"{copy.Name}\" criado como cópia de \"{source.Name}\" com {copy.Environments.Count} ambiente(s).",
            null, Describe(copy));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult<Guid>.Ok(copy.Id);
    }

    public Task<OperationResult> ArchiveAsync(Guid id, CancellationToken ct = default) =>
        SetStatusAsync(id, ProjectStatus.Archived, ct);

    public Task<OperationResult> RestoreAsync(Guid id, CancellationToken ct = default) =>
        SetStatusAsync(id, ProjectStatus.Active, ct);

    /// <summary>Exclusão definitiva só para projeto sem histórico (sem ambientes). Com histórico, arquivar.</summary>
    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var project = await projects.GetAsync(id, includeEnvironments: false, ct);
        if (project is null) return OperationResult.Fail("Projeto não encontrado.");
        if (await environments.CountAsync(id, ct) > 0)
            return OperationResult.Fail("Este projeto tem ambientes cadastrados. Arquive o projeto em vez de excluir.");
        if (await captures.CountByProjectAsync(id, ct) > 0)
            return OperationResult.Fail("Este projeto tem capturas registradas. Arquive o projeto em vez de excluir.");
        if (await analyzedFiles.CountByProjectAsync(id, ct) > 0)
            return OperationResult.Fail("Este projeto tem arquivos analisados registrados. Arquive o projeto em vez de excluir.");

        projects.Remove(project);
        Audit("Excluir projeto", project.Id, null, $"Projeto \"{project.Name}\" excluído definitivamente.", Describe(project), null);
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    private async Task<OperationResult> SetStatusAsync(Guid id, ProjectStatus status, CancellationToken ct)
    {
        var project = await projects.GetAsync(id, includeEnvironments: false, ct);
        if (project is null) return OperationResult.Fail("Projeto não encontrado.");
        if (project.Status == status) return OperationResult.Ok(); // idempotente

        var before = StatusText(project.Status);
        project.Status = status;
        project.ArchivedAt = status == ProjectStatus.Archived ? clock.Now : null;
        project.UpdatedAt = clock.Now;
        var verb = status == ProjectStatus.Archived ? "arquivado" : "reativado";
        Audit(status == ProjectStatus.Archived ? "Arquivar projeto" : "Reativar projeto", project.Id, project.Id,
            $"Projeto \"{project.Name}\" {verb}.", $"Situação: {before}", $"Situação: {StatusText(status)}");
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    private async Task<string?> ValidateAsync(ProjectInput input, Guid? exceptId, CancellationToken ct)
    {
        var result = _validator.Validate(input);
        if (!result.IsValid) return result.Errors[0].ErrorMessage;
        if (await projects.NameExistsAsync(ResearchProject.Normalize(input.Name), exceptId, ct))
            return $"Já existe um projeto chamado \"{input.Name.Trim()}\". Escolha outro nome.";
        return null;
    }

    private void Audit(string action, Guid entityId, Guid? projectId, string summary, string? before, string? after) =>
        activity.Record(new ActivityEntry
        {
            Timestamp = clock.Now,
            Operator = operatorContext.OperatorName,
            Action = action,
            EntityType = "Projeto",
            EntityId = entityId,
            ProjectId = projectId,
            Summary = summary,
            Before = before,
            After = after,
        });

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string StatusText(ProjectStatus status) => status switch
    {
        ProjectStatus.Active => "Ativo",
        ProjectStatus.Archived => "Arquivado",
        _ => status.ToString(),
    };

    private static string Describe(ResearchProject p) =>
        $"Nome: {p.Name}; Jogo: {p.GameName ?? "(não informado)"}; Situação: {StatusText(p.Status)}; " +
        $"Descrição: {p.Description ?? "(vazia)"}";
}
