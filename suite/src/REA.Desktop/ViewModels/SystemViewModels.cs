using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using REA.Core;
using REA.Domain;
using REA.Desktop.Services;
using REA.Infrastructure;

namespace REA.Desktop.ViewModels;

public sealed class ActivityRow(ActivityEntry a)
{
    public string When { get; } = a.Timestamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public string Operator { get; } = a.Operator;
    public string Action { get; } = a.Action;
    public string Summary { get; } = a.Summary;
    public string? Before { get; } = a.Before;
    public string? After { get; } = a.After;
    public string Details => string.Join("\n", new[]
    {
        a.Before is null ? null : $"Antes: {a.Before}",
        a.After is null ? null : $"Depois: {a.After}",
    }.Where(s => s is not null));
}

public sealed class ToolRow(ToolStatus t)
{
    public string Name { get; } = t.Name;
    public bool Available { get; } = t.Available;
    public string StatusText => Available ? "Disponível" : "Não encontrado";
    public string Version { get; } = t.Version ?? "—";
    public string Location { get; } = t.Location ?? "—";
    public string Requirement { get; } = t.Requirement;
}

public sealed partial class DashboardViewModel(ScopedRunner runner, NotificationService notify, INavigator navigator, IToolDetector toolDetector)
    : ObservableObject, IPage
{
    public string Title => "Dashboard";

    [ObservableProperty] private bool _isLoading;
    // Texto "…" até os dados chegarem: nunca exibir 0 enquanto carrega.
    [ObservableProperty] private string _activeProjects = "…";
    [ObservableProperty] private string _archivedProjects = "…";
    [ObservableProperty] private string _environments = "…";
    [ObservableProperty] private string _captures = "…";
    [ObservableProperty] private string _packetsAnalyzed = "…";
    [ObservableProperty] private string _tcpStreams = "…";
    [ObservableProperty] private string _modules = "…";
    [ObservableProperty] private string _toolsSummary = "Detectando…";
    public ObservableCollection<ActivityRow> Activity { get; } = [];
    public ObservableCollection<ToolRow> Tools { get; } = [];
    public bool HasNoActivity => !IsLoading && Activity.Count == 0;

    public Task OnNavigatedToAsync(object? parameter) => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            // 1) contagens do banco (rápido)
            var s = await runner.RunAsync<DashboardService, DashboardSnapshot>(d => d.GetAsync(includeTools: false));
            ActiveProjects = s.ActiveProjects.ToString("N0");
            ArchivedProjects = s.ArchivedProjects.ToString("N0");
            Environments = s.Environments.ToString("N0");
            Captures = s.Captures.ToString("N0");
            PacketsAnalyzed = s.PacketsAnalyzed.ToString("N0");
            TcpStreams = s.TcpStreams.ToString("N0");
            Modules = $"{s.ImplementedModules} de {s.TotalModules}";
            Activity.Clear();
            foreach (var a in s.RecentActivity) Activity.Add(new ActivityRow(a));
            OnPropertyChanged(nameof(HasNoActivity));

            // 2) ferramentas externas (executa processos; pode levar alguns segundos)
            ToolsSummary = "Detectando…";
            var tools = await toolDetector.DetectAsync();
            ToolsSummary = $"{tools.Count(t => t.Available)} de {tools.Count} disponíveis";
            Tools.Clear();
            foreach (var t in tools) Tools.Add(new ToolRow(t));
        }
        catch (Exception ex)
        {
            notify.Error($"Não foi possível carregar o dashboard: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoActivity));
        }
    }

    [RelayCommand]
    private Task NewProject() => navigator.NavigateAsync(NavigationCatalog.ProjectsNew);
}

public sealed partial class ActivityViewModel(IActivityLogReader reader, NotificationService notify) : ObservableObject, IPage
{
    public string Title => "Atividades";
    public ObservableCollection<ActivityRow> Items { get; } = [];
    public IReadOnlyList<int> PageSizes => PageRequest.AllowedSizes;
    [ObservableProperty] private int _take = PageRequest.DefaultSize;
    [ObservableProperty] private bool _isLoading;
    public bool IsEmpty => !IsLoading && Items.Count == 0;

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();
    partial void OnTakeChanged(int value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var list = await reader.RecentAsync(Take);
            Items.Clear();
            foreach (var a in list) Items.Add(new ActivityRow(a));
        }
        catch (Exception ex) { notify.Error($"Não foi possível carregar as atividades: {ex.Message}"); }
        finally { IsLoading = false; OnPropertyChanged(nameof(IsEmpty)); }
    }
}

