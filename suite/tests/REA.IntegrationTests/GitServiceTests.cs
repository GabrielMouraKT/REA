using Microsoft.EntityFrameworkCore;
using REA.Core;
using REA.Domain;
using REA.Infrastructure;

namespace REA.IntegrationTests;

/// <summary>Git real (git.exe) em repositórios temporários; remoto local via file://.</summary>
public class GitServiceTests
{
    private static async Task Git(TestApp app, string dir, params string[] args)
    {
        var r = await app.Singleton<IGitClient>().RunAsync(dir, args);
        Assert.True(r.Success, $"{r.Command}: {r.Output}");
    }

    private static async Task Identity(TestApp app, string dir)
    {
        await Git(app, dir, "config", "user.name", "Teste REA");
        await Git(app, dir, "config", "user.email", "teste@rea.local");
    }

    private static Task<T> Svc<T>(TestApp app, Func<GitService, Task<T>> f) => app.Run(f);

    private static async Task<(Guid Repo, string Dir)> NewRepo(TestApp app, Guid project, string root, string name)
    {
        var dir = Path.Combine(root, name);
        var r = await Svc(app, s => s.InitAsync(project, dir, name));
        Assert.True(r.Success, r.Error);
        await Identity(app, dir);
        return (r.Value, dir);
    }

    private static async Task CommitAll(TestApp app, Guid repo, string message)
    {
        var st = (await Svc(app, s => s.StatusAsync(repo))).Value!;
        Assert.True((await Svc(app, s => s.StageAsync(repo, st.Changes.Select(c => c.Path).ToList()))).Success);
        var c = await Svc(app, s => s.CommitAsync(repo, message));
        Assert.True(c.Success, c.Error);
    }

    [Fact]
    public async Task Init_stage_commit_log_and_audit()
    {
        Assert.SkipWhen(new REA.ToolIntegration.GitCli().Locate() is null, "Git não instalado.");
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var project = await CaptureFixtures.Project(app, "Projeto Git");
        var (repo, dir) = await NewRepo(app, project, root.Path, "jogo");

        await File.WriteAllTextAsync(Path.Combine(dir, "leia-me.txt"), "olá\n", TestContext.Current.CancellationToken);
        var st = (await Svc(app, s => s.StatusAsync(repo))).Value!;
        Assert.Equal("main", st.Branch);
        Assert.True(st.State.HasFlag(GitSyncState.Untracked));
        Assert.True(st.State.HasFlag(GitSyncState.NoUpstream));
        var change = Assert.Single(st.Changes);
        Assert.Equal(GitChangeKind.Untracked, change.Kind);
        Assert.Contains("+olá", (await Svc(app, s => s.DiffAsync(repo, change))).Value);

        Assert.False((await Svc(app, s => s.CommitAsync(repo, "vazio"))).Success); // nada selecionado
        await CommitAll(app, repo, "Primeiro commit\n\nDetalhes.");

        var log = (await Svc(app, s => s.LogAsync(repo))).Value!;
        Assert.Equal("Primeiro commit", Assert.Single(log).Subject);
        Assert.True((await Svc(app, s => s.StatusAsync(repo))).Value!.State.HasFlag(GitSyncState.Clean));

        var audit = await app.Run<ReaDbContext, List<ActivityEntry>>(db => db.Activity.Where(a => a.EntityId == repo).ToListAsync());
        Assert.Contains(audit, a => a.Action == "Iniciar repositório");
        Assert.Contains(audit, a => a.Action == "Commit" && a.Summary.Contains("Primeiro commit"));
    }

