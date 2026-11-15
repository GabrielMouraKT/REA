using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using REA.Core;
using REA.Domain;
using REA.Desktop.Services;
using REA.PacketAnalyzer;

namespace REA.Desktop.ViewModels;

/// <summary>Base comum: seletor de captura e abertura do arquivo.</summary>
public abstract partial class CaptureViewerBase(ScopedRunner runner, NotificationService notify) : ObservableObject
{
    protected ScopedRunner Runner { get; } = runner;
    protected NotificationService Notify { get; } = notify;
    private bool _loadingOptions;

    public ObservableCollection<CaptureOption> CaptureOptions { get; } = [];
    [ObservableProperty] private CaptureOption? _selectedCapture;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _fileWarning;
    [ObservableProperty] private string _captureInfo = "";
    public bool HasNoCaptures => CaptureOptions.Count == 0;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        _loadingOptions = true;
        CaptureOptions.Clear();
        foreach (var o in await ViewModels.CaptureOptions.LoadAsync(Runner)) CaptureOptions.Add(o);
        OnPropertyChanged(nameof(HasNoCaptures));
        var target = parameter is Guid id
            ? CaptureOptions.FirstOrDefault(o => o.Id == id)
            : CaptureOptions.FirstOrDefault();
        _loadingOptions = false;
        SelectedCapture = target;
    }

    async partial void OnSelectedCaptureChanged(CaptureOption? value)
    {
        if (_loadingOptions || value is null) return;
        IsLoading = true;
        try
        {
            var capture = await Runner.RunAsync<CaptureService, Capture?>(s => s.GetAsync(value.Id));
            if (capture is null) { Notify.Warning("Captura não encontrada."); return; }
            var path = await Runner.RunAsync<CaptureService, string>(s => Task.FromResult(s.ResolvePath(capture)));
            if (!File.Exists(path)) { FileWarning = "O arquivo desta captura não foi encontrado na pasta de dados."; return; }
            CaptureInfo = $"{capture.FileFormat.ToUpperInvariant()} · SHA-256 {capture.Sha256?[..16]}… · {Format.Bytes(capture.FileSizeBytes)}";
            await OpenAsync(path);
        }
        catch (Exception ex) { Notify.Error($"Não foi possível abrir a captura: {ex.Message}"); }
        finally { IsLoading = false; }
    }

    protected abstract Task OpenAsync(string path);
}

public sealed record ProtocolOption(string? Value, string Label);

public sealed class PacketRow(PacketSummary s)
{
    public PacketSummary Summary { get; } = s;
    public long Number => Summary.Number;
    public string Time => Summary.Timestamp.ToLocalTime().ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
    public string Source => Summary.SourcePort is { } p ? $"{Summary.Source}:{p}" : Summary.Source;
    public string Destination => Summary.DestinationPort is { } p ? $"{Summary.Destination}:{p}" : Summary.Destination;
    public string Protocol => Summary.Protocol;
    public int Length => Summary.Length;
    public string Flags => Summary.TcpFlags ?? "";
    public string Info => Summary.Info;
}

