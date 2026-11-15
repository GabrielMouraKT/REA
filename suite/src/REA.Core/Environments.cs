using FluentValidation;
using REA.Domain;

namespace REA.Core;

public sealed record EnvironmentInput(
    string Name, EnvironmentKind Kind, string? Description, string? ClientVersion, string? ClientDirectory, string? Notes);

public sealed class EnvironmentInputValidator : AbstractValidator<EnvironmentInput>
{
    public EnvironmentInputValidator()
    {
        RuleFor(x => x.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe o nome do ambiente.")
            .Must(n => n is null || n.Trim().Length <= 120).WithMessage("O nome do ambiente pode ter no máximo 120 caracteres.");
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Escolha um tipo de ambiente válido.");
        RuleFor(x => x.ClientVersion).MaximumLength(80).WithMessage("A versão do cliente pode ter no máximo 80 caracteres.");
        RuleFor(x => x.ClientDirectory).MaximumLength(1024).WithMessage("O diretório pode ter no máximo 1024 caracteres.");
        RuleFor(x => x.Description).MaximumLength(4000).WithMessage("A descrição pode ter no máximo 4000 caracteres.");
        RuleFor(x => x.Notes).MaximumLength(8000).WithMessage("As observações podem ter no máximo 8000 caracteres.");
    }
}

public sealed class EnvironmentService(
    IProjectRepository projects,
    IEnvironmentRepository environments,
    ICaptureRepository captures,
    IAnalyzedFileRepository analyzedFiles,
    IActivityLog activity,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOperatorContext operatorContext)
{
    private readonly EnvironmentInputValidator _validator = new();

    public Task<IReadOnlyList<ResearchEnvironment>> ListAsync(Guid projectId, CancellationToken ct = default) =>
        environments.ListByProjectAsync(projectId, ct);

    public async Task<OperationResult<Guid>> CreateAsync(Guid projectId, EnvironmentInput input, CancellationToken ct = default)
    {
        var project = await projects.GetAsync(projectId, includeEnvironments: false, ct);
        if (project is null) return OperationResult<Guid>.Fail("Projeto não encontrado.");
        if (project.Status == ProjectStatus.Archived)
            return OperationResult<Guid>.Fail("O projeto está arquivado. Reative-o para adicionar ambientes.");
        var error = await ValidateAsync(projectId, input, null, ct);
        if (error is not null) return OperationResult<Guid>.Fail(error);

        var now = clock.Now;
        var env = new ResearchEnvironment { ProjectId = projectId, CreatedAt = now };
        Apply(env, input, now);
        environments.Add(env);
        Audit("Criar ambiente", env, $"Ambiente \"{env.Name}\" criado no projeto \"{project.Name}\".", null, Describe(env));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult<Guid>.Ok(env.Id);
    }

    public async Task<OperationResult> UpdateAsync(Guid projectId, Guid id, EnvironmentInput input, CancellationToken ct = default)
    {
        var env = await environments.GetAsync(projectId, id, ct);
        if (env is null) return OperationResult.Fail("Ambiente não encontrado.");
        var error = await ValidateAsync(projectId, input, id, ct);
        if (error is not null) return OperationResult.Fail(error);

        var before = Describe(env);
        Apply(env, input, clock.Now);
        Audit("Editar ambiente", env, $"Ambiente \"{env.Name}\" editado.", before, Describe(env));
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    /// <summary>Exclusão definitiva só para ambiente sem evidências (capturas). Com histórico, não exclui.</summary>
    public async Task<OperationResult> DeleteAsync(Guid projectId, Guid id, CancellationToken ct = default)
    {
        var env = await environments.GetAsync(projectId, id, ct);
        if (env is null) return OperationResult.Fail("Ambiente não encontrado.");
        if (await captures.CountByEnvironmentAsync(id, ct) > 0)
            return OperationResult.Fail("Este ambiente tem capturas registradas e não pode ser excluído. Arquive o projeto se ele não for mais usado.");
        if (await analyzedFiles.CountByEnvironmentAsync(id, ct) > 0)
            return OperationResult.Fail("Este ambiente tem arquivos analisados registrados e não pode ser excluído. Arquive o projeto se ele não for mais usado.");
        environments.Remove(env);
        Audit("Excluir ambiente", env, $"Ambiente \"{env.Name}\" excluído.", Describe(env), null);
        await unitOfWork.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    private async Task<string?> ValidateAsync(Guid projectId, EnvironmentInput input, Guid? exceptId, CancellationToken ct)
    {
        var result = _validator.Validate(input);
        if (!result.IsValid) return result.Errors[0].ErrorMessage;
        if (await environments.NameExistsAsync(projectId, ResearchProject.Normalize(input.Name), exceptId, ct))
            return $"Este projeto já tem um ambiente chamado \"{input.Name.Trim()}\".";
        return null;
    }

    private static void Apply(ResearchEnvironment env, EnvironmentInput input, DateTimeOffset now)
    {
        env.Name = input.Name.Trim();
        env.NormalizedName = ResearchProject.Normalize(input.Name);
        env.Kind = input.Kind;
        env.Description = ProjectService.Clean(input.Description);
        env.ClientVersion = ProjectService.Clean(input.ClientVersion);
        env.ClientDirectory = ProjectService.Clean(input.ClientDirectory);
        env.Notes = ProjectService.Clean(input.Notes);
        env.UpdatedAt = now;
    }

    private void Audit(string action, ResearchEnvironment env, string summary, string? before, string? after) =>
        activity.Record(new ActivityEntry
        {
            Timestamp = clock.Now,
            Operator = operatorContext.OperatorName,
            Action = action,
            EntityType = "Ambiente",
            EntityId = env.Id,
            ProjectId = env.ProjectId,
            Summary = summary,
            Before = before,
            After = after,
        });

    public static string KindText(EnvironmentKind kind) => kind switch
    {
        EnvironmentKind.Official => "Oficial",
        EnvironmentKind.PrivateServer => "Servidor privado",
        EnvironmentKind.LocalLaboratory => "Laboratório local",
        EnvironmentKind.Emulator => "Emulador",
        EnvironmentKind.Custom => "Personalizado",
        _ => kind.ToString(),
    };

    private static string Describe(ResearchEnvironment e) =>
        $"Nome: {e.Name}; Tipo: {KindText(e.Kind)}; Versão do cliente: {e.ClientVersion ?? "(não informada)"}; " +
        $"Diretório: {e.ClientDirectory ?? "(não informado)"}";
}
