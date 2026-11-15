using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using REA.Core;
using REA.Desktop.Services;
using REA.PacketAnalyzer;
using REA.ProtocolAnalysis;

namespace REA.Desktop.ViewModels;

/// <summary>Nó da árvore de campos Protobuf (com ou sem nomes do .proto).</summary>
public sealed record ProtoNode(string Header, string Detail, IReadOnlyList<ProtoNode> Children);

internal static class ProtoTree
{
    public static string WireName(WireType w) => w switch
    {
        WireType.Varint => "varint",
        WireType.Fixed64 => "64 bits",
        WireType.Fixed32 => "32 bits",
        WireType.LengthDelimited => "bloco",
        _ => w.ToString(),
    };

    public static List<ProtoNode> Build(IEnumerable<ProtoField> fields) =>
        fields.Select(f => new ProtoNode(
            $"campo {f.Number} · {WireName(f.WireType)} · byte {f.Offset}",
            string.Join("   |   ", f.Interpretations),
            f.Nested is null ? [] : Build(f.Nested))).ToList();

    public static List<ProtoNode> Build(IEnumerable<LabeledField> fields) =>
        fields.Select(l => new ProtoNode(
            l.IsDeclared ? $"{l.Name} ({l.DeclaredType}) · campo {l.Field.Number}" : $"campo {l.Field.Number} · não declarado no .proto",
            l.DisplayValue ?? string.Join("   |   ", l.Field.Interpretations),
            Build(l.Children))).ToList();

    public static bool TryParseHex(string text, out byte[] bytes, out string? error)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var token in text.Split([' ', '\n', '\r', '\t', ',', '-', ':'], StringSplitOptions.RemoveEmptyEntries))
            sb.Append(token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token);
        var hex = sb.ToString();
        bytes = [];
        error = null;
        if (hex.Length == 0) { error = "Cole os bytes em hexadecimal (ex.: 08 96 01)."; return false; }
        if (hex.Length % 2 != 0) { error = "Quantidade ímpar de dígitos hexadecimais."; return false; }
        if (!hex.All(Uri.IsHexDigit)) { error = "Há caracteres que não são hexadecimais."; return false; }
        bytes = Convert.FromHexString(hex);
        return true;
    }
}

// =====================================================================================
// Research › Protobuf Analyzer
// =====================================================================================

public sealed partial class ProtobufViewModel(NotificationService notify, IDialogService dialogs, ViewModeService mode) : ObservableObject, IPage
{
    private ProtoSchema? _schema;
    public string Title => "Protobuf Analyzer";
    public ViewModeService Mode { get; } = mode;
    public ObservableCollection<ProtoNode> Nodes { get; } = [];
    public ObservableCollection<string> Messages { get; } = [];

    [ObservableProperty] private string _hexInput = "";
    [ObservableProperty] private string? _selectedMessage;
    [ObservableProperty] private string _schemaText = "Nenhum .proto carregado: os campos aparecem só com número (o REA nunca inventa nomes).";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _encoderInput = "1 varint 150\n2 string Olá\n3 sint -42";
    [ObservableProperty] private string _encoderOutput = "";

    public Task OnNavigatedToAsync(object? parameter)
    {
        if (parameter is byte[] bytes) { HexInput = Convert.ToHexString(bytes); Decode(); }
        return Task.CompletedTask;
    }

    partial void OnSelectedMessageChanged(string? value) { if (HexInput.Length > 0) Decode(); }