public sealed partial class PacketInspectorViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs)
    : CaptureViewerBase(runner, notify), IPage
{
    private CaptureDocument? _document;
    private IReadOnlyList<long> _matches = [];

    public string Title => "Packet Inspector";
    public ObservableCollection<PacketRow> Items { get; } = [];
    public ObservableCollection<DecodedLayer> Layers { get; } = [];
    public IReadOnlyList<int> PageSizes => PageRequest.AllowedSizes;
    public IReadOnlyList<ProtocolOption> Protocols { get; } =
        new[] { null, "TCP", "UDP", "ICMP", "ICMPv6", "ARP", "IPv4", "IPv6", "Ethernet", "Malformado" }
            .Select(p => new ProtocolOption(p, p ?? "Todos")).ToList();

    [ObservableProperty] private ProtocolOption? _protocol;
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _port = "";
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _onlyPayload;
    [ObservableProperty] private int _pageSize = PageRequest.DefaultSize;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private string _pageText = "";
    [ObservableProperty] private PacketRow? _selectedPacket;
    [ObservableProperty] private string _hex = "";
    [ObservableProperty] private string _payloadHex = "";
    [ObservableProperty] private string? _filterError;

    protected override async Task OpenAsync(string path)
    {
        _document = await CaptureDocument.OpenAsync(path);
        FileWarning = _document.Index.TruncationReason is { } r
            ? $"Arquivo incompleto: {r}. Os {_document.PacketCount} pacotes anteriores são exibidos normalmente." : null;
        Protocol ??= Protocols[0];
        await ApplyFilter();
    }

    [RelayCommand]
    private async Task ApplyFilter()
    {
        if (_document is null) return;
        FilterError = null;
        int? port = null;
        if (!string.IsNullOrWhiteSpace(Port))
        {
            if (!int.TryParse(Port.Trim(), out var p) || p is < 0 or > 65535) { FilterError = "Porta deve ser um número de 0 a 65535."; return; }
            port = p;
        }
        IsLoading = true;
        try
        {
            _matches = await _document.FilterAsync(new PacketFilter(Protocol?.Value, Address, port, Text, OnlyPayload));
            Page = 1;
            await LoadPageAsync();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private Task ClearFilter()
    {
        Protocol = Protocols[0];
        Address = Port = Text = "";
        OnlyPayload = false;
        return ApplyFilter();
    }

    partial void OnPageSizeChanged(int value) { Page = 1; _ = LoadPageAsync(); }

    [RelayCommand]
    private async Task NextPage() { if (Page < TotalPages) { Page++; await LoadPageAsync(); } }

    [RelayCommand]
    private async Task PreviousPage() { if (Page > 1) { Page--; await LoadPageAsync(); } }

    private async Task LoadPageAsync()
    {
        if (_document is null) return;
        TotalPages = Math.Max(1, (int)Math.Ceiling(_matches.Count / (double)PageSize));
        var slice = _matches.Skip((Page - 1) * PageSize).Take(PageSize);
        var rows = await _document.ReadSummariesAsync(slice);
        Items.Clear();
        foreach (var r in rows) Items.Add(new PacketRow(r));
        PageText = $"Página {Page} de {TotalPages} · {_matches.Count} de {_document.PacketCount} pacote(s)";
        SelectedPacket = Items.FirstOrDefault();
    }

    async partial void OnSelectedPacketChanged(PacketRow? value)
    {
        Layers.Clear();
        Hex = PayloadHex = "";
        if (value is null || _document is null) return;
        var detail = await _document.ReadDetailAsync(value.Number);
        if (detail is null) return;
        foreach (var l in detail.Layers) Layers.Add(l);
        Hex = HexDump.Format(detail.Raw);
        PayloadHex = detail.Payload.Length == 0 ? "(sem dados de aplicação)" : HexDump.Format(detail.Payload);
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        if (_document is null || _matches.Count == 0) return;
        var target = dialogs.SaveFile("Exportar pacotes filtrados", "pacotes.csv", "CSV (*.csv)|*.csv");
        if (target is null) return;
        IsLoading = true;
        try
        {
            var sb = new StringBuilder("numero;horario_utc;origem;porta_origem;destino;porta_destino;protocolo;tamanho;flags;dados;info\n");
            foreach (var chunk in _matches.Chunk(2000))
                foreach (var s in await _document.ReadSummariesAsync(chunk))
                    sb.AppendLine(string.Join(';', s.Number, s.Timestamp.UtcDateTime.ToString("o"), s.Source, s.SourcePort, s.Destination,
                        s.DestinationPort, s.Protocol, s.Length, Csv(s.TcpFlags), s.PayloadLength, Csv(s.Info)));
            await File.WriteAllTextAsync(target, sb.ToString(), new UTF8Encoding(true));
            Notify.Success($"{_matches.Count} pacote(s) exportado(s) para {target}.");
        }
        catch (Exception ex) { Notify.Error($"Falha ao exportar: {ex.Message}"); }
        finally { IsLoading = false; }
    }

    private static string Csv(string? v) => v is null ? "" : "\"" + v.Replace("\"", "\"\"") + "\"";
}

public sealed class StreamRow(TcpStreamResult s)
{
    public TcpStreamResult Result { get; } = s;
    public int Index => Result.Index;
    public string Client => Result.Client.ToString();
    public string Server => Result.Server.ToString();
    public int Packets => Result.Packets;
    public string ClientBytes => Format.Bytes(Result.ClientToServer.Data.Length);
    public string ServerBytes => Format.Bytes(Result.ServerToClient.Data.Length);
    public string Status => Result.CompletenessText;
    public bool IsIncomplete => Result.Completeness == TcpStreamCompleteness.Incomplete;
    public int Retransmissions => Result.ClientToServer.Retransmissions + Result.ServerToClient.Retransmissions;
    public string Duration => (Result.End - Result.Start).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}

public sealed class TimelineRow(TcpTimelineEntry e)
{
    public long Frame { get; } = e.Frame;
    public string Time { get; } = e.Timestamp.ToLocalTime().ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
    public string Direction { get; } = e.FromClient ? "C → S" : "S → C";
    public string Flags { get; } = e.Flags;
    public int Length { get; } = e.PayloadLength;
    public long Sequence { get; } = e.RelativeSequence;
}

public sealed record DirectionOption(bool ClientToServer, string Label);

public sealed partial class TcpStreamsViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs)
    : CaptureViewerBase(runner, notify), IPage
{
    public string Title => "TCP Stream Explorer";
    public ObservableCollection<StreamRow> Streams { get; } = [];
    public ObservableCollection<TimelineRow> Timeline { get; } = [];
    public ObservableCollection<string> Gaps { get; } = [];
    public IReadOnlyList<DirectionOption> Directions { get; } =
        [new(true, "Cliente → Servidor"), new(false, "Servidor → Cliente")];

    [ObservableProperty] private StreamRow? _selectedStream;
    [ObservableProperty] private DirectionOption? _direction;
    [ObservableProperty] private string _hex = "";
    [ObservableProperty] private string _ascii = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _streamStats = "";
    public bool HasGaps => Gaps.Count > 0;

    protected override async Task OpenAsync(string path)
    {
        var doc = await CaptureDocument.OpenAsync(path);
        FileWarning = doc.Index.TruncationReason is { } r ? $"Arquivo incompleto: {r}." : null;
        var streams = await doc.ReassembleTcpAsync();
        Streams.Clear();
        foreach (var s in streams) Streams.Add(new StreamRow(s));
        var complete = streams.Count(s => s.Completeness == TcpStreamCompleteness.Complete);
        var incomplete = streams.Count(s => s.Completeness == TcpStreamCompleteness.Incomplete);
        Summary = streams.Count == 0 ? "Nenhuma conexão TCP nesta captura."
            : $"{streams.Count} fluxo(s): {complete} completo(s), {incomplete} com lacunas, " +
              $"{streams.Count - complete - incomplete} sem lacunas mas com início/fim fora da captura.";
        Direction ??= Directions[0];
        SelectedStream = Streams.FirstOrDefault();
    }

    partial void OnSelectedStreamChanged(StreamRow? value) => Render();
    partial void OnDirectionChanged(DirectionOption? value) => Render();

    private void Render()
    {
        Timeline.Clear();
        Gaps.Clear();
        Hex = Ascii = StreamStats = "";
        if (SelectedStream is null || Direction is null) { OnPropertyChanged(nameof(HasGaps)); return; }
        var s = SelectedStream.Result;
        foreach (var t in s.Timeline.Take(5000)) Timeline.Add(new TimelineRow(t));
        var d = Direction.ClientToServer ? s.ClientToServer : s.ServerToClient;
        foreach (var g in s.ClientToServer.Gaps) Gaps.Add($"Cliente → Servidor: {g.Length} byte(s) a partir do byte {g.Offset} — {g.Reason}");
        foreach (var g in s.ServerToClient.Gaps) Gaps.Add($"Servidor → Cliente: {g.Length} byte(s) a partir do byte {g.Offset} — {g.Reason}");
        OnPropertyChanged(nameof(HasGaps));
        Hex = d.Data.Length == 0 ? "(nenhum dado nesta direção)" : HexDump.Format(d.Data);
        Ascii = d.Data.Length == 0 ? "" : PacketDecoder.Printable(d.Data, 64 * 1024);
        StreamStats = $"{s.Client} ⇄ {s.Server} · {s.CompletenessText} · {d.Segments} segmento(s) com dados nesta direção, " +
                      $"{d.Retransmissions} retransmissão(ões), {d.PartialOverlaps} sobreposição(ões)" +
                      (d.Truncated ? " · exibição limitada a 32 MB" : "") +
                      (d.Gaps.Count > 0 ? " · lacunas preenchidas com 00 na visualização" : "");
    }

    [RelayCommand]
    private async Task ExportBytes()
    {
        if (SelectedStream is null || Direction is null) return;
        var d = Direction.ClientToServer ? SelectedStream.Result.ClientToServer : SelectedStream.Result.ServerToClient;
        var name = $"fluxo{SelectedStream.Index}-{(Direction.ClientToServer ? "cliente-servidor" : "servidor-cliente")}.bin";
        var target = dialogs.SaveFile("Exportar bytes do fluxo", name, "Binário (*.bin)|*.bin");
        if (target is null) return;
        await File.WriteAllBytesAsync(target, d.Data);
        Notify.Success(d.Gaps.Count > 0
            ? $"Exportado para {target}. Atenção: {d.Gaps.Count} lacuna(s) estão preenchidas com zeros."
            : $"Exportado para {target}.");
    }
}
