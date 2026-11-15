using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using REA.Core;
using REA.Desktop.Services;
using REA.Domain;

namespace REA.Desktop.ViewModels;

public sealed record GitRepoOption(Guid Id, string Label, string Path);

/// <summary>Base das telas que trabalham sobre um repositório: seletor, estado atual e explicação para iniciantes.</summary>
public abstract partial class GitRepoPageBase(ScopedRunner runner, NotificationService notify, ViewModeService mode) : ObservableObject
{
    protected ScopedRunner Runner { get; } = runner;
    protected NotificationService Notify { get; } = notify;
    public ViewModeService Mode { get; } = mode;
    public ObservableCollection<GitRepoOption> Repos { get; } = [];
    private bool _loading;

    [ObservableProperty] private GitRepoOption? _selectedRepo;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private GitStatus? _status;

    public bool HasNoRepos => Repos.Count == 0;
    public abstract string Explanation { get; }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _loading = true;
        Repos.Clear();
        var list = await Runner.RunAsync<GitService, IReadOnlyList<GitRepositorySummary>>(s => s.ListAsync(null));
        foreach (var r in list) Repos.Add(new GitRepoOption(r.Id, $"{r.Name} · {r.ProjectName}", r.LocalPath));
        OnPropertyChanged(nameof(HasNoRepos));
        _loading = false;
        SelectedRepo = parameter is Guid id ? Repos.FirstOrDefault(r => r.Id == id) : Repos.FirstOrDefault();
    }

    async partial void OnSelectedRepoChanged(GitRepoOption? value)
    {
        if (_loading || value is null) return;
        await RefreshAsync();
    }

    [RelayCommand]
    protected async Task RefreshAsync()
    {
        if (SelectedRepo is null) return;
        IsBusy = true;
        try
        {
            var st = await Runner.RunAsync<GitService, OperationResult<GitStatus>>(s => s.StatusAsync(SelectedRepo.Id));
            Status = st.Value;
            StateText = st.Success
                ? $"Branch: {st.Value!.Branch ?? "(nenhum — HEAD destacado)"} · {st.Value.Describe()}"
                : st.Error!;
            if (st.Success) await LoadAsync(SelectedRepo.Id, st.Value!);
        }
        catch (Exception ex) { Notify.Error($"Falha ao ler o repositório: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    protected abstract Task LoadAsync(Guid repoId, GitStatus status);

    /// <summary>Executa uma operação, mostra o resultado em linguagem simples e recarrega o estado.</summary>
    protected async Task RunOpAsync(Func<GitService, Task<OperationResult>> op, string success)
    {
        if (SelectedRepo is null) return;
        IsBusy = true;
        try
        {
            var r = await Runner.RunAsync(op);
            if (r.Success) Notify.Success(success); else Notify.Error(r.Error!);
        }
        catch (Exception ex) { Notify.Error(ex.Message); }
        finally { IsBusy = false; }
        await RefreshAsync();
    }
}

// =====================================================================================
// Dashboard
// =====================================================================================

public sealed record GitRepoRow(Guid Id, string Name, string Project, string Path, string Branch, string State, string Remote);

public sealed partial class GitDashboardViewModel(ScopedRunner runner, NotificationService notify, INavigator navigator) : ObservableObject, IPage
{
    public string Title => "Git Control Center";
    public ObservableCollection<GitRepoRow> Rows { get; } = [];
    [ObservableProperty] private string _toolsText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private GitRepoRow? _selected;
    public bool IsEmpty => !IsBusy && Rows.Count == 0;

    public Task OnNavigatedToAsync(object? parameter) => Refresh();

    [RelayCommand]
    private async Task Refresh()
    {
        IsBusy = true;
        Rows.Clear();
        try
        {
            var (gitPath, ghPath) = await runner.RunAsync<GitService, (string?, string?)>(s => Task.FromResult((s.GitPath, s.GitHubCliPath)));
            ToolsText = (gitPath is null ? "Git não encontrado (instale em git-scm.com)." : "Git encontrado.") + " " +
                        (ghPath is null ? "GitHub CLI não encontrado (necessário só para o GitHub)." : "GitHub CLI encontrado — veja a conta em Accounts.");
            var repos = await runner.RunAsync<GitService, IReadOnlyList<GitRepositorySummary>>(s => s.ListAsync(null));
            foreach (var r in repos)
            {
                var st = await runner.RunAsync<GitService, OperationResult<GitStatus>>(s => s.StatusAsync(r.Id));
                Rows.Add(new GitRepoRow(r.Id, r.Name, r.ProjectName, r.LocalPath,
                    st.Value?.Branch ?? "—", st.Success ? st.Value!.Describe() : $"Erro: {st.Error}",
                    r.RemoteUrl is null ? "sem remoto" : GitService.RedactUrl(r.RemoteUrl)));
            }
        }
        catch (Exception ex) { notify.Error(ex.Message); }
        finally { IsBusy = false; OnPropertyChanged(nameof(IsEmpty)); }
    }

    [RelayCommand] private Task OpenRepositories() => navigator.NavigateAsync(NavigationCatalog.GitRepositories);
    [RelayCommand] private Task OpenCommit(GitRepoRow? row) => row is null ? Task.CompletedTask : navigator.NavigateAsync(NavigationCatalog.GitCommit, row.Id);
    [RelayCommand] private Task OpenSync(GitRepoRow? row) => row is null ? Task.CompletedTask : navigator.NavigateAsync(NavigationCatalog.GitSync, row.Id);
}

// =====================================================================================
// Accounts
// =====================================================================================

public sealed record GitHubRepoRow(string Name, string Visibility, string Updated, string Description, string Url);

public sealed partial class GitAccountsViewModel(IGitHubClient gh, NotificationService notify) : ObservableObject, IPage
{
    public string Title => "Accounts";
    public ObservableCollection<FactRow> Facts { get; } = [];
    public ObservableCollection<GitHubRepoRow> Remote { get; } = [];
    [ObservableProperty] private string _summary = "Verificando a conta…";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _connected;

    public Task OnNavigatedToAsync(object? parameter) => Refresh();

    [RelayCommand]
    private async Task Refresh()
    {
        IsBusy = true;
        Facts.Clear(); Remote.Clear();
        try
        {
            if (gh.Locate() is null)
            {
                Connected = false;
                Summary = "O GitHub CLI (gh) não está instalado. Instale em cli.github.com e depois rode, no PowerShell: gh auth login";
                return;
            }
            var acc = await gh.AccountAsync();
            Connected = acc.Success;
            if (!acc.Success) { Summary = acc.Error!; return; }
            var a = acc.Value!;
            Summary = $"Conectado ao GitHub como {a.Login}. O REA usa o login do GitHub CLI guardado no cofre do Windows; não pede senha nem guarda token.";
            Facts.Add(new FactRow("Conta", a.Login + (a.Name is null ? "" : $" ({a.Name})")));
            if (a.Url is not null) Facts.Add(new FactRow("Perfil", a.Url));
            Facts.Add(new FactRow("Permissões do login", a.Scopes.Count == 0 ? "não informadas" : string.Join(", ", a.Scopes)));
            Facts.Add(new FactRow("Organizações", a.Organizations.Count == 0 ? "nenhuma visível" : string.Join(", ", a.Organizations)));
            var repos = await gh.ListReposAsync(null, 100);
            if (repos.Success)
                foreach (var r in repos.Value!)
                    Remote.Add(new GitHubRepoRow(r.NameWithOwner, r.Visibility + (r.IsArchived ? " · arquivado" : ""),
                        r.UpdatedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) ?? "—", r.Description ?? "", r.Url));
            else notify.Warning(repos.Error!);
        }
        catch (Exception ex) { notify.Error(ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenTerminalLogin()
    {
        // Abre um PowerShell para o próprio usuário fazer o login oficial (o REA não digita nada por ele).
        Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit -Command \"Write-Host 'Login oficial do GitHub. Siga as instruções:'; gh auth login\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenTerminalLogout() =>
        Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit -Command \"gh auth logout\"") { UseShellExecute = true });
}

// =====================================================================================
// Repositories
// =====================================================================================

public sealed partial class GitRepositoriesViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs, INavigator navigator)
    : ObservableObject, IPage
{
    public string Title => "Repositories";
    public ObservableCollection<ProjectFilterOption> Projects { get; } = [];
    public ObservableCollection<GitRepositorySummary> Items { get; } = [];
    [ObservableProperty] private ProjectFilterOption? _project;
    [ObservableProperty] private GitRepositorySummary? _selected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _cloneUrl = "";
    [ObservableProperty] private string _githubName = "";
    [ObservableProperty] private bool _githubPrivate = true;
    [ObservableProperty] private string _githubDescription = "";

    public bool HasProject => Project?.Id is not null;
    public bool HasSelection => Selected is not null;
    partial void OnSelectedChanged(GitRepositorySummary? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        if (value is not null && GithubName.Length == 0) GithubName = new string(value.Name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray());
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        var projects = await runner.RunAsync<ProjectService, PagedResult<ProjectSummary>>(
            s => s.ListAsync(new ProjectQuery(null, ProjectStatus.Active, new PageRequest(1, 500))));
        Projects.Clear();
        foreach (var p in projects.Items.OrderBy(p => p.Name)) Projects.Add(new ProjectFilterOption(p.Id, p.Name));
        Project ??= Projects.FirstOrDefault();
        await Load();
    }

    async partial void OnProjectChanged(ProjectFilterOption? value) { OnPropertyChanged(nameof(HasProject)); await Load(); }

    [RelayCommand]
    private async Task Load()
    {
        Items.Clear();
        if (Project?.Id is not { } id) return;
        foreach (var r in await runner.RunAsync<GitService, IReadOnlyList<GitRepositorySummary>>(s => s.ListAsync(id))) Items.Add(r);
    }

    private async Task Do(Func<GitService, Task<OperationResult<Guid>>> op, string ok)
    {
        IsBusy = true;
        try
        {
            var r = await runner.RunAsync(op);
            if (r.Success) notify.Success(ok); else notify.Error(r.Error!);
        }
        catch (Exception ex) { notify.Error(ex.Message); }
        finally { IsBusy = false; }
        await Load();
    }

    [RelayCommand]
    private async Task LinkFolder()
    {
        if (Project?.Id is not { } pid) return;
        var folder = dialogs.PickFolder("Pasta de um repositório Git existente");
        if (folder is not null) await Do(s => s.LinkAsync(pid, folder, null), "Repositório vinculado ao projeto.");
    }

    [RelayCommand]
    private async Task InitFolder()
    {
        if (Project?.Id is not { } pid) return;
        var folder = dialogs.PickFolder("Pasta onde criar o repositório (git init)");
        if (folder is not null) await Do(s => s.InitAsync(pid, folder, null), "Repositório criado e vinculado. Próximo passo: o primeiro commit em Commit Manager.");
    }

    [RelayCommand]
    private async Task Clone()
    {
        if (Project?.Id is not { } pid) return;
        if (!GitService.IsAllowedRemoteUrl(CloneUrl)) { notify.Warning("Informe um endereço https://… ou git@host:dono/repo.git."); return; }
        var parent = dialogs.PickFolder("Pasta onde a cópia será criada");
        if (parent is null) return;
        var name = CloneUrl.TrimEnd('/').Split('/', ':').Last();
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var target = Path.Combine(parent, name);
        await Do(s => s.CloneAsync(pid, CloneUrl, target, null), $"Repositório clonado em {target}.");
        CloneUrl = "";
    }

    [RelayCommand]
    private async Task CreateOnGitHub()
    {
        if (Selected is null) return;
        if (!dialogs.Confirm("Criar repositório no GitHub",
                $"Criar o repositório {(GithubPrivate ? "PRIVADO" : "PÚBLICO")} \"{GithubName}\" na sua conta do GitHub e configurá-lo como destino (origin) de \"{Selected.Name}\"?\n\nNada será enviado agora; o envio é feito em Remote Sync.",
                "Criar")) return;
        IsBusy = true;
        try
        {
            var id = Selected.Id;
            var r = await runner.RunAsync<GitService, OperationResult<GitHubRepoInfo>>(s => s.CreateGitHubRepoAsync(id, GithubName.Trim(), GithubPrivate, null, GithubDescription));
            if (r.Success) notify.Success($"Criado: {r.Value!.Url}. Para enviar os commits, use Remote Sync › Enviar.");
            else notify.Error(r.Error!);
        }
        finally { IsBusy = false; }
        await Load();
    }

    [RelayCommand]
    private async Task Unlink()
    {
        if (Selected is null) return;
        if (!dialogs.Confirm("Desvincular repositório", $"Desvincular \"{Selected.Name}\" deste projeto?\n\nA pasta {Selected.LocalPath} e o histórico Git NÃO são apagados.", "Desvincular")) return;
        var id = Selected.Id;
        var r = await runner.RunAsync<GitService, OperationResult>(s => s.UnlinkAsync(id));
        if (r.Success) notify.Success("Repositório desvinculado; nenhum arquivo foi apagado."); else notify.Error(r.Error!);
        await Load();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Selected is not null && Directory.Exists(Selected.LocalPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Selected.LocalPath}\"") { UseShellExecute = true });
    }

    [RelayCommand] private Task OpenCommit() => Selected is null ? Task.CompletedTask : navigator.NavigateAsync(NavigationCatalog.GitCommit, Selected.Id);
}