    [Fact]
    public async Task Commit_with_possible_secret_is_blocked_until_confirmed_and_secret_is_not_logged()
    {
        Assert.SkipWhen(new REA.ToolIntegration.GitCli().Locate() is null, "Git não instalado.");
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var project = await CaptureFixtures.Project(app, "Segredos");
        var (repo, dir) = await NewRepo(app, project, root.Path, "s");
        var token = "ghp_" + new string('A', 36);
        await File.WriteAllTextAsync(Path.Combine(dir, "config.cs"), $"var t = \"{token}\";\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dir, ".env"), "X=1\n", TestContext.Current.CancellationToken);
        Assert.True((await Svc(app, s => s.StageAsync(repo, ["config.cs", ".env"]))).Success);

        var scan = (await Svc(app, s => s.ScanStagedAsync(repo))).Value!;
        Assert.Contains(scan, f => f.Path == "config.cs" && f.Line == 1 && f.Rule == "Token do GitHub");
        Assert.Contains(scan, f => f.Path == ".env");
        Assert.All(scan, f => Assert.DoesNotContain(token, f.Excerpt));

        var blocked = await Svc(app, s => s.CommitAsync(repo, "com segredo"));
        Assert.False(blocked.Success);
        Assert.Contains("Possíveis segredos", blocked.Error);
        Assert.DoesNotContain(token, blocked.Error);

        Assert.True((await Svc(app, s => s.CommitAsync(repo, "confirmado", confirmedFindings: true))).Success);
        var audit = await app.Run<ReaDbContext, List<ActivityEntry>>(db => db.Activity.Where(a => a.EntityId == repo).ToListAsync());
        Assert.Contains(audit, a => a.Summary.Contains("confirmado apesar de"));
        Assert.DoesNotContain(audit, a => a.Summary.Contains(token));
    }

    [Fact]
    public async Task Branch_protections()
    {
        Assert.SkipWhen(new REA.ToolIntegration.GitCli().Locate() is null, "Git não instalado.");
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var project = await CaptureFixtures.Project(app, "Branches");
        var (repo, dir) = await NewRepo(app, project, root.Path, "b");
        await File.WriteAllTextAsync(Path.Combine(dir, "a.txt"), "1\n", TestContext.Current.CancellationToken);
        await CommitAll(app, repo, "base");

        Assert.False((await Svc(app, s => s.CreateBranchAsync(repo, "nome inválido", false))).Success);
        Assert.False((await Svc(app, s => s.CreateBranchAsync(repo, "--force", false))).Success);
        Assert.True((await Svc(app, s => s.CreateBranchAsync(repo, "recurso/x", switchTo: true))).Success);
        await File.WriteAllTextAsync(Path.Combine(dir, "b.txt"), "2\n", TestContext.Current.CancellationToken);
        await CommitAll(app, repo, "trabalho no recurso");

        Assert.Contains("ativo", (await Svc(app, s => s.DeleteBranchAsync(repo, "recurso/x", false))).Error);
        Assert.True((await Svc(app, s => s.SwitchBranchAsync(repo, "main"))).Success);

        var unmerged = await Svc(app, s => s.DeleteBranchAsync(repo, "recurso/x", confirmUnmerged: false));
        Assert.False(unmerged.Success);
        Assert.Contains("perderia", unmerged.Error);
        Assert.Contains((await Svc(app, s => s.BranchesAsync(repo))).Value!, b => b.Name == "recurso/x");

        Assert.True((await Svc(app, s => s.DeleteBranchAsync(repo, "recurso/x", confirmUnmerged: true))).Success);
        Assert.DoesNotContain((await Svc(app, s => s.BranchesAsync(repo))).Value!, b => b.Name == "recurso/x");
    }

    [Fact]
    public async Task Merge_conflict_is_reported_and_can_be_aborted()
    {
        Assert.SkipWhen(new REA.ToolIntegration.GitCli().Locate() is null, "Git não instalado.");
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var project = await CaptureFixtures.Project(app, "Conflitos");
        var (repo, dir) = await NewRepo(app, project, root.Path, "c");
        var file = Path.Combine(dir, "x.txt");
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(file, "linha\n", ct);
        await CommitAll(app, repo, "base");
        Assert.True((await Svc(app, s => s.CreateBranchAsync(repo, "outro", true))).Success);
        await File.WriteAllTextAsync(file, "versão do outro\n", ct);
        await CommitAll(app, repo, "outro");
        Assert.True((await Svc(app, s => s.SwitchBranchAsync(repo, "main"))).Success);
        await File.WriteAllTextAsync(file, "versão da main\n", ct);
        await CommitAll(app, repo, "main");

        var merge = await Svc(app, s => s.MergeAsync(repo, "outro"));
        Assert.False(merge.Success);
        Assert.Contains("conflitos", merge.Error, StringComparison.OrdinalIgnoreCase);
        var st = (await Svc(app, s => s.StatusAsync(repo))).Value!;
        Assert.True(st.HasConflicts);
        Assert.Equal("Merge", st.OperationInProgress);
        Assert.False((await Svc(app, s => s.CommitAsync(repo, "tentativa"))).Success);

        Assert.True((await Svc(app, s => s.AbortMergeAsync(repo))).Success);
        Assert.Equal("versão da main\n", (await File.ReadAllTextAsync(file, ct)).Replace("\r\n", "\n"));
        Assert.True((await Svc(app, s => s.StatusAsync(repo))).Value!.State.HasFlag(GitSyncState.Clean));
    }

    [Fact]
    public async Task Push_pull_divergence_never_forces_and_explains()
    {
        Assert.SkipWhen(new REA.ToolIntegration.GitCli().Locate() is null, "Git não instalado.");
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var ct = TestContext.Current.CancellationToken;
        var project = await CaptureFixtures.Project(app, "Sincronia");
        var bare = Path.Combine(root.Path, "remoto.git");
        Directory.CreateDirectory(bare);
        await Git(app, bare, "init", "--bare", "-b", "main");
        var remoteUrl = new Uri(bare).AbsoluteUri;

        var (a, dirA) = await NewRepo(app, project, root.Path, "a");
        await File.WriteAllTextAsync(Path.Combine(dirA, "f.txt"), "1\n", ct);
        await CommitAll(app, a, "c1");
        Assert.False((await Svc(app, s => s.PushAsync(a))).Success); // sem remoto
        await Git(app, dirA, "remote", "add", "origin", remoteUrl);
        Assert.True((await Svc(app, s => s.PushAsync(a))).Success);   // configura upstream
        Assert.True((await Svc(app, s => s.StatusAsync(a))).Value!.State.HasFlag(GitSyncState.Synced));

        var clone = await Svc(app, s => s.CloneAsync(project, remoteUrl, Path.Combine(root.Path, "b"), "b"));
        Assert.True(clone.Success, clone.Error);
        var b = clone.Value;
        var dirB = Path.Combine(root.Path, "b");
        await Identity(app, dirB);
        await File.WriteAllTextAsync(Path.Combine(dirB, "g.txt"), "b\n", ct);
        await CommitAll(app, b, "c2 de B");
        Assert.True((await Svc(app, s => s.PushAsync(b))).Success);

        await File.WriteAllTextAsync(Path.Combine(dirA, "h.txt"), "a\n", ct);
        await CommitAll(app, a, "c3 de A");
        var rejected = await Svc(app, s => s.PushAsync(a));
        Assert.False(rejected.Success);
        Assert.Contains("nunca força", rejected.Error);

        Assert.True((await Svc(app, s => s.FetchAsync(a))).Success);
        var st = (await Svc(app, s => s.StatusAsync(a))).Value!;
        Assert.True(st.State.HasFlag(GitSyncState.Diverged));
        Assert.Equal((1, 1), (st.Ahead, st.Behind));
        var pull = await Svc(app, s => s.PullAsync(a));
        Assert.False(pull.Success);
        Assert.Contains("divergiram", pull.Error);

        // Remoto intacto: continua com o commit de B no topo.
        var remoteLog = await app.Singleton<IGitClient>().LogAsync(bare, 5, ct: ct);
        Assert.Equal("c2 de B", remoteLog[0].Subject);
    }

    [Fact]
    public async Task Repositories_are_scoped_by_project_and_linking_is_idempotent()
    {
        Assert.SkipWhen(new REA.ToolIntegration.GitCli().Locate() is null, "Git não instalado.");
        using var root = new TempDataRoot();
        await using var app = await TestApp.StartAsync(root.Path);
        var p1 = await CaptureFixtures.Project(app, "Projeto Um");
        var p2 = await CaptureFixtures.Project(app, "Projeto Dois");
        var (repo, dir) = await NewRepo(app, p1, root.Path, "comum");

        Assert.Equal(repo, (await Svc(app, s => s.LinkAsync(p1, Path.Combine(dir, "."), null))).Value);
        Assert.False((await Svc(app, s => s.LinkAsync(p1, root.Path + "\\nao-existe", null))).Success);
        Assert.Single((await Svc(app, s => s.ListAsync(p1))));
        Assert.Empty((await Svc(app, s => s.ListAsync(p2))));

        var del = await app.Run<ProjectService, OperationResult>(s => s.DeleteAsync(p1));
        Assert.False(del.Success);
        Assert.True((await Svc(app, s => s.UnlinkAsync(repo))).Success);
        Assert.True(Directory.Exists(Path.Combine(dir, ".git"))); // desvincular não apaga nada
    }

    [Theory]
    [InlineData("https://github.com/dono/repo.git", true)]
    [InlineData("git@github.com:dono/repo.git", true)]
    [InlineData("file:///C:/repos/x.git", true)]
    [InlineData("--upload-pack=calc", false)]
    [InlineData("ext::sh -c touch", false)]
    [InlineData("http://inseguro/repo", false)]
    public void Remote_url_allow_list(string url, bool ok) => Assert.Equal(ok, GitService.IsAllowedRemoteUrl(url));
}
