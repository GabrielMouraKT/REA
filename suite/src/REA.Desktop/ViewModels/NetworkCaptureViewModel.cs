using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using REA.Core;
using REA.Domain;
using REA.Desktop.Services;

namespace REA.Desktop.ViewModels;

public sealed record EnvironmentOption(Guid? Id, string Label);

public sealed class InterfaceRow(CaptureInterfaceInfo i)
{
    public CaptureInterfaceInfo Info { get; } = i;
    public string Label => (Info.IsLoopback ? "Loopback · " : "") + (Info.FriendlyName ?? Info.Description);
    public string Addresses => Info.Addresses.Count == 0 ? "sem endereço" : string.Join(", ", Info.Addresses);
}

/// <summary>
/// Captura ao vivo. Singleton: a captura continua se o usuário trocar de tela, e o estado é mantido.
/// </summary>
public sealed partial class NetworkCaptureViewModel : ObservableObject, IPage
{
    private const int VisiblePackets = 1000;
    private readonly ScopedRunner _runner;
    private readonly ILiveCaptureEngine _engine;
    private readonly NotificationService _notify;
    private readonly INavigator _navigator;
    private readonly ILogger<NetworkCaptureViewModel> _logger;
    private readonly DispatcherTimer _timer;
    private ActiveCapture? _active;

    public NetworkCaptureViewModel(ScopedRunner runner, ILiveCaptureEngine engine, NotificationService notify,
        INavigator navigator, ILogger<NetworkCaptureViewModel> logger)
    {
        _runner = runner;
        _engine = engine;
        _notify = notify;
        _navigator = navigator;
        _logger = logger;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Pump();
    }

    public string Title => "Network Capture";

    public ObservableCollection<InterfaceRow> Interfaces { get; } = [];
    public ObservableCollection<ProjectSummary> Projects { get; } = [];
    public ObservableCollection<EnvironmentOption> Environments { get; } = [];
    public ObservableCollection<LivePacket> Packets { get; } = [];

    [ObservableProperty] private string? _unavailableReason;
    [ObservableProperty] private InterfaceRow? _selectedInterface;
    [ObservableProperty] private ProjectSummary? _selectedProject;
    [ObservableProperty] private EnvironmentOption? _selectedEnvironment;
    [ObservableProperty] private string _captureName = "";
    [ObservableProperty] private string _bpfFilter = "";
    [ObservableProperty] private string _maxPackets = "";
    [ObservableProperty] private string _maxMegabytes = "100";
    [ObservableProperty] private string _maxSeconds = "";
    [ObservableProperty] private string? _formError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    private bool _isCapturing;

    [ObservableProperty] private string _statsText = "Nenhuma captura em andamento.";
    [ObservableProperty] private Guid? _lastCaptureId;
    [ObservableProperty] private string? _lastResult;

