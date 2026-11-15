using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using REA.Core;
using REA.Domain;
using REA.Desktop.Services;

namespace REA.Desktop.ViewModels;

public sealed record KindOption(EnvironmentKind Kind, string Label);

public sealed class EnvironmentRow(ResearchEnvironment e)
{
    public ResearchEnvironment Entity { get; } = e;
    public string Name => Entity.Name;
    public string KindText => EnvironmentService.KindText(Entity.Kind);
    public string ClientVersion => Entity.ClientVersion ?? "—";
    public string ClientDirectory => Entity.ClientDirectory ?? "—";
    public string Updated => Entity.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

public sealed partial class EnvironmentsViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs)
    : ObservableObject, IPage
{
    public string Title => "Ambientes";

    public ObservableCollection<ProjectSummary> Projects { get; } = [];
    public ObservableCollection<EnvironmentRow> Items { get; } = [];
    public IReadOnlyList<KindOption> Kinds { get; } =
        Enum.GetValues<EnvironmentKind>().Select(k => new KindOption(k, EnvironmentService.KindText(k))).ToList();

    [ObservableProperty] private ProjectSummary? _selectedProject;
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private Guid? _editingId;
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private KindOption? _formKind;
    [ObservableProperty] private string _formVersion = "";
    [ObservableProperty] private string _formDirectory = "";
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private string _formNotes = "";
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private bool _isFormOpen;

    public bool HasProjects => Projects.Count > 0;
    public bool IsEmpty => SelectedProject is not null && !IsLoading && Items.Count == 0;
    public bool CanAdd => SelectedProject is { Status: ProjectStatus.Active };
    public string FormTitle => EditingId is null ? "Novo ambiente" : "Editar ambiente";

    partial void OnEditingIdChanged(Guid? value) => OnPropertyChanged(nameof(FormTitle));

    public async Task OnNavigatedToAsync(object? parameter)
    {
        // Lista até 500 projetos (máximo de página permitido); seletor com busca chega na Fase 4.
        var page = await runner.RunAsync<ProjectService, PagedResult<ProjectSummary>>(
            s => s.ListAsync(new ProjectQuery(null, null, new PageRequest(1, 500))));
        Projects.Clear();
        foreach (var p in page.Items.OrderBy(p => p.Name)) Projects.Add(p);
        OnPropertyChanged(nameof(HasProjects));
        SelectedProject = parameter is Guid id ? Projects.FirstOrDefault(p => p.Id == id) : Projects.FirstOrDefault();
    }

    partial void OnSelectedProjectChanged(ProjectSummary? value)
    {
        IsFormOpen = false;
        OnPropertyChanged(nameof(CanAdd));
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        Items.Clear();
        if (SelectedProject is null) { OnPropertyChanged(nameof(IsEmpty)); return; }
        IsLoading = true;
        try
        {
            var projectId = SelectedProject.Id;
            var list = await runner.RunAsync<EnvironmentService, IReadOnlyList<ResearchEnvironment>>(s => s.ListAsync(projectId));
            foreach (var e in list) Items.Add(new EnvironmentRow(e));
        }
        catch (Exception ex)
        {
            notify.Error($"Não foi possível carregar os ambientes: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private void OpenNew()
    {
        EditingId = null;
        FormName = FormVersion = FormDirectory = FormDescription = FormNotes = "";
        FormKind = Kinds.First(k => k.Kind == EnvironmentKind.LocalLaboratory);
        FormError = null;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void Edit(EnvironmentRow row)
    {
        var e = row.Entity;
        EditingId = e.Id;
        FormName = e.Name;
        FormKind = Kinds.First(k => k.Kind == e.Kind);
        FormVersion = e.ClientVersion ?? "";
        FormDirectory = e.ClientDirectory ?? "";
        FormDescription = e.Description ?? "";
        FormNotes = e.Notes ?? "";
        FormError = null;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CloseForm() => IsFormOpen = false;

    [RelayCommand]
    private async Task Save()
    {
        if (SelectedProject is null) return;
        var projectId = SelectedProject.Id;
        var input = new EnvironmentInput(FormName, FormKind?.Kind ?? EnvironmentKind.Custom,
            FormDescription, FormVersion, FormDirectory, FormNotes);
        OperationResult result = EditingId is { } id
            ? await runner.RunAsync<EnvironmentService, OperationResult>(s => s.UpdateAsync(projectId, id, input))
            : await runner.RunAsync<EnvironmentService, OperationResult<Guid>>(s => s.CreateAsync(projectId, input));
        if (!result.Success) { FormError = result.Error; return; }
        notify.Success(EditingId is null ? $"Ambiente \"{FormName.Trim()}\" criado." : "Ambiente atualizado.");
        EditingId = null;
        IsFormOpen = false;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Delete(EnvironmentRow row)
    {
        if (SelectedProject is null) return;
        if (!dialogs.Confirm("Excluir ambiente", $"Excluir o ambiente \"{row.Name}\"? Esta ação não pode ser desfeita.", "Excluir"))
            return;
        var projectId = SelectedProject.Id;
        var result = await runner.RunAsync<EnvironmentService, OperationResult>(s => s.DeleteAsync(projectId, row.Entity.Id));
        if (result.Success) notify.Success($"Ambiente \"{row.Name}\" excluído.");
        else notify.Error(result.Error!);
        await LoadAsync();
    }
}