// =====================================================================================
// Commit Manager
// =====================================================================================

public sealed partial class ChangeRow(GitFileChange change) : ObservableObject
{
    public GitFileChange Change { get; } = change;
    public string Path => Change.OldPath is null ? Change.Path : $"{Change.OldPath} → {Change.Path}";
    public string Kind => Change.Kind switch
    {
        GitChangeKind.Added => "Novo (adicionado)",
        GitChangeKind.Deleted => "Excluído",
        GitChangeKind.Renamed => "Renomeado",
        GitChangeKind.Copied => "Copiado",
        GitChangeKind.Untracked => "Novo (não acompanhado)",
        GitChangeKind.Conflict => "CONFLITO",
        GitChangeKind.TypeChanged => "Tipo alterado",
        _ => "Modificado",
    };
    public string Selection => Change.Staged && Change.Unstaged ? "Parcial" : Change.Staged ? "Sim" : "Não";
    [ObservableProperty] private bool _isChecked;
}

public sealed partial class CommitManagerViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs, ViewModeService mode)
    : GitRepoPageBase(runner, notify, mode), IPage
{
    public string Title => "Commit Manager";
    public override string Explanation =>
        "Um commit é uma \"foto\" das alterações com uma descrição. Marque os arquivos que entram na foto, clique em Selecionar para o commit, " +
        "escreva o que mudou e clique em Fazer commit. O REA procura senhas e chaves antes de registrar.";
    public ObservableCollection<ChangeRow> Changes { get; } = [];
    [ObservableProperty] private ChangeRow? _selectedChange;
    [ObservableProperty] private string _diff = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _branchText = "";

    protected override Task LoadAsync(Guid repoId, GitStatus status)
    {
        Changes.Clear();
        foreach (var c in status.Changes.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase)) Changes.Add(new ChangeRow(c));
        Diff = "";
        BranchText = status.Changes.Count == 0 ? "Nenhuma alteração desde o último commit." : $"{status.Changes.Count} arquivo(s) alterado(s); {status.Changes.Count(c => c.Staged)} selecionado(s) para o commit.";
        return Task.CompletedTask;
    }

    async partial void OnSelectedChangeChanged(ChangeRow? value)
    {
        if (value is null || SelectedRepo is null) { Diff = ""; return; }
        var id = SelectedRepo.Id;
        var r = await Runner.RunAsync<GitService, OperationResult<string>>(s => s.DiffAsync(id, value.Change));
        Diff = r.Success ? r.Value! : r.Error!;
    }

    private List<string> Checked() => Changes.Where(c => c.IsChecked).Select(c => c.Change.Path).ToList();

    [RelayCommand]
    private void CheckAll() { var all = Changes.Any(c => !c.IsChecked); foreach (var c in Changes) c.IsChecked = all; }

    [RelayCommand]
    private Task Stage()
    {
        var paths = Checked();
        if (paths.Count == 0) { Notify.Warning("Marque os arquivos na lista primeiro."); return Task.CompletedTask; }
        var id = SelectedRepo!.Id;
        return RunOpAsync(s => s.StageAsync(id, paths), $"{paths.Count} arquivo(s) selecionado(s) para o commit.");
    }

    [RelayCommand]
    private Task Unstage()
    {
        var paths = Checked();
        if (paths.Count == 0) { Notify.Warning("Marque os arquivos na lista primeiro."); return Task.CompletedTask; }
        var id = SelectedRepo!.Id;
        return RunOpAsync(s => s.UnstageAsync(id, paths), $"{paths.Count} arquivo(s) retirado(s) da seleção (as alterações continuam nos arquivos).");
    }

    [RelayCommand]
    private async Task Commit()
    {
        if (SelectedRepo is null) return;
        if (Message.Trim().Length == 0) { Notify.Warning("Escreva uma mensagem descrevendo o que mudou."); return; }
        var id = SelectedRepo.Id;
        var scan = await Runner.RunAsync<GitService, OperationResult<IReadOnlyList<SecretFinding>>>(s => s.ScanStagedAsync(id));
        var confirmed = false;
        if (scan.Success && scan.Value!.Count > 0)
        {
            var list = string.Join("\n", scan.Value.Take(15).Select(f => $"• {f.Path}{(f.Line is { } l ? $" (linha {l})" : "")}: {f.Rule} — {f.Excerpt}"));
            if (!dialogs.Confirm("Possíveis segredos no commit",
                    $"O REA encontrou itens que parecem senhas, chaves ou arquivos de credenciais:\n\n{list}\n\n" +
                    "Se forem segredos de verdade, cancele, remova-os e tire esses arquivos da seleção. Commitar mesmo assim?",
                    "Commitar mesmo assim")) return;
            confirmed = true;
        }
        var message = Message;
        IsBusy = true;
        try
        {
            var r = await Runner.RunAsync<GitService, OperationResult<string>>(s => s.CommitAsync(id, message, confirmed));
            if (r.Success) { var hash = r.Value ?? ""; Notify.Success($"Commit {hash[..Math.Min(8, hash.Length)]} registrado."); Message = ""; }
            else Notify.Error(r.Error!);
        }
        finally { IsBusy = false; }
        await RefreshAsync();
    }

    [RelayCommand]
    private Task Stash()
    {
        var id = SelectedRepo!.Id;
        return RunOpAsync(s => s.StashAsync(id, Message.Length > 0 ? Message : null), "Alterações guardadas (stash). Os arquivos voltaram ao último commit.");
    }

    [RelayCommand]
    private Task StashPop()
    {
        var id = SelectedRepo!.Id;
        return RunOpAsync(s => s.StashPopAsync(id), "Alterações guardadas foram reaplicadas.");
    }
}

