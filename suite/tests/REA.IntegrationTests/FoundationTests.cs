using REA.Core;

using REA.Domain;

namespace REA.IntegrationTests;

public class PersistenceTests
{
    /// <summary>Critério de aceitação da Fase 1: cria projeto, persiste e recupera após reinicialização.</summary>
    [Fact]
    public async Task Project_and_environment_survive_application_restart()
    {
        using var root = new TempDataRoot();
        Guid projectId;
        await using (var app = await TestApp.StartAsync(root.Path))
        {
            var created = await app.Run<ProjectService, OperationResult<Guid>>(
                s => s.CreateAsync(new ProjectInput("MIR4 Laboratório", "MIR4", "Pesquisa local autorizada")));
            Assert.True(created.Success, created.Error);
            projectId = created.Value;
            var env = await app.Run<EnvironmentService, OperationResult<Guid>>(s => s.CreateAsync(projectId,
                new EnvironmentInput("Lab local", EnvironmentKind.LocalLaboratory, null, "1.0.0", @"C:\Lab", null)));
            Assert.True(env.Success, env.Error);
        }

        await using (var reopened = await TestApp.StartAsync(root.Path))
        {
            var project = await reopened.Run<ProjectService, ResearchProject?>(s => s.GetAsync(projectId));
            Assert.NotNull(project);
            Assert.Equal("MIR4 Laboratório", project.Name);
            Assert.Equal("MIR4", project.GameName);
            var env = Assert.Single(project.Environments);
            Assert.Equal("Lab local", env.Name);
            Assert.Equal("1.0.0", env.ClientVersion);

            var log = await reopened.Run<IActivityLog, IReadOnlyList<ActivityEntry>>(a => a.RecentAsync(10, projectId));
            Assert.Equal(2, log.Count);
            Assert.All(log, e => Assert.False(string.IsNullOrWhiteSpace(e.Operator)));
            Assert.Contains(log, e => e.Summary == "Projeto \"MIR4 Laboratório\" criado.");
        }
    }

    [Fact]
    public async Task Migrations_are_all_applied_on_a_fresh_database()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var info = await app.Run<IDatabaseInfoProvider, DatabaseInfo>(p => p.GetAsync());
        Assert.NotEmpty(info.AppliedMigrations);
        Assert.Empty(info.PendingMigrations);
        Assert.True(File.Exists(app.Paths.DatabasePath));
    }
}

public class ProjectRulesTests
{
    [Fact]
    public async Task Duplicate_names_are_rejected_ignoring_case_and_spaces()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Teste MIR4", null, null)));
        var second = await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("  teste   mir4 ", null, null)));
        Assert.False(second.Success);
        Assert.Contains("Já existe um projeto", second.Error);
    }

    [Fact]
    public async Task Project_with_environments_cannot_be_deleted_only_archived_and_archive_is_idempotent()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var id = (await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Com histórico", null, null)))).Value;
        await app.Run<EnvironmentService, OperationResult<Guid>>(s =>
            s.CreateAsync(id, new EnvironmentInput("Global", EnvironmentKind.Official, null, null, null, null)));

        var delete = await app.Run<ProjectService, OperationResult>(s => s.DeleteAsync(id));
        Assert.False(delete.Success);
        Assert.Contains("Arquive", delete.Error);

        Assert.True((await app.Run<ProjectService, OperationResult>(s => s.ArchiveAsync(id))).Success);
        Assert.True((await app.Run<ProjectService, OperationResult>(s => s.ArchiveAsync(id))).Success);
        var log = await app.Run<IActivityLog, IReadOnlyList<ActivityEntry>>(a => a.RecentAsync(50, id));
        Assert.Single(log, e => e.Action == "Arquivar projeto");

        var addToArchived = await app.Run<EnvironmentService, OperationResult<Guid>>(s =>
            s.CreateAsync(id, new EnvironmentInput("Outro", EnvironmentKind.Custom, null, null, null, null)));
        Assert.False(addToArchived.Success);
    }

    [Fact]
    public async Task Empty_project_can_be_deleted()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var id = (await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Vazio", null, null)))).Value;
        Assert.True((await app.Run<ProjectService, OperationResult>(s => s.DeleteAsync(id))).Success);
        Assert.Null(await app.Run<ProjectService, ResearchProject?>(s => s.GetAsync(id)));
    }

    [Fact]
    public async Task Duplicate_copies_environments_into_a_new_project()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var id = (await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Original", "Jogo", null)))).Value;
        await app.Run<EnvironmentService, OperationResult<Guid>>(s =>
            s.CreateAsync(id, new EnvironmentInput("Lab", EnvironmentKind.LocalLaboratory, null, "2.0", null, null)));

        var copy = await app.Run<ProjectService, OperationResult<Guid>>(s => s.DuplicateAsync(id, "Original (cópia)"));
        Assert.True(copy.Success, copy.Error);
        var copied = await app.Run<ProjectService, ResearchProject?>(s => s.GetAsync(copy.Value));
        var env = Assert.Single(copied!.Environments);
        Assert.Equal("2.0", env.ClientVersion);
        var original = await app.Run<ProjectService, ResearchProject?>(s => s.GetAsync(id));
        Assert.NotEqual(original!.Environments[0].Id, env.Id);
    }

    [Fact]
    public async Task Environment_of_another_project_is_reported_as_not_found()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var a = (await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Projeto A", null, null)))).Value;
        var b = (await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Projeto B", null, null)))).Value;
        var envA = (await app.Run<EnvironmentService, OperationResult<Guid>>(s =>
            s.CreateAsync(a, new EnvironmentInput("Só do A", EnvironmentKind.Custom, null, null, null, null)))).Value;

        var update = await app.Run<EnvironmentService, OperationResult>(s =>
            s.UpdateAsync(b, envA, new EnvironmentInput("Invadido", EnvironmentKind.Custom, null, null, null, null)));
        Assert.False(update.Success);
        Assert.Equal("Ambiente não encontrado.", update.Error);
        var delete = await app.Run<EnvironmentService, OperationResult>(s => s.DeleteAsync(b, envA));
        Assert.False(delete.Success);
        Assert.Single(await app.Run<EnvironmentService, IReadOnlyList<ResearchEnvironment>>(s => s.ListAsync(a)));
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally_and_paging_happens_in_the_database()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        for (var i = 1; i <= 120; i++)
        {
            var n = i;
            await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput($"Projeto {n:000}", null, null)));
        }
        await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Taxa 100% drop", null, null)));

        var page3 = await app.Run<ProjectService, PagedResult<ProjectSummary>>(s =>
            s.ListAsync(new ProjectQuery("Projeto", ProjectStatus.Active, new PageRequest(3, 50))));
        Assert.Equal(120, page3.TotalCount);
        Assert.Equal(3, page3.TotalPages);
        Assert.Equal(20, page3.Items.Count);

        var percent = await app.Run<ProjectService, PagedResult<ProjectSummary>>(s =>
            s.ListAsync(new ProjectQuery("%", null, new PageRequest())));
        Assert.Equal("Taxa 100% drop", Assert.Single(percent.Items).Name);
    }
}

