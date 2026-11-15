using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using REA.Core;
using REA.Desktop.Services;

namespace REA.Desktop.ViewModels;

/// <summary>Página navegável. Carrega seus dados ao ser aberta.</summary>
public interface IPage
{
    string Title { get; }
    Task OnNavigatedToAsync(object? parameter);
}

public sealed partial class NavEntry(NavItem? item, string? header) : ObservableObject
{
    public NavItem? Item { get; } = item;
    public string? Header { get; } = header;
    public bool IsHeader => Header is not null;
    public string Title => Item?.Title ?? Header ?? "";
    public bool IsPlanned => Item is { Implemented: false };
    public string Badge => Item is { Implemented: false } i ? $"F{i.Phase}" : "";

    [ObservableProperty] private bool _isSelected;
}

public interface INavigator
{
    Task NavigateAsync(string key, object? parameter = null);
}

public sealed partial class ShellViewModel : ObservableObject, INavigator
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ShellViewModel> _logger;

    public ObservableCollection<NavEntry> Entries { get; } = [];
    public NotificationService Notifications { get; }
    public ViewModeService Mode { get; }

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private string _currentSection = "";

    public ShellViewModel(IServiceProvider services, NotificationService notifications, ViewModeService mode, ILogger<ShellViewModel> logger)
    {
        Mode = mode;
        _services = services;
        _logger = logger;
        Notifications = notifications;
        foreach (var section in NavigationCatalog.Sections)
        {
            Entries.Add(new NavEntry(null, section.Title));
            foreach (var item in section.Items) Entries.Add(new NavEntry(item, null));
        }
    }

    [RelayCommand]
    private Task Select(NavEntry entry) => entry.Item is null ? Task.CompletedTask : NavigateAsync(entry.Item.Key);

    public async Task NavigateAsync(string key, object? parameter = null)
    {
        var item = NavigationCatalog.Find(key);
        if (item is null) return;

        foreach (var e in Entries) e.IsSelected = e.Item?.Key == key;
        CurrentTitle = item.Title;
        CurrentSection = NavigationCatalog.Sections.First(s => s.Items.Contains(item)).Title;

        IPage page = item.Implemented ? CreatePage(key) : new ModuleStatusViewModel(item);
        CurrentPage = page;
        try
        {
            await page.OnNavigatedToAsync(key == NavigationCatalog.ProjectsNew ? "new" : parameter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao abrir {Page}", key);
            Notifications.Error($"Não foi possível carregar \"{item.Title}\": {ex.Message}");
        }
    }

    private IPage CreatePage(string key) => key switch
    {
        NavigationCatalog.Dashboard => _services.GetRequiredService<DashboardViewModel>(),
        NavigationCatalog.ProjectsList or NavigationCatalog.ProjectsNew => _services.GetRequiredService<ProjectsViewModel>(),
        NavigationCatalog.ProjectsEnvironments => _services.GetRequiredService<EnvironmentsViewModel>(),
        NavigationCatalog.ActivityLog => _services.GetRequiredService<ActivityViewModel>(),
        NavigationCatalog.SettingsDatabase => _services.GetRequiredService<DatabaseViewModel>(),
        NavigationCatalog.SettingsTools => _services.GetRequiredService<ToolsViewModel>(),
        NavigationCatalog.SettingsBackup => _services.GetRequiredService<BackupViewModel>(),
        NavigationCatalog.ResearchCapture => _services.GetRequiredService<NetworkCaptureViewModel>(),
        NavigationCatalog.ResearchPackets => _services.GetRequiredService<PacketInspectorViewModel>(),
        NavigationCatalog.ResearchTcp => _services.GetRequiredService<TcpStreamsViewModel>(),
        NavigationCatalog.EvidenceCaptures => _services.GetRequiredService<CapturesViewModel>(),
        NavigationCatalog.EvidenceFiles => _services.GetRequiredService<FilesViewModel>(),
        NavigationCatalog.ResearchBinary => _services.GetRequiredService<BinaryAnalysisViewModel>(),
        NavigationCatalog.ResearchFiles => _services.GetRequiredService<FileAnalysisViewModel>(),
        NavigationCatalog.ResearchGhidra => _services.GetRequiredService<GhidraViewModel>(),
        NavigationCatalog.ResearchKali => _services.GetRequiredService<KaliViewModel>(),
        NavigationCatalog.ResearchProtobuf => _services.GetRequiredService<ProtobufViewModel>(),
        NavigationCatalog.ResearchProtocol => _services.GetRequiredService<ProtocolExplorerViewModel>(),
        NavigationCatalog.ResearchHttp => _services.GetRequiredService<HttpViewModel>(),
        NavigationCatalog.SettingsIntegrations => _services.GetRequiredService<IntegrationsViewModel>(),
        _ => throw new InvalidOperationException($"Página implementada sem tela: {key}"),
    };
}

public sealed class ModuleStatusViewModel(NavItem item) : IPage
{
    public NavItem Item { get; } = item;
    public string Title => Item.Title;
    public string PhaseText => $"Planejado para a Fase {Item.Phase}";
    public string Dependencies => Item.Dependencies ?? "Nenhuma dependência externa.";
    public Task OnNavigatedToAsync(object? parameter) => Task.CompletedTask;
}