    public bool IsIdle => !IsCapturing;
    public bool HasNoPackets => Packets.Count == 0;
    public bool IsAvailable => UnavailableReason is null;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        UnavailableReason = _engine.UnavailableReason;
        OnPropertyChanged(nameof(IsAvailable));
        if (IsCapturing) return; // mantém a tela como está durante a captura
        await LoadProjectsAsync();
        if (IsAvailable) RefreshInterfaces();
        if (string.IsNullOrWhiteSpace(CaptureName)) CaptureName = $"Captura {DateTime.Now:dd/MM/yyyy HH:mm}";
    }

    private async Task LoadProjectsAsync()
    {
        var page = await _runner.RunAsync<ProjectService, PagedResult<ProjectSummary>>(
            s => s.ListAsync(new ProjectQuery(null, ProjectStatus.Active, new PageRequest(1, 500))));
        var keep = SelectedProject?.Id;
        Projects.Clear();
        foreach (var p in page.Items.OrderBy(p => p.Name)) Projects.Add(p);
        SelectedProject = Projects.FirstOrDefault(p => p.Id == keep) ?? Projects.FirstOrDefault();
    }

    async partial void OnSelectedProjectChanged(ProjectSummary? value)
    {
        Environments.Clear();
        Environments.Add(new EnvironmentOption(null, "(sem ambiente)"));
        if (value is not null)
        {
            var envs = await _runner.RunAsync<EnvironmentService, IReadOnlyList<ResearchEnvironment>>(s => s.ListAsync(value.Id));
            foreach (var e in envs) Environments.Add(new EnvironmentOption(e.Id, e.Name));
        }
        SelectedEnvironment = Environments[0];
    }

    [RelayCommand]
    private void RefreshInterfaces()
    {
        try
        {
            var keep = SelectedInterface?.Info.Name;
            Interfaces.Clear();
            foreach (var i in _engine.ListInterfaces().OrderByDescending(i => i.IsLoopback).ThenBy(i => i.Description))
                Interfaces.Add(new InterfaceRow(i));
            SelectedInterface = Interfaces.FirstOrDefault(i => i.Info.Name == keep) ?? Interfaces.FirstOrDefault();
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Não foi possível listar as interfaces: {ex.Message}";
            OnPropertyChanged(nameof(IsAvailable));
        }
    }

    private bool CanStart() => !IsCapturing;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        FormError = null;
        if (SelectedProject is null) { FormError = "Escolha um projeto (as capturas ficam guardadas como evidência do projeto)."; return; }
        if (SelectedInterface is null) { FormError = "Escolha uma interface de rede."; return; }
        if (!TryParseLimit(MaxPackets, "pacotes", out var maxPackets)
            || !TryParseLimit(MaxMegabytes, "megabytes", out var maxMb)
            || !TryParseLimit(MaxSeconds, "segundos", out var maxSec)) return;

        var options = new LiveCaptureOptions(SelectedInterface.Info.Name, BpfFilter, maxPackets,
            maxMb is { } mb ? mb * 1024 * 1024 : null, maxSec is { } s ? TimeSpan.FromSeconds(s) : null);
        var input = new StartCaptureInput(SelectedProject.Id, SelectedEnvironment?.Id, CaptureName, options);
        var result = await _runner.RunAsync<CaptureService, OperationResult<ActiveCapture>>(c => c.StartLiveAsync(input));
        if (!result.Success) { FormError = result.Error; return; }

        _active = result.Value!;
        Packets.Clear();
        LastResult = null;
        LastCaptureId = null;
        IsCapturing = true;
        _timer.Start();
        _ = WatchCompletionAsync(_active);
        _notify.Info($"Captura iniciada em {SelectedInterface.Label}.");
    }

    private bool CanStop() => IsCapturing;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task Stop() => _active?.Session.StopAsync() ?? Task.CompletedTask;

    /// <summary>Usado ao fechar o aplicativo: encerra e registra a captura em andamento.</summary>
    public async Task StopIfRunningAsync()
    {
        if (_active is { } a)
        {
            await a.Session.StopAsync();
            await FinalizeAsync(a, await a.Session.Completion);
        }
    }

    private async Task WatchCompletionAsync(ActiveCapture active)
    {
        var result = await active.Session.Completion;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await FinalizeAsync(active, result)).Task.Unwrap();
    }

    private async Task FinalizeAsync(ActiveCapture active, LiveCaptureResult result)
    {
        if (!ReferenceEquals(_active, active)) return;
        _active = null;
        _timer.Stop();
        Pump();
        IsCapturing = false;
        try
        {
            var saved = await _runner.RunAsync<CaptureService, OperationResult>(c => c.FinalizeLiveAsync(active.CaptureId, result));
            LastCaptureId = active.CaptureId;
            LastResult = $"Captura encerrada ({CaptureService.EndReasonText(result.Reason)}): {result.Stats.Packets} pacote(s), " +
                         $"{Format.Bytes(result.Stats.Bytes)} em {result.Stats.Elapsed:hh\\:mm\\:ss}. " +
                         (result.Stats.Dropped + result.Stats.InterfaceDropped > 0
                             ? $"Atenção: {result.Stats.Dropped + result.Stats.InterfaceDropped} pacote(s) descartado(s) pelo driver."
                             : "Nenhum pacote descartado.");
            if (result.Error is not null) _notify.Error(result.Error);
            else if (saved.Success) _notify.Success("Captura salva e analisada.");
            else _notify.Error(saved.Error!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao finalizar a captura {Id}", active.CaptureId);
            _notify.Error($"A captura foi gravada, mas o registro falhou: {ex.Message}. Ela será recuperada na próxima abertura.");
        }
        CaptureName = $"Captura {DateTime.Now:dd/MM/yyyy HH:mm}";
    }

    private void Pump()
    {
        if (_active is null) return;
        var s = _active.Session.Stats;
        StatsText = $"{s.Packets} pacote(s) · {Format.Bytes(s.Bytes)} · {s.Elapsed:hh\\:mm\\:ss}" +
                    (s.Dropped + s.InterfaceDropped > 0 ? $" · {s.Dropped + s.InterfaceDropped} descartado(s)" : "");
        foreach (var p in _active.Session.DrainNewPackets(500)) Packets.Add(p);
        while (Packets.Count > VisiblePackets) Packets.RemoveAt(0);
        OnPropertyChanged(nameof(HasNoPackets));
    }

    [RelayCommand]
    private Task OpenInInspector() => LastCaptureId is { } id ? _navigator.NavigateAsync(NavigationCatalog.ResearchPackets, id) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenStreams() => LastCaptureId is { } id ? _navigator.NavigateAsync(NavigationCatalog.ResearchTcp, id) : Task.CompletedTask;

    [RelayCommand]
    private void UseLoopbackExample() => BpfFilter = "tcp port 7000";

    private bool TryParseLimit(string text, string unit, out long? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var v) && v > 0) { value = v; return true; }
        FormError = $"O limite de {unit} precisa ser um número inteiro maior que zero (ou vazio para sem limite).";
        return false;
    }
}