    [RelayCommand]
    private void Decode()
    {
        Nodes.Clear();
        Error = null;
        if (!ProtoTree.TryParseHex(HexInput, out var bytes, out var err)) { Error = err; Result = ""; return; }
        var r = ProtobufWire.Decode(bytes);
        List<ProtoNode> nodes;
        if (_schema is not null && SelectedMessage is not null)
        {
            try { nodes = ProtoTree.Build(ProtoLabeler.Label(r.Fields, _schema, SelectedMessage)); }
            catch (KeyNotFoundException ex) { Error = ex.Message; nodes = ProtoTree.Build(r.Fields); }
        }
        else nodes = ProtoTree.Build(r.Fields);
        foreach (var n in nodes) Nodes.Add(n);

        Result = r.Complete && r.Fields.Count > 0
            ? $"Os {bytes.Length} bytes formam uma mensagem Protobuf válida com {Ui.Plural(r.Fields.Count, "campo", "campos")} no primeiro nível. " +
              "Isso é compatível com Protobuf, mas outros formatos às vezes também passam nessa verificação."
            : r.Fields.Count == 0
                ? $"Estes bytes não parecem Protobuf. {r.Error}"
                : $"Só o começo parece Protobuf ({r.Fields.Count} campo(s) lidos até o byte {r.BytesConsumed}). {r.Error}";
    }