// =====================================================================================
// Branch Manager
// =====================================================================================

public sealed record BranchRow(GitBranchInfo Info)
{
    public string Name => Info.Name;
    public string Current => Info.IsCurrent ? "Ativo" : "";
    public string Where => Info.IsRemote ? "Remoto" : "Local";
    public string Tracking => Info.Upstream is null ? "—" : Info.UpstreamGone ? $"{Info.Upstream} (apagado no remoto)" :
        Info.Ahead + Info.Behind == 0 ? $"{Info.Upstream} (em dia)" : $"{Info.Upstream} ({Info.Ahead} à frente, {Info.Behind} atrás)";
    public string Last => $"{Info.LastCommit} · {Info.LastCommitDate?.ToLocalTime():dd/MM/yyyy HH:mm} · {Info.LastSubject}";
}

public sealed partial class BranchManagerViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs, ViewModeService mode)
    : GitRepoPageBase(runner, notify, mode), IPage
{
    public string Title => "Branch Manager";
    public override string Explanation =>
        "Um branch é uma linha de trabalho separada: dá para testar uma ideia sem mexer na versão principal e depois juntar (merge). " +
        "O REA não deixa excluir um branch com trabalho que só existe nele sem uma confirmação extra.";
    public ObservableCollection<BranchRow> Branches { get; } = [];
    [ObservableProperty] private BranchRow? _selectedBranch;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private bool _mergeInProgress;

    protected override async Task LoadAsync(Guid repoId, GitStatus status)
    {
        MergeInProgress = status.OperationInProgress == "Merge";
        Branches.Clear();
        var r = await Runner.RunAsync<GitService, OperationResult<IReadOnlyList<GitBranchInfo>>>(s => s.BranchesAsync(repoId));
        if (r.Success) foreach (var b in r.Value!.OrderBy(b => b.IsRemote).ThenByDescending(b => b.IsCurrent).ThenBy(b => b.Name)) Branches.Add(new BranchRow(b));
    }

    [RelayCommand]
    private Task Create()
    {
        var name = NewName.Trim();
        if (name.Length == 0) { Notify.Warning("Digite o nome do novo branch (ex.: recurso/login)."); return Task.CompletedTask; }
        var id = SelectedRepo!.Id;
        NewName = "";
        return RunOpAsync(s => s.CreateBranchAsync(id, name, switchTo: true), $"Branch \"{name}\" criado e ativado.");
    }

    [RelayCommand]
    private Task Switch()
    {
        if (SelectedBranch is null || SelectedBranch.Info.IsRemote) { Notify.Warning("Escolha um branch local na lista."); return Task.CompletedTask; }
        var id = SelectedRepo!.Id; var name = SelectedBranch.Name;
        return RunOpAsync(s => s.SwitchBranchAsync(id, name), $"Agora você está no branch \"{name}\".");
    }

    [RelayCommand]
    private Task Rename()
    {
        if (SelectedBranch is null || SelectedBranch.Info.IsRemote) { Notify.Warning("Escolha um branch local na lista."); return Task.CompletedTask; }
        var newName = dialogs.Prompt("Renomear branch", $"Novo nome para \"{SelectedBranch.Name}\":", SelectedBranch.Name, "Renomear");
        if (string.IsNullOrWhiteSpace(newName) || newName == SelectedBranch.Name) return Task.CompletedTask;
        var id = SelectedRepo!.Id; var old = SelectedBranch.Name;
        return RunOpAsync(s => s.RenameBranchAsync(id, old, newName.Trim()), $"Branch renomeado para \"{newName.Trim()}\".");
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedBranch is null || SelectedBranch.Info.IsRemote) { Notify.Warning("Escolha um branch local na lista."); return; }
        var id = SelectedRepo!.Id; var name = SelectedBranch.Name;
        if (!dialogs.Confirm("Excluir branch", $"Excluir o branch local \"{name}\"?", "Excluir")) return;
        var r = await Runner.RunAsync<GitService, OperationResult>(s => s.DeleteBranchAsync(id, name, confirmUnmerged: false));
        if (!r.Success && r.Error!.Contains("perderia", StringComparison.Ordinal))
        {
            if (!dialogs.Confirm("Trabalho seria perdido",
                    $"O branch \"{name}\" tem commits que não estão em nenhum outro branch. Excluir agora APAGA esse trabalho.\n\nTem certeza?",
                    "Excluir e perder os commits")) return;
            r = await Runner.RunAsync<GitService, OperationResult>(s => s.DeleteBranchAsync(id, name, confirmUnmerged: true));
        }
        if (r.Success) Notify.Success($"Branch \"{name}\" excluído."); else Notify.Error(r.Error!);
        await RefreshAsync();
    }

    [RelayCommand]
    private Task Merge()
    {
        if (SelectedBranch is null || SelectedBranch.Info.IsCurrent) { Notify.Warning("Escolha na lista o branch que será juntado ao ativo."); return Task.CompletedTask; }
        var current = Status?.Branch ?? "ativo";
        if (!dialogs.Confirm("Merge", $"Juntar as alterações de \"{SelectedBranch.Name}\" no branch \"{current}\"?", "Fazer merge")) return Task.CompletedTask;
        var id = SelectedRepo!.Id; var name = SelectedBranch.Name;
        return RunOpAsync(s => s.MergeAsync(id, name), $"Merge de \"{name}\" concluído.");
    }

    [RelayCommand]
    private Task AbortMerge()
    {
        if (!dialogs.Confirm("Cancelar merge", "Cancelar o merge em andamento e voltar os arquivos ao estado anterior a ele?", "Cancelar merge")) return Task.CompletedTask;
        var id = SelectedRepo!.Id;
        return RunOpAsync(s => s.AbortMergeAsync(id), "Merge cancelado.");
    }
}

