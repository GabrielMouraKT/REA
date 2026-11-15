using REA.Core;
using REA.ToolIntegration;

namespace REA.ToolTests;

public class GitParsingTests
{
    [Fact]
    public void Porcelain_v2_status_with_rename_conflict_untracked_and_tracking()
    {
        var raw = string.Join('\0',
            "# branch.oid 0123456789abcdef0123456789abcdef01234567",
            "# branch.head main",
            "# branch.upstream origin/main",
            "# branch.ab +2 -1",
            "1 .M N... 100644 100644 100644 aaa bbb src/arquivo com espaço.cs",
            "1 A. N... 000000 100644 100644 000 ccc novo.txt",
            "2 R. N... 100644 100644 100644 ddd eee R100 depois.txt", "antes.txt",
            "u UU N... 100644 100644 100644 100644 f1 f2 f3 conflito.txt",
            "? solto.log", "");
        var s = GitCli.ParseStatus(raw, "Merge");

        Assert.Equal("main", s.Branch);
        Assert.Equal("origin/main", s.Upstream);
        Assert.Equal((2, 1), (s.Ahead, s.Behind));
        Assert.Equal(5, s.Changes.Count);
        Assert.Equal(new GitFileChange("src/arquivo com espaço.cs", null, GitChangeKind.Modified, false, true), s.Changes[0]);
        Assert.Equal(GitChangeKind.Added, s.Changes[1].Kind);
        Assert.True(s.Changes[1].Staged);
        Assert.Equal(("depois.txt", "antes.txt", GitChangeKind.Renamed), (s.Changes[2].Path, s.Changes[2].OldPath, s.Changes[2].Kind));
        Assert.Equal(GitChangeKind.Conflict, s.Changes[3].Kind);
        Assert.Equal(GitChangeKind.Untracked, s.Changes[4].Kind);
        var st = s.State;
        Assert.True(st.HasFlag(GitSyncState.Conflict) && st.HasFlag(GitSyncState.Diverged) && st.HasFlag(GitSyncState.Untracked));
        Assert.Contains("Merge em andamento", s.Describe());
    }

    [Fact]
    public void Fresh_repository_has_no_head_and_detached_head_has_no_branch()
    {
        var fresh = GitCli.ParseStatus("# branch.oid (initial)\0# branch.head main\0");
        Assert.Null(fresh.HeadCommit);
        Assert.True(fresh.State.HasFlag(GitSyncState.Clean) && fresh.State.HasFlag(GitSyncState.NoUpstream));
        var detached = GitCli.ParseStatus("# branch.oid abc\0# branch.head (detached)\0");
        Assert.True(detached.IsDetached);
        Assert.Null(detached.Branch);
    }

    [Fact]
    public void Branch_listing_parses_tracking_and_skips_remote_head()
    {
        const char us = '\u001F', rs = '\u001E';
        var raw = $"refs/heads/main{us}main{us}*{us}origin/main{us}[ahead 1, behind 3]{us}abc1234{us}2026-11-15T10:00:00-03:00{us}msg{rs}\n" +
                  $"refs/heads/velho{us}velho{us} {us}origin/velho{us}[gone]{us}def5678{us}2026-11-14T10:00:00-03:00{us}x{rs}\n" +
                  $"refs/remotes/origin/HEAD{us}origin{us} {us}{us}{us}abc1234{us}2026-11-15T10:00:00-03:00{us}msg{rs}\n";
        var b = GitCli.ParseBranches(raw);
        Assert.Equal(2, b.Count);
        Assert.True(b[0].IsCurrent);
        Assert.Equal((1, 3), (b[0].Ahead, b[0].Behind));
        Assert.True(b[1].UpstreamGone);
    }

    [Theory]
    [InlineData("! [rejected]        main -> main (fetch first)", "nunca força")]
    [InlineData("error: Your local changes to the following files would be overwritten by checkout", "perdidas")]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git", "não é um repositório")]
    [InlineData("fatal: Authentication failed for 'https://github.com/x/y.git/'", "autenticar")]
    [InlineData("error: The branch 'x' is not fully merged.", "perderia")]
    public void Common_git_errors_are_translated(string stderr, string expected) =>
        Assert.Contains(expected, GitCli.Friendly(stderr));

    [Fact]
    public void Secret_scanner_finds_known_formats_masks_them_and_ignores_harmless_lines()
    {
        var key = "AKIA" + "ABCDEFGHIJKLMNOP";
        var diff = string.Join('\n',
            "diff --git a/app.cs b/app.cs", "+++ b/app.cs", "@@ -0,0 +10,4 @@",
            $"+var aws = \"{key}\";",
            "+var senha = \"\";",                       // vazio: não é segredo
            "+// password policy documented elsewhere", // palavra solta: não é segredo
            "+-----BEGIN OPENSSH PRIVATE KEY-----");
        var f = SecretScanner.Scan(["app.cs", "certs/server.pfx", ".env.example", ".env.local"], diff);

        Assert.Contains(f, x => x.Rule == "Chave de acesso AWS" && x.Line == 10 && !x.Excerpt.Contains(key));
        Assert.Contains(f, x => x.Rule == "Chave privada (PEM)" && x.Line == 13);
        Assert.Contains(f, x => x.Path == "certs/server.pfx");
        Assert.Contains(f, x => x.Path == ".env.local");
        Assert.DoesNotContain(f, x => x.Path == ".env.example");
        Assert.DoesNotContain(f, x => x.Line is 11 or 12);
    }
}

public class GitHubCliTests
{
    /// <summary>Somente leitura, na conta real do gh. Ative com REA_TEST_GITHUB=1 (chama a API do GitHub).</summary>
    [Fact]
    public async Task Real_account_and_repo_listing_are_read_through_gh()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("REA_TEST_GITHUB") == "1", "Defina REA_TEST_GITHUB=1 para consultar o GitHub real.");
        var gh = new GitHubCli();
        Assert.SkipWhen(gh.Locate() is null, "GitHub CLI não instalado.");
        var acc = await gh.AccountAsync(TestContext.Current.CancellationToken);
        Assert.True(acc.Success, acc.Error);
        Assert.False(string.IsNullOrWhiteSpace(acc.Value!.Login));
        var repos = await gh.ListReposAsync(null, 5, TestContext.Current.CancellationToken);
        Assert.True(repos.Success, repos.Error);
        Assert.All(repos.Value!, r => Assert.Contains("/", r.NameWithOwner));
    }

    [Theory]
    [InlineData("meu-repo", true)]
    [InlineData("REA.Suite_2", true)]
    [InlineData("--public", false)]
    [InlineData("a b", false)]
    [InlineData("../x", false)]
    public void Repository_names_are_validated(string name, bool ok)
    {
        if (ok) GitHubCli.ValidateName(name);
        else Assert.Throws<ArgumentException>(() => GitHubCli.ValidateName(name));
    }
}
