using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using REA.Core;
using REA.Domain;
using REA.Desktop.Services;

namespace REA.Desktop.ViewModels;

public sealed record StatusFilterOption(string Label, ProjectStatus? Status);

public sealed class ProjectRow(ProjectSummary s)
{
    public ProjectSummary Summary { get; } = s;
    public Guid Id => Summary.Id;
    public string Name => Summary.Name;
    public string Game => Summary.GameName ?? "—";
    public string StatusText => ProjectService.StatusText(Summary.Status);
    public bool IsArchived => Summary.Status == ProjectStatus.Archived;
    public int EnvironmentCount => Summary.EnvironmentCount;
    public string Updated => Summary.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

public sealed partial class ProjectsViewModel(
    ScopedRunner runner, NotificationService notify, IDialogService dialogs, INavigator navigator)
    : ObservableObject, IPage
{
    public string Title => "Projetos";

    public ObservableCollection<ProjectRow> Items { get; } = [];
    public IReadOnlyList<int> PageSizes => PageRequest.AllowedSizes;
    public IReadOnlyList<StatusFilterOption> StatusOptions { get; } =
    [
        new("Ativos", ProjectStatus.Active),
        new("Arquivados", ProjectStatus.Archived),
        new("Todos", null),
    ];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private StatusFilterOption? _statusFilter;
    [ObservableProperty] private int _pageSize = PageRequest.DefaultSize;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isLoading;

    // Formulário (criar/editar). Só é limpo depois de salvar com sucesso.
    [ObservableProperty] private Guid? _editingId;
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private string _formGame = "";
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private bool _isFormOpen;

    public string FormTitle => EditingId is null ? "Novo projeto" : "Editar projeto";
    public string SaveLabel => EditingId is null ? "Criar projeto" : "Salvar alterações";
    public bool IsEmpty => !IsLoading && TotalCount == 0;
    public string PageText => $"Página {Page} de {TotalPages} · {TotalCount} projeto(s)";

    partial void OnEditingIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SaveLabel));
    }

    private bool _ready;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        StatusFilter ??= StatusOptions[0];
        _ready = true;
        if (parameter is "new") OpenNew();
        await LoadAsync();
    }

    partial void OnStatusFilterChanged(StatusFilterOption? value) { if (_ready) _ = ReloadFromFirstPage(); }
    partial void OnPageSizeChanged(int value) { if (_ready) _ = ReloadFromFirstPage(); }

    private Task ReloadFromFirstPage()
    {
        Page = 1;
        return LoadAsync();
    }

    [RelayCommand]
    private Task ApplySearch() => ReloadFromFirstPage();

    [RelayCommand]
    private async Task NextPage()
    {
        if (Page >= TotalPages) return;
        Page++;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task PreviousPage()
    {
        if (Page <= 1) return;
        Page--;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (StatusFilter is null) return;
        IsLoading = true;
        try
        {
            var query = new ProjectQuery(Search, StatusFilter.Status, new PageRequest(Page, PageSize));
            var result = await runner.RunAsync<ProjectService, PagedResult<ProjectSummary>>(s => s.ListAsync(query));
            Items.Clear();
            foreach (var p in result.Items) Items.Add(new ProjectRow(p));
            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            if (Page > TotalPages) { Page = TotalPages; await LoadAsync(); return; }
        }
        catch (Exception ex)
        {
            notify.Error($"Não foi possível carregar os projetos: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(PageText));
        }
    }

    [RelayCommand]
    private void OpenNew()
    {
        EditingId = null;
        FormName = FormGame = FormDescription = "";
        FormError = null;
        IsFormOpen = true;
    }

    [RelayCommand]
    private async Task Edit(ProjectRow row)
    {
        var project = await runner.RunAsync<ProjectService, ResearchProject?>(s => s.GetAsync(row.Id));
        if (project is null) { notify.Warning("Projeto não encontrado."); await LoadAsync(); return; }
        EditingId = project.Id;
        FormName = project.Name;
        FormGame = project.GameName ?? "";
        FormDescription = project.Description ?? "";
        FormError = null;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsFormOpen = false;
        FormError = null;
    }

    [RelayCommand]
    private async Task Save()
    {
        var input = new ProjectInput(FormName, FormGame, FormDescription);
        OperationResult result = EditingId is { } id
            ? await runner.RunAsync<ProjectService, OperationResult>(s => s.UpdateAsync(id, input))
            : await runner.RunAsync<ProjectService, OperationResult<Guid>>(s => s.CreateAsync(input));
        if (!result.Success) { FormError = result.Error; return; }

        notify.Success(EditingId is null ? $"Projeto \"{FormName.Trim()}\" criado." : "Alterações salvas.");
        EditingId = null;
        FormName = FormGame = FormDescription = "";
        FormError = null;
        IsFormOpen = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Duplicate(ProjectRow row)
    {
        var name = dialogs.Prompt("Duplicar projeto",
            $"Os ambientes de \"{row.Name}\" serão copiados. Evidências não são copiadas. Nome do novo projeto:",
            $"{row.Name} (cópia)", "Duplicar");
        if (name is null) return;
        var result = await runner.RunAsync<ProjectService, OperationResult<Guid>>(s => s.DuplicateAsync(row.Id, name));
        if (result.Success) notify.Success($"Projeto \"{name.Trim()}\" criado como cópia.");
        else notify.Error(result.Error!);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleArchive(ProjectRow row)
    {
        OperationResult result;
        if (row.IsArchived)
        {
            result = await runner.RunAsync<ProjectService, OperationResult>(s => s.RestoreAsync(row.Id));
            if (result.Success) notify.Success($"Projeto \"{row.Name}\" reativado.");
        }
        else
        {
            if (!dialogs.Confirm("Arquivar projeto",
                    $"Arquivar \"{row.Name}\"? Ele sai da lista de ativos, mas nada é apagado e pode ser reativado.", "Arquivar"))
                return;
            result = await runner.RunAsync<ProjectService, OperationResult>(s => s.ArchiveAsync(row.Id));
            if (result.Success) notify.Success($"Projeto \"{row.Name}\" arquivado.");
        }
        if (!result.Success) notify.Error(result.Error!);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Delete(ProjectRow row)
    {
        if (!dialogs.Confirm("Excluir projeto",
                $"Excluir \"{row.Name}\" definitivamente? Esta ação não pode ser desfeita. " +
                "Projetos com ambientes não podem ser excluídos, apenas arquivados.", "Excluir definitivamente"))
            return;
        var result = await runner.RunAsync<ProjectService, OperationResult>(s => s.DeleteAsync(row.Id));
        if (result.Success) notify.Success($"Projeto \"{row.Name}\" excluído.");
        else notify.Error(result.Error!);
        await LoadAsync();
    }

    [RelayCommand]
    private Task OpenEnvironments(ProjectRow row) => navigator.NavigateAsync(NavigationCatalog.ProjectsEnvironments, row.Id);
}