/// <summary>Leitura da auditoria num escopo próprio (o IActivityLog é scoped).</summary>
public interface IActivityLogReader
{
    Task<IReadOnlyList<ActivityEntry>> RecentAsync(int take);
}

public sealed class ActivityLogReader(ScopedRunner runner) : IActivityLogReader
{
    public Task<IReadOnlyList<ActivityEntry>> RecentAsync(int take) =>
        runner.RunAsync<IActivityLog, IReadOnlyList<ActivityEntry>>(a => a.RecentAsync(take, null));
}

public sealed partial class DatabaseViewModel(ScopedRunner runner, ReaPaths paths, NotificationService notify)
    : ObservableObject, IPage
{
    public string Title => "Banco de dados";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _size = "";
    [ObservableProperty] private string _dataRoot = "";
    public ObservableCollection<string> Applied { get; } = [];
    public ObservableCollection<string> Pending { get; } = [];
    public bool HasPending => Pending.Count > 0;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        try
        {
            var info = await runner.RunAsync<IDatabaseInfoProvider, DatabaseInfo>(p => p.GetAsync());
            Path = info.Path;
            Size = Format.Bytes(info.SizeBytes);
            DataRoot = paths.Root;
            Applied.Clear(); foreach (var m in info.AppliedMigrations) Applied.Add(m);
            Pending.Clear(); foreach (var m in info.PendingMigrations) Pending.Add(m);
            OnPropertyChanged(nameof(HasPending));
        }
        catch (Exception ex) { notify.Error($"Não foi possível ler o banco: {ex.Message}"); }
    }

    [RelayCommand]
    private void OpenFolder() => Shell.OpenFolder(paths.Root);
}

public sealed partial class ToolsViewModel(IToolDetector detector, NotificationService notify) : ObservableObject, IPage
{
    public string Title => "Ferramentas";
    public ObservableCollection<ToolRow> Items { get; } = [];
    [ObservableProperty] private bool _isLoading;

    public Task OnNavigatedToAsync(object? parameter) => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var list = await detector.DetectAsync();
            Items.Clear();
            foreach (var t in list) Items.Add(new ToolRow(t));
        }
        catch (Exception ex) { notify.Error($"Falha na detecção de ferramentas: {ex.Message}"); }
        finally { IsLoading = false; }
    }
}

public sealed class BackupRow(BackupInfo b)
{
    public BackupInfo Info { get; } = b;
    public string FileName => Info.FileName;
    public string When => Info.CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");
    public string Reason => Info.Reason;
    public string Size => Format.Bytes(Info.SizeBytes);
}

public sealed partial class BackupViewModel(IBackupService backups, ReaPaths paths, NotificationService notify, IDialogService dialogs)
    : ObservableObject, IPage
{
    public string Title => "Backup";
    public ObservableCollection<BackupRow> Items { get; } = [];
    public string BackupFolder => paths.BackupDirectory;
    public bool IsEmpty => Items.Count == 0;

    public Task OnNavigatedToAsync(object? parameter) => LoadAsync();

    private async Task LoadAsync()
    {
        var list = await backups.ListAsync();
        Items.Clear();
        foreach (var b in list) Items.Add(new BackupRow(b));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task Create()
    {
        try
        {
            var b = await backups.CreateAsync("manual");
            notify.Success($"Backup criado: {b.FileName}");
        }
        catch (Exception ex) { notify.Error($"Falha ao criar backup: {ex.Message}"); }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Restore(BackupRow row)
    {
        if (!dialogs.Confirm("Restaurar backup",
                $"Restaurar o banco para o estado de {row.When}? Os dados atuais serão substituídos. " +
                "Um backup de segurança do estado atual será criado antes.", "Restaurar"))
            return;
        var result = await backups.RestoreAsync(row.FileName);
        if (result.Success) notify.Success("Backup restaurado. Os dados exibidos já refletem o backup.");
        else notify.Error(result.Error!);
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenFolder() => Shell.OpenFolder(paths.BackupDirectory);
}

internal static class Format
{
    public static string Bytes(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.0} KB",
        _ => $"{b / 1024.0 / 1024.0:0.0} MB",
    };
}

internal static class Shell
{
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
