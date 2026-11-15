using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using REA.Core;
using REA.Domain;
using REA.Desktop.Services;
using REA.PacketAnalyzer;

namespace REA.Desktop.ViewModels;

public sealed record CaptureOption(Guid Id, string Label);

public sealed record ProjectFilterOption(Guid? Id, string Label);

public sealed class CaptureRow(CaptureSummary c)
{
    public CaptureSummary Summary { get; } = c;
    public Guid Id => Summary.Id;
    public string Name => Summary.Name;
    public string Project => Summary.ProjectName;
    public string Environment => Summary.EnvironmentName ?? "—";
    public string Source => Summary.Source == CaptureSource.Live ? "Ao vivo" : "Importada";
    public string Status => CaptureService.StatusText(Summary.Status) +
                            (Summary.AnalysisStatus == CaptureAnalysisStatus.Failed ? " · análise falhou" : "");
    public long Packets => Summary.PacketCount;
    public string Size => Format.Bytes(Summary.FileSizeBytes);
    public int Streams => Summary.TcpStreamCount;
    public string Sha => Summary.Sha256 is { Length: > 12 } s ? s[..12] + "…" : Summary.Sha256 ?? "—";
    public string Created => Summary.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

/// <summary>Carrega as capturas mais recentes para os seletores do Packet Inspector e do TCP Stream Explorer.</summary>
internal static class CaptureOptions
{
    public static async Task<List<CaptureOption>> LoadAsync(ScopedRunner runner)
    {
        var page = await runner.RunAsync<CaptureService, PagedResult<CaptureSummary>>(
            s => s.ListAsync(new CaptureQuery(null, null, new PageRequest(1, 500))));
        return page.Items.Select(c => new CaptureOption(c.Id,
            $"{c.Name} · {c.ProjectName} · {c.PacketCount} pacotes · {c.CreatedAt.ToLocalTime():dd/MM HH:mm}")).ToList();
    }
}

public sealed partial class CapturesViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs, INavigator navigator)
    : ObservableObject, IPage
{
    public string Title => "Capturas";
    public ObservableCollection<CaptureRow> Items { get; } = [];
    public ObservableCollection<ProjectFilterOption> ProjectOptions { get; } = [];
    public ObservableCollection<EnvironmentOption> Environments { get; } = [];
    public IReadOnlyList<int> PageSizes => PageRequest.AllowedSizes;

    [ObservableProperty] private ProjectFilterOption? _projectFilter;
    [ObservableProperty] private EnvironmentOption? _importEnvironment;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private int _pageSize = PageRequest.DefaultSize;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _report;
    [ObservableProperty] private string? _reportTitle;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private CaptureRow? _selectedRow;
    public bool HasSelection => SelectedRow is not null;
    private bool _ready;

    public bool IsEmpty => !IsLoading && TotalCount == 0;
    public bool CanImport => ProjectFilter?.Id is not null;
    public string PageText => $"Página {Page} de {TotalPages} · {TotalCount} captura(s)";

    public async Task OnNavigatedToAsync(object? parameter)
    {
        var projects = await runner.RunAsync<ProjectService, PagedResult<ProjectSummary>>(
            s => s.ListAsync(new ProjectQuery(null, ProjectStatus.Active, new PageRequest(1, 500))));
        ProjectOptions.Clear();
        ProjectOptions.Add(new ProjectFilterOption(null, "Todos os projetos"));
        foreach (var p in projects.Items.OrderBy(p => p.Name)) ProjectOptions.Add(new ProjectFilterOption(p.Id, p.Name));
        ProjectFilter ??= ProjectOptions[0];
        _ready = true;
        await LoadAsync();
    }

    async partial void OnProjectFilterChanged(ProjectFilterOption? value)
    {
        OnPropertyChanged(nameof(CanImport));
        Environments.Clear();
        Environments.Add(new EnvironmentOption(null, "(sem ambiente)"));
        if (value?.Id is { } id)
            foreach (var e in await runner.RunAsync<EnvironmentService, IReadOnlyList<ResearchEnvironment>>(s => s.ListAsync(id)))
                Environments.Add(new EnvironmentOption(e.Id, e.Name));
        ImportEnvironment = Environments[0];
        if (_ready) { Page = 1; await LoadAsync(); }
    }

    partial void OnPageSizeChanged(int value) { if (_ready) { Page = 1; _ = LoadAsync(); } }

    [RelayCommand]
    private async Task ApplySearch() { Page = 1; await LoadAsync(); }

    [RelayCommand]
    private async Task NextPage() { if (Page < TotalPages) { Page++; await LoadAsync(); } }

    [RelayCommand]
    private async Task PreviousPage() { if (Page > 1) { Page--; await LoadAsync(); } }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var q = new CaptureQuery(ProjectFilter?.Id, Search, new PageRequest(Page, PageSize));
            var r = await runner.RunAsync<CaptureService, PagedResult<CaptureSummary>>(s => s.ListAsync(q));
            Items.Clear();
            foreach (var c in r.Items) Items.Add(new CaptureRow(c));
            TotalCount = r.TotalCount;
            TotalPages = r.TotalPages;
        }
        catch (Exception ex) { notify.Error($"Não foi possível carregar as capturas: {ex.Message}"); }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(PageText));
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (ProjectFilter?.Id is not { } projectId) { notify.Warning("Escolha um projeto no filtro antes de importar."); return; }
        var path = dialogs.OpenFile("Importar captura", "Capturas (*.pcapng;*.pcap;*.cap)|*.pcapng;*.pcap;*.cap|Todos os arquivos (*.*)|*.*");
        if (path is null) return;
        IsLoading = true;
        try
        {
            var input = new ImportCaptureInput(projectId, ImportEnvironment?.Id, path, null);
            var r = await runner.RunAsync<CaptureService, OperationResult<Guid>>(s => s.ImportAsync(input));
            if (r.Success) notify.Success($"Captura importada: {Path.GetFileName(path)}. O arquivo original não foi alterado.");
            else notify.Error(r.Error!);
        }
        finally { IsLoading = false; }
        await LoadAsync();
    }

    [RelayCommand]
    private Task Open(CaptureRow? row) => row is null ? Task.CompletedTask : navigator.NavigateAsync(NavigationCatalog.ResearchPackets, row.Id);

    [RelayCommand]
    private Task Streams(CaptureRow row) => navigator.NavigateAsync(NavigationCatalog.ResearchTcp, row.Id);

    [RelayCommand]
    private async Task Details(CaptureRow row)
    {
        var c = await runner.RunAsync<CaptureService, Capture?>(s => s.GetAsync(row.Id));
        if (c is null) { notify.Warning("Captura não encontrada."); return; }
        var sb = new StringBuilder();
        sb.AppendLine($"Nome: {c.Name}");
        sb.AppendLine($"Projeto: {c.Project?.Name} · Ambiente: {c.Environment?.Name ?? "—"}");
        sb.AppendLine($"Origem: {(c.Source == CaptureSource.Live ? "captura ao vivo" : "importação")} · Situação: {CaptureService.StatusText(c.Status)}");
        if (c.OriginalPath is not null) sb.AppendLine($"Arquivo de origem: {c.OriginalPath}");
        sb.AppendLine($"Formato: {c.FileFormat.ToUpperInvariant()} · Tamanho: {Format.Bytes(c.FileSizeBytes)}");
        sb.AppendLine($"SHA-256: {c.Sha256 ?? "—"}");
        if (c.InterfaceDescription is not null) sb.AppendLine($"Interface: {c.InterfaceDescription}");
        if (c.CaptureFilter is not null) sb.AppendLine($"Filtro BPF: {c.CaptureFilter}");
        if (c.StartedAt is not null) sb.AppendLine($"Início: {c.StartedAt.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss} · Fim: {c.EndedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "—"}");
        sb.AppendLine($"Pacotes: {c.PacketCount} · Bytes: {c.TotalBytes} · Fluxos TCP: {c.TcpStreamCount} · Descartados: {c.DroppedPackets}");
        if (c.FirstPacketAt is not null) sb.AppendLine($"Primeiro pacote: {c.FirstPacketAt.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss.fff} · Último: {c.LastPacketAt?.ToLocalTime():dd/MM/yyyy HH:mm:ss.fff}");
        if (c.ProtocolSummary is not null) sb.AppendLine($"Protocolos: {c.ProtocolSummary}");
        if (c.AnalysisError is not null) sb.AppendLine($"Observação da análise: {c.AnalysisError}");
        if (c.Notes is not null) sb.AppendLine($"Notas: {c.Notes}");
        ReportTitle = "Detalhes da captura";
        Report = sb.ToString();
    }

    [RelayCommand]
    private async Task CrossCheck(CaptureRow row)
    {
        var c = await runner.RunAsync<CaptureService, Capture?>(s => s.GetAsync(row.Id));
        if (c is null) return;
        string path = await runner.RunAsync<CaptureService, string>(s => Task.FromResult(s.ResolvePath(c)));
        ReportTitle = "Validação com TShark";
        Report = "Comparando com o TShark…";
        try
        {
            var r = await TsharkCrossCheck.RunAsync(path);
            var sb = new StringBuilder();
            sb.AppendLine($"Ferramenta independente: {r.TsharkVersion}");
            sb.AppendLine($"{(r.PacketsMatch ? "[OK]" : "[DIFERENTE]")} Pacotes — REA: {r.ReaPackets} · TShark: {r.TsharkPackets}");
            sb.AppendLine($"{(r.StreamsMatch ? "[OK]" : "[DIFERENTE]")} Fluxos TCP — REA: {r.ReaTcpStreams} · TShark: {r.TsharkTcpConversations}");
            foreach (var s in r.Streams)
                sb.AppendLine($"{(s.ClientToServerMatches && s.ServerToClientMatches ? "[OK]" : "[DIFERENTE]")} Fluxo {s.Index} {s.Endpoints} — " +
                              $"cliente→servidor {s.ReaClientBytes}/{s.TsharkClientBytes} B, servidor→cliente {s.ReaServerBytes}/{s.TsharkServerBytes} B");
            if (r.StreamsNotCompared > 0) sb.AppendLine($"{r.StreamsNotCompared} fluxo(s) além dos 20 primeiros não foram comparados.");
            sb.AppendLine(r.AllMatch ? "Resultado: leitura e reconstrução do REA conferem com o TShark." : "Resultado: há diferenças — veja as linhas marcadas.");
            Report = sb.ToString();
        }
        catch (Exception ex)
        {
            Report = $"Não foi possível validar: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Reanalyze(CaptureRow row)
    {
        var r = await runner.RunAsync<CaptureService, OperationResult>(s => s.ReanalyzeAsync(row.Id));
        if (r.Success) notify.Success("Estatísticas recalculadas.");
        else notify.Error(r.Error!);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Export(CaptureRow row)
    {
        var c = await runner.RunAsync<CaptureService, Capture?>(s => s.GetAsync(row.Id));
        if (c is null) return;
        var source = await runner.RunAsync<CaptureService, string>(s => Task.FromResult(s.ResolvePath(c)));
        var target = dialogs.SaveFile("Exportar cópia da captura", $"{Sanitize(c.Name)}.{c.FileFormat}",
            c.FileFormat == "pcapng" ? "PCAPNG (*.pcapng)|*.pcapng" : "PCAP (*.pcap)|*.pcap");
        if (target is null) return;
        try
        {
            File.Copy(source, target, overwrite: true);
            notify.Success($"Cópia exportada para {target}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notify.Error($"Não foi possível exportar: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ShowInFolder(CaptureRow row)
    {
        var c = await runner.RunAsync<CaptureService, Capture?>(s => s.GetAsync(row.Id));
        if (c is null) return;
        var path = await runner.RunAsync<CaptureService, string>(s => Task.FromResult(s.ResolvePath(c)));
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void CloseReport() => Report = null;

    private static string Sanitize(string name) => string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}