    [RelayCommand]
    private async Task LoadProto()
    {
        var path = dialogs.OpenFile("Abrir arquivo .proto", "Protobuf (*.proto)|*.proto|Todos os arquivos (*.*)|*.*");
        if (path is null) return;
        try
        {
            _schema = ProtoSchema.Parse(await File.ReadAllTextAsync(path));
            Messages.Clear();
            foreach (var m in _schema.Messages.Keys.Order(StringComparer.Ordinal)) Messages.Add(m);
            SchemaText = $"{Path.GetFileName(path)}: {Ui.Plural(_schema.Messages.Count, "mensagem", "mensagens")}, {Ui.Plural(_schema.Enums.Count, "enum", "enums")}." +
                         (_schema.Notes.Count > 0 ? " " + string.Join(" · ", _schema.Notes) : "");
            SelectedMessage = Messages.FirstOrDefault();
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            notify.Error($".proto inválido: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearProto()
    {
        _schema = null;
        Messages.Clear();
        SelectedMessage = null;
        SchemaText = "Nenhum .proto carregado: os campos aparecem só com número (o REA nunca inventa nomes).";
        if (HexInput.Length > 0) Decode();
    }

    /// <summary>Cada linha: número tipo valor. Tipos: varint, sint, bool, fixed32, fixed64, float, double, string, bytes (hex).</summary>
    [RelayCommand]
    private void Encode()
    {
        var w = new ProtoWriter();
        var lineNo = 0;
        try
        {
            foreach (var raw in EncoderInput.Split('\n'))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 || !int.TryParse(parts[0], out var field)) throw new FormatException("use: número tipo valor");
                var v = parts[2];
                var c = CultureInfo.InvariantCulture;
                switch (parts[1].ToLowerInvariant())
                {
                    case "varint": w.Varint(field, v.StartsWith('-') ? unchecked((ulong)long.Parse(v, c)) : ulong.Parse(v, c)); break;
                    case "sint": w.SInt(field, long.Parse(v, c)); break;
                    case "bool": w.Varint(field, bool.Parse(v) ? 1UL : 0UL); break;
                    case "fixed32": w.Fixed32(field, uint.Parse(v, c)); break;
                    case "fixed64": w.Fixed64(field, ulong.Parse(v, c)); break;
                    case "float": w.Fixed32(field, BitConverter.SingleToUInt32Bits(float.Parse(v, c))); break;
                    case "double": w.Fixed64(field, BitConverter.DoubleToUInt64Bits(double.Parse(v, c))); break;
                    case "string": w.String(field, v); break;
                    case "bytes":
                        if (!ProtoTree.TryParseHex(v, out var b, out var e)) throw new FormatException(e);
                        w.Bytes(field, b);
                        break;
                    default: throw new FormatException($"tipo '{parts[1]}' desconhecido");
                }
            }
            var bytes = w.ToArray();
            EncoderOutput = Convert.ToHexString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            EncoderOutput = $"Linha {lineNo}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void UseEncoded()
    {
        if (EncoderOutput.Length > 0 && EncoderOutput.All(Uri.IsHexDigit)) { HexInput = EncoderOutput; Decode(); }
    }
}

// =====================================================================================
// Research › Protocol Explorer
// =====================================================================================

public sealed record HypothesisRow(FramingResult Result)
{
    public string Description => Result.Hypothesis.Describe();
    public string Confidence => Result.Confidence switch { REA.ProtocolAnalysis.Confidence.Alta => "Alta", REA.ProtocolAnalysis.Confidence.Media => "Média", _ => "Baixa" };
    public int Frames => Result.FrameCount;
    public string Coverage => $"{Result.BytesExplained:N0} B" + (Result.TrailingBytes > 0 ? $" (+{Result.TrailingBytes:N0} B sobrando)" : " (tudo)");
    public string Protobuf => Result.ProtobufBodyRatio.ToString("P0", CultureInfo.CurrentCulture);
    public string Notes => string.Join(" ", Result.Notes);
}

public sealed record FrameRow(Frame Frame)
{
    public int Index => Frame.Index;
    public string Offset => $"0x{Frame.Offset:X}";
    public int Length => Frame.Length;
    public string Opcode => Frame.OpcodeCandidate is { } o ? $"0x{o:X4}" : "—";
    public string Protobuf => Frame.BodyIsProtobuf ? "Sim" : "Não";
}

public sealed record OpcodeRow(string Value, int Count, string BodySizes);

public sealed partial class ProtocolExplorerViewModel(ScopedRunner runner, NotificationService notify, INavigator navigator, ViewModeService mode)
    : CaptureViewerBase(runner, notify), IPage
{
    private byte[] _data = [];
    public string Title => "Protocol Explorer";
    public ViewModeService Mode { get; } = mode;
    public ObservableCollection<StreamRow> Streams { get; } = [];
    public IReadOnlyList<DirectionOption> Directions { get; } = [new(true, "Cliente → Servidor"), new(false, "Servidor → Cliente")];
    public ObservableCollection<HypothesisRow> Hypotheses { get; } = [];
    public ObservableCollection<OpcodeRow> Opcodes { get; } = [];
    [ObservableProperty] private IReadOnlyList<FrameRow> _frames = [];

    [ObservableProperty] private StreamRow? _selectedStream;
    [ObservableProperty] private DirectionOption? _direction;
    [ObservableProperty] private HypothesisRow? _selectedHypothesis;
    [ObservableProperty] private FrameRow? _selectedFrame;
    [ObservableProperty] private string _simpleSummary = "";
    [ObservableProperty] private string? _gapWarning;
    [ObservableProperty] private string _frameHex = "";
    [ObservableProperty] private string _frameProtobuf = "";

    protected override async Task OpenAsync(string path)
    {
        var doc = await CaptureDocument.OpenAsync(path);
        var streams = await doc.ReassembleTcpAsync();
        Streams.Clear();
        foreach (var s in streams) Streams.Add(new StreamRow(s));
        Direction ??= Directions[0];
        SelectedStream = Streams.FirstOrDefault();
        if (Streams.Count == 0) SimpleSummary = "Esta captura não tem conexões TCP para analisar.";
    }

    partial void OnSelectedStreamChanged(StreamRow? value) => Analyze();
    partial void OnDirectionChanged(DirectionOption? value) => Analyze();

    private void Analyze()
    {
        Hypotheses.Clear(); Opcodes.Clear(); Frames = [];
        FrameHex = FrameProtobuf = "";
        GapWarning = null;
        if (SelectedStream is null || Direction is null) return;
        var d = Direction.ClientToServer ? SelectedStream.Result.ClientToServer : SelectedStream.Result.ServerToClient;
        _data = d.Data;
        if (d.Gaps.Count > 0)
            GapWarning = $"Esta direção tem {d.Gaps.Count} lacuna(s) preenchida(s) com zeros; quadros depois de uma lacuna podem estar errados.";
        if (_data.Length == 0) { SimpleSummary = "Nenhum dado nesta direção."; return; }

        var results = FramingDetector.Analyze(_data);
        foreach (var r in results) Hypotheses.Add(new HypothesisRow(r));
        SelectedHypothesis = Hypotheses.FirstOrDefault();
        if (results.Count == 0)
            SimpleSummary = $"O REA não encontrou um padrão de \"tamanho no começo de cada mensagem\" nestes {_data.Length:N0} bytes. " +
                            "O protocolo pode usar outro formato (texto, delimitador, cifra) — veja os bytes no TCP Stream Explorer.";
    }

    partial void OnSelectedHypothesisChanged(HypothesisRow? value)
    {
        Opcodes.Clear();
        FrameHex = FrameProtobuf = "";
        if (value is null) { Frames = []; return; }
        var r = value.Result;
        Frames = r.Frames.Select(f => new FrameRow(f)).ToList();
        foreach (var o in r.OpcodeCandidates.Take(500))
            Opcodes.Add(new OpcodeRow($"0x{o.Value:X4}", o.Count, o.MinBodyLength == o.MaxBodyLength ? $"{o.MinBodyLength} B" : $"{o.MinBodyLength}–{o.MaxBodyLength} B"));
        var conf = value.Confidence.ToLowerInvariant();
        SimpleSummary =
            $"Hipótese com confiança {conf}: as mensagens parecem começar com um número que informa o tamanho de cada uma " +
            $"({r.Hypothesis.LengthSize} bytes). Assim, os {r.BytesExplained:N0} bytes se dividem em {Ui.Plural(r.FrameCount, "mensagem", "mensagens")}. " +
            (r.OpcodeCandidates.Count is > 0 and <= 64 ? $"Há {Ui.Plural(r.OpcodeCandidates.Count, "tipo provável", "tipos prováveis")} de mensagem (opcode candidato, não confirmado). " : "") +
            (r.ProtobufBodyRatio >= 0.8 ? "O conteúdo da maioria das mensagens é compatível com Protobuf. " : "") +
            "Isto é uma hipótese a confirmar com mais capturas.";
        SelectedFrame = Frames.FirstOrDefault();
    }

    partial void OnSelectedFrameChanged(FrameRow? value)
    {
        if (value is null) { FrameHex = FrameProtobuf = ""; return; }
        var f = value.Frame;
        FrameHex = HexDump.Format(_data.AsSpan(f.Offset, f.Length), 64 * 1024, f.Offset);
        var body = _data.AsMemory(f.BodyOffset, f.Offset + f.Length - f.BodyOffset);
        if (body.Length == 0) { FrameProtobuf = "(quadro sem corpo)"; return; }
        var r = ProtobufWire.Decode(body);
        var sb = new StringBuilder();
        sb.AppendLine(r.Complete && r.Fields.Count > 0 ? "Corpo compatível com Protobuf (sem nomes):" : $"Corpo não decodifica como Protobuf. {r.Error}");
        void Print(IEnumerable<ProtoField> fields, int depth)
        {
            foreach (var x in fields)
            {
                sb.Append(' ', depth * 2).AppendLine($"campo {x.Number} [{ProtoTree.WireName(x.WireType)}] {string.Join(" | ", x.Interpretations)}");
                if (x.Nested is not null) Print(x.Nested, depth + 1);
            }
        }
        Print(r.Fields, 1);
        FrameProtobuf = sb.ToString();
    }

    [RelayCommand]
    private Task OpenInProtobuf()
    {
        if (SelectedFrame is null) return Task.CompletedTask;
        var f = SelectedFrame.Frame;
        return navigator.NavigateAsync(NavigationCatalog.ResearchProtobuf, _data.AsSpan(f.BodyOffset, f.Offset + f.Length - f.BodyOffset).ToArray());
    }
}

// =====================================================================================
// Research › HTTP/HTTPS Analyzer
// =====================================================================================

public sealed record HttpRow(HttpExchange Exchange)
{
    public int Index => Exchange.Index;
    public string Method => Exchange.Method;
    public string Host => Exchange.Host ?? "—";
    public string Url => Exchange.Url;
    public string Status => Exchange.Status is { } s ? $"{s} {Exchange.StatusText}".Trim() : "sem resposta";
    public string Type => Exchange.ContentType ?? "—";
    public string Size => Exchange.ResponseBodySize is { } b ? Format.Bytes(b) : "—";
    public string Duration => Exchange.DurationMs is { } d ? $"{d:0} ms" : "—";
}

public sealed partial class HttpViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs, ViewModeService mode)
    : CaptureViewerBase(runner, notify), IPage
{
    public string Title => "HTTP/HTTPS Analyzer";
    public ViewModeService Mode { get; } = mode;
    [ObservableProperty] private IReadOnlyList<HttpRow> _exchanges = [];
    public ObservableCollection<EncryptedConnection> Encrypted { get; } = [];
    public ObservableCollection<string> Notes { get; } = [];

    [ObservableProperty] private HttpRow? _selectedExchange;
    [ObservableProperty] private string _headers = "";
    [ObservableProperty] private string _simpleSummary = "";
    [ObservableProperty] private string _sourceText = "";

    protected override async Task OpenAsync(string path)
    {
        if (TsharkCrossCheck.Locate() is null)
        {
            Show(new HttpAnalysis("TShark", [], [], ["TShark não encontrado. Instale o Wireshark para ler HTTP de capturas, ou importe um arquivo HAR do navegador."]), "Nesta captura");
            return;
        }
        SourceText = "Fonte: captura selecionada (lida pelo TShark).";
        Show(await HttpAnalyzer.FromCaptureAsync(path), "Nesta captura");
    }

    [RelayCommand]
    private async Task OpenHar()
    {
        var path = dialogs.OpenFile("Abrir arquivo HAR", "HAR (*.har)|*.har|JSON (*.json)|*.json");
        if (path is null) return;
        IsLoading = true;
        try
        {
            SourceText = $"Fonte: {Path.GetFileName(path)} (HAR exportado do navegador).";
            Show(await HttpAnalyzer.ReadHarAsync(path), "Neste arquivo HAR");
        }
        catch (Exception ex) { Notify.Error($"HAR inválido: {ex.Message}"); }
        finally { IsLoading = false; }
    }

    private void Show(HttpAnalysis a, string source)
    {
        Exchanges = a.Exchanges.Select(x => new HttpRow(x)).ToList();
        Encrypted.Clear();
        foreach (var e in a.Encrypted) Encrypted.Add(e);
        Notes.Clear();
        foreach (var n in a.Notes) Notes.Add(n);
        var hosts = a.Exchanges.Select(x => x.Host).Where(h => h is not null).Distinct().Count();
        SimpleSummary = $"{source}: {Ui.Plural(a.Exchanges.Count, "pedido HTTP aberto", "pedidos HTTP abertos")} para {Ui.Plural(hosts, "site", "sites")}" +
                        (a.Encrypted.Count > 0 ? $" e {Ui.Plural(a.Encrypted.Count, "conexão segura", "conexões seguras")} (HTTPS) cujo conteúdo é cifrado e não pode ser lido — só o nome do servidor aparece." : ".") +
                        " Senhas, tokens e cookies nunca são exibidos.";
        SelectedExchange = Exchanges.FirstOrDefault();
    }

    partial void OnSelectedExchangeChanged(HttpRow? value)
    {
        if (value is null) { Headers = ""; return; }
        var x = value.Exchange;
        var sb = new StringBuilder();
        sb.AppendLine($"{x.Method} {x.Url} {x.HttpVersion}");
        foreach (var h in x.RequestHeaders) sb.AppendLine($"{h.Name}: {h.Value}");
        if (x.RequestBodySize is { } rb) sb.AppendLine($"(corpo do pedido: {rb} bytes, não copiado)");
        sb.AppendLine();
        sb.AppendLine(x.Status is null ? "(sem resposta registrada)" : $"{x.Status} {x.StatusText}");
        foreach (var h in x.ResponseHeaders) sb.AppendLine($"{h.Name}: {h.Value}");
        if (x.ResponseBodySize is { } b) sb.AppendLine($"(corpo da resposta: {b} bytes, não copiado)");
        Headers = sb.ToString();
    }
}