// =====================================================================================
// Remote Sync
// =====================================================================================

public sealed record CommitRow(GitCommitInfo Info)
{
    public string Hash => Info.ShortHash;
    public string Subject => Info.Subject;
    public string Author => Info.Author;
    public string Date => Info.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
    public string Refs => Info.Refs;
}

public sealed partial class RemoteSyncViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs, ViewModeService mode)
    : GitRepoPageBase(runner, notify, mode), IPage
{
    public string Title => "Remote Sync";
    public override string Explanation =>
        "Buscar (fetch) só olha o que há de novo no servidor. Receber (pull) traz esses commits para a sua pasta. " +
        "Enviar (push) manda os seus commits para o servidor. O REA nunca força um envio que apagaria trabalho de outra pessoa.";
    public ObservableCollection<CommitRow> ToSend { get; } = [];
    public ObservableCollection<CommitRow> ToReceive { get; } = [];
    [ObservableProperty] private string _remoteText = "";

    protected override async Task LoadAsync(Guid repoId, GitStatus status)
    {
        ToSend.Clear(); ToReceive.Clear();
        RemoteText = status.Upstream is null ? "Este branch ainda não está ligado a um remoto. O primeiro envio cria essa ligação." : $"Remoto acompanhado: {status.Upstream}";
        var r = await Runner.RunAsync<GitService, OperationResult<(IReadOnlyList<GitCommitInfo>, IReadOnlyList<GitCommitInfo>)>>(s => s.PendingAsync(repoId));
        if (!r.Success) return;
        foreach (var c in r.Value.Item1) ToSend.Add(new CommitRow(c));
        foreach (var c in r.Value.Item2) ToReceive.Add(new CommitRow(c));
    }

    [RelayCommand] private Task Fetch() { var id = SelectedRepo!.Id; return RunOpAsync(s => s.FetchAsync(id), "Informações do remoto atualizadas."); }

    [RelayCommand]
    private Task Pull()
    {
        var id = SelectedRepo!.Id;
        return RunOpAsync(s => s.PullAsync(id), "Commits do remoto recebidos.");
    }

    [RelayCommand]
    private Task Push()
    {
        if (Status is null) return Task.CompletedTask;
        var summary = Status.Upstream is null
            ? $"Enviar o branch \"{Status.Branch}\" pela primeira vez e ligá-lo ao remoto?"
            : $"Enviar {ToSend.Count} commit(s) do branch \"{Status.Branch}\" para {Status.Upstream}?\n\n" +
              string.Join("\n", ToSend.Take(10).Select(c => $"• {c.Hash} {c.Subject}"));
        if (!dialogs.Confirm("Enviar para o remoto", summary + "\n\nO envio nunca é forçado.", "Enviar")) return Task.CompletedTask;
        var id = SelectedRepo!.Id;
        return RunOpAsync(s => s.PushAsync(id), "Commits enviados.");
    }
}

