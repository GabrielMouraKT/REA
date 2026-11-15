using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using REA.Core;
using REA.Domain;
using REA.Infrastructure;
using REA.ToolIntegration;

namespace REA.IntegrationTests;

public class FileEvidenceTests
{
    private static readonly string Notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
    private static readonly string Choice = Path.Combine(Environment.SystemDirectory, "choice.exe");

    private static Task<OperationResult<Guid>> Import(TestApp app, Guid project, Guid? env, string path, string? name = null) =>
        app.Run<FileEvidenceService, OperationResult<Guid>>(s => s.ImportAsync(new ImportFileInput(project, env, path, name)));

    [Fact]
    public async Task Import_copies_verifies_hash_reads_pe_facts_and_is_idempotent_per_project()
    {
        Assert.SkipUnless(File.Exists(Notepad), "notepad.exe não encontrado.");
        using var root = new TempDataRoot();
        var originalHash = CaptureFixtures.Sha(Notepad);
        Guid fileId, projectA;

        await using (var app = await TestApp.StartAsync(root.Path))
        {
            projectA = await CaptureFixtures.Project(app, "Cliente A");
            var projectB = await CaptureFixtures.Project(app, "Cliente B");

            var r = await Import(app, projectA, null, Notepad, "Bloco de notas");
            Assert.True(r.Success, r.Error);
            fileId = r.Value;

            var again = await Import(app, projectA, null, Notepad);
            Assert.Equal(fileId, again.Value); // mesmo conteúdo no mesmo projeto: não duplica

            var other = await Import(app, projectB, null, Notepad);
            Assert.True(other.Success);
            Assert.NotEqual(fileId, other.Value); // outro projeto tem o próprio registro

            var listA = await app.Run<FileEvidenceService, PagedResult<AnalyzedFileSummary>>(s =>
                s.ListAsync(new AnalyzedFileQuery(projectA, null, new PageRequest())));
            Assert.Equal(fileId, Assert.Single(listA.Items).Id);
        }

        // Reinicia o aplicativo: o registro e a cópia continuam lá.
        await using (var app = await TestApp.StartAsync(root.Path))
        {
            var (file, path) = await app.Run<FileEvidenceService, (AnalyzedFile?, string?)>(async s =>
            {
                var f = await s.GetAsync(fileId);
                return (f, f is null ? null : s.ResolvePath(f));
            });
            Assert.NotNull(file);
            Assert.Equal("Bloco de notas", file!.Name);
            Assert.Equal("PE", file.Format);
            Assert.NotNull(file.Architecture);
            Assert.Contains("seção", file.Summary);
            Assert.Equal(originalHash, file.Sha256);
            Assert.Equal(originalHash, CaptureFixtures.Sha(path!));
            Assert.StartsWith(app.Paths.FileDirectory, path);
            Assert.Equal(GhidraAnalysisStatus.NotRun, file.GhidraStatus);

            var activity = await app.Run<ReaDbContext, List<ActivityEntry>>(db =>
                db.Activity.Where(a => a.EntityId == fileId).ToListAsync());
            Assert.Contains(activity, a => a.Action == "Importar arquivo" && a.Summary.Contains(originalHash));
        }
        Assert.Equal(originalHash, CaptureFixtures.Sha(Notepad));
    }

    [Fact]
    public async Task Projects_and_environments_with_files_cannot_be_deleted_and_archived_projects_reject_imports()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var project = await CaptureFixtures.Project(app, "Com arquivo");
        var env = await CaptureFixtures.Environment(app, project, "Lab");
        var sample = Path.Combine(root.Path, "dados.dat");
        await File.WriteAllBytesAsync(sample, [1, 2, 3, 4, 5], TestContext.Current.CancellationToken);
        Assert.True((await Import(app, project, env, sample)).Success);

        var delEnv = await app.Run<EnvironmentService, OperationResult>(s => s.DeleteAsync(project, env));
        Assert.False(delEnv.Success);
        Assert.Contains("arquivos analisados", delEnv.Error);

        Assert.True((await app.Run<ProjectService, OperationResult>(s => s.ArchiveAsync(project))).Success);
        var archived = await Import(app, project, null, sample + ".novo");
        Assert.False(archived.Success);