public class BackupTests
{
    [Fact]
    public async Task Backup_and_restore_round_trip_keeps_a_safety_copy()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var backups = app.Singleton<IBackupService>();

        await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Antes do backup", null, null)));
        var backup = await backups.CreateAsync("manual", TestCt.Value);
        await Task.Delay(5, TestCt.Value); // nomes com milissegundos distintos
        await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Depois do backup", null, null)));

        var restore = await backups.RestoreAsync(backup.FileName, TestCt.Value);
        Assert.True(restore.Success, restore.Error);

        var list = await app.Run<ProjectService, PagedResult<ProjectSummary>>(s => s.ListAsync(new ProjectQuery(null, null, new PageRequest())));
        Assert.Equal("Antes do backup", Assert.Single(list.Items).Name);

        var all = await backups.ListAsync(TestCt.Value);
        Assert.Contains(all, b => b.Reason == "Antes de restaurar");
        Assert.Contains(all, b => b.Reason == "Manual");
    }

    [Theory]
    [InlineData(@"..\rea.db")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("nao-existe.db")]
    public async Task Restore_rejects_names_outside_the_backup_folder(string name)
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var result = await app.Singleton<IBackupService>().RestoreAsync(name, TestCt.Value);
        Assert.False(result.Success);
        Assert.Equal("Backup não encontrado.", result.Error);
    }

    [Fact]
    public async Task Restore_rejects_a_corrupted_backup_and_leaves_data_untouched()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        await app.Run<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(new ProjectInput("Intacto", null, null)));
        var fake = Path.Combine(app.Paths.BackupDirectory, "rea-20260101-000000-000-manual.db");
        await File.WriteAllTextAsync(fake, "isto não é sqlite", TestCt.Value);

        var result = await app.Singleton<IBackupService>().RestoreAsync(Path.GetFileName(fake), TestCt.Value);
        Assert.False(result.Success);
        var list = await app.Run<ProjectService, PagedResult<ProjectSummary>>(s => s.ListAsync(new ProjectQuery(null, null, new PageRequest())));
        Assert.Equal("Intacto", Assert.Single(list.Items).Name);
    }

    [Fact]
    public async Task Automatic_backup_runs_once_per_interval()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var backups = app.Singleton<IBackupService>();
        Assert.NotNull(await backups.EnsureRecentAsync(TestCt.Value));
        Assert.Null(await backups.EnsureRecentAsync(TestCt.Value));
    }
}

public class ToolDetectionTests
{
    [Fact]
    public async Task Detects_every_known_tool_without_throwing()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var tools = await app.Singleton<IToolDetector>().DetectAsync(TestCt.Value);
        Assert.Equal(["Npcap", "TShark (Wireshark)", "Ghidra", "Java (JDK)", "REA CLI", "WSL / Kali", "WiX Toolset"],
            tools.Select(t => t.Name));
        Assert.All(tools, t => { if (t.Available) Assert.NotNull(t.Location); });
        foreach (var t in tools)
            TestContext.Current.TestOutputHelper?.WriteLine($"{t.Name}: {t.Available} | {t.Version} | {t.Location}");
    }
}

internal static class TestCt
{
    public static CancellationToken Value => TestContext.Current.CancellationToken;
}