// =====================================================================================
// Git History
// =====================================================================================

public sealed partial class GitHistoryViewModel(ScopedRunner runner, NotificationService notify, ViewModeService mode)
    : GitRepoPageBase(runner, notify, mode), IPage
{
    public string Title => "Git History";
    public override string Explanation => "Lista dos commits do branch ativo, do mais recente para o mais antigo. Clique em um para ver o que ele mudou.";
    public ObservableCollection<CommitRow> Commits { get; } = [];
    [ObservableProperty] private CommitRow? _selectedCommit;
    [ObservableProperty] private string _details = "";

    protected override async Task LoadAsync(Guid repoId, GitStatus status)
    {
        Commits.Clear();
        Details = "";
        var r = await Runner.RunAsync<GitService, OperationResult<IReadOnlyList<GitCommitInfo>>>(s => s.LogAsync(repoId, 500));
        if (r.Success) foreach (var c in r.Value!) Commits.Add(new CommitRow(c));
    }

    async partial void OnSelectedCommitChanged(CommitRow? value)
    {
        if (value is null || SelectedRepo is null) { Details = ""; return; }
        var id = SelectedRepo.Id;
        var r = await Runner.RunAsync<GitService, OperationResult<string>>(s => s.ShowAsync(id, value.Info.Hash));
        Details = r.Success ? r.Value! : r.Error!;
    }
}