        File.Copy(sample, sample + ".2");
        var blocked = await Import(app, project, null, sample + ".2");
        Assert.False(blocked.Success);
        Assert.Contains("arquivado", blocked.Error);
    }

    [Fact]
    public async Task Missing_file_and_foreign_environment_are_rejected()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var a = await CaptureFixtures.Project(app, "Projeto Alfa");
        var b = await CaptureFixtures.Project(app, "Projeto Beta");
        var envOfB = await CaptureFixtures.Environment(app, b, "Lab B");
        var sample = Path.Combine(root.Path, "x.bin");
        await File.WriteAllBytesAsync(sample, [9, 9], TestContext.Current.CancellationToken);

        Assert.Equal("Arquivo não encontrado.", (await Import(app, a, null, sample + ".nao-existe")).Error);
        Assert.Equal("Ambiente não encontrado neste projeto.", (await Import(app, a, envOfB, sample)).Error);
    }

    [Fact]
    public async Task Interrupted_ghidra_runs_are_recovered_as_failed()
    {
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var project = await CaptureFixtures.Project(app, "Recuperação");
        var sample = Path.Combine(root.Path, "y.bin");
        await File.WriteAllBytesAsync(sample, [7, 7, 7], TestContext.Current.CancellationToken);
        var id = (await Import(app, project, null, sample)).Value;

        await app.Run<ReaDbContext, int>(db =>
            db.AnalyzedFiles.Where(f => f.Id == id).ExecuteUpdateAsync(u => u.SetProperty(f => f.GhidraStatus, GhidraAnalysisStatus.Running)));

        Assert.Equal(1, await app.Run<FileEvidenceService, int>(s => s.RecoverInterruptedAsync()));
        var file = await app.Run<FileEvidenceService, AnalyzedFile?>(s => s.GetAsync(id));
        Assert.Equal(GhidraAnalysisStatus.Failed, file!.GhidraStatus);
        Assert.Contains("fechado", file.GhidraError);
    }

    [Fact]
    public async Task Integration_settings_validate_and_persist_ghidra_folder()
    {
        using var root = new TempDataRoot();
        await using (var app = await TestApp.StartAsync(root.Path))
        {
            var bad = await app.Run<SettingsService, OperationResult>(s => s.SaveIntegrationsAsync(new IntegrationSettingsInput(root.Path)));
            Assert.False(bad.Success);
            Assert.Contains("não parece ser do Ghidra", bad.Error);

            var fake = Path.Combine(root.Path, "ghidra");
            Directory.CreateDirectory(Path.Combine(fake, "support"));
            await File.WriteAllTextAsync(Path.Combine(fake, "support", "analyzeHeadless.bat"), "", TestContext.Current.CancellationToken);
            var ok = await app.Run<SettingsService, OperationResult>(s => s.SaveIntegrationsAsync(new IntegrationSettingsInput(fake)));
            Assert.True(ok.Success, ok.Error);
            Assert.Equal(fake, app.Singleton<IntegrationSettings>().GhidraDirectory);
        }
        await using (var app = await TestApp.StartAsync(root.Path))
        {
            Assert.Null(app.Singleton<IntegrationSettings>().GhidraDirectory);
            await app.Run<SettingsService, bool>(async s => { await s.LoadAsync(); return true; });
            Assert.Equal(Path.Combine(root.Path, "ghidra"), app.Singleton<IntegrationSettings>().GhidraDirectory);
        }
    }

    [Fact]
    public async Task Real_ghidra_run_is_recorded_with_result_file_and_audit()
    {
        Assert.SkipWhen(GhidraHeadless.Locate() is null, "Ghidra não encontrado (defina GHIDRA_INSTALL_DIR).");
        Assert.SkipUnless(File.Exists(Choice), "choice.exe não encontrado.");
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var project = await CaptureFixtures.Project(app, "Ghidra real");
        var id = (await Import(app, project, null, Choice)).Value;

        var log = new List<string>();
        var r = await app.Run<FileEvidenceService, OperationResult>(s => s.RunGhidraAsync(id, l => { lock (log) log.Add(l); }));
        Assert.True(r.Success, r.Error + "\n" + string.Join('\n', log.TakeLast(20)));

        var (file, resultPath) = await app.Run<FileEvidenceService, (AnalyzedFile?, string?)>(async s =>
        {
            var f = await s.GetAsync(id);
            return (f, f is null ? null : s.ResolveGhidraResult(f));
        });
        Assert.Equal(GhidraAnalysisStatus.Completed, file!.GhidraStatus);
        Assert.True(file.GhidraFunctionCount > 0);
        Assert.True(file.GhidraStringCount > 0);
        Assert.NotNull(file.GhidraVersion);
        var export = await GhidraResultJson.LoadAsync(resultPath!, TestContext.Current.CancellationToken);
        Assert.Equal("choice.exe", export!.Program, ignoreCase: true); // nome de exibição, não o nome técnico da cópia
        Assert.Equal(file.GhidraFunctionCount, export.Functions.Count);

        var activity = await app.Run<ReaDbContext, List<ActivityEntry>>(db =>
            db.Activity.Where(a => a.EntityId == id && a.Action == "Analisar com Ghidra").ToListAsync());
        Assert.Single(activity);
    }
}
