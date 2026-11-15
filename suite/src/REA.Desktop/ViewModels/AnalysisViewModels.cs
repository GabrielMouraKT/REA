using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using REA.BinaryAnalysis;
using REA.Core;
using REA.Desktop.Services;
using REA.Domain;
using REA.PacketAnalyzer;
using REA.ToolIntegration;

namespace REA.Desktop.ViewModels;

public sealed record FactRow(string Name, string Value);

public sealed record FileOption(Guid Id, string Label);

internal static class FileOptions
{
    public static async Task<List<FileOption>> LoadAsync(ScopedRunner runner)
    {
        var page = await runner.RunAsync<FileEvidenceService, PagedResult<AnalyzedFileSummary>>(
            s => s.ListAsync(new AnalyzedFileQuery(null, null, new PageRequest(1, 500))));
        return page.Items.Select(f => new FileOption(f.Id, $"{f.Name} · {f.ProjectName} · {f.Format} · {Format.Bytes(f.SizeBytes)}")).ToList();
    }

    public static async Task<(AnalyzedFile? File, string? Path)> ResolveAsync(ScopedRunner runner, Guid id) =>
        await runner.RunAsync<FileEvidenceService, (AnalyzedFile?, string?)>(async s =>
        {
            var f = await s.GetAsync(id);
            return (f, f is null ? null : s.ResolvePath(f));
        });
}

internal static class Ui
{
    public static void Post(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action(); else d.BeginInvoke(action);
    }

    public static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString("N0", CultureInfo.CurrentCulture)} {many}";
}

// =====================================================================================
// Evidence › Files
// =====================================================================================

public sealed class FileRow(AnalyzedFileSummary f)
{
    public AnalyzedFileSummary Summary { get; } = f;
    public Guid Id => Summary.Id;
    public string Name => Summary.Name;
    public string Project => Summary.ProjectName;
    public string Environment => Summary.EnvironmentName ?? "—";
    public string FormatName => Summary.Format;
    public string Architecture => Summary.Architecture ?? "—";
    public string Size => Format.Bytes(Summary.SizeBytes);
    public string Sha => Summary.Sha256.Length > 12 ? Summary.Sha256[..12] + "…" : Summary.Sha256;
    public string Ghidra => FileEvidenceService.StatusText(Summary.GhidraStatus);
    public string Created => Summary.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

public sealed partial class FilesViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs, INavigator navigator)
    : ObservableObject, IPage
{
    public string Title => "Arquivos";
    public ObservableCollection<FileRow> Items { get; } = [];
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
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _report;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private FileRow? _selectedRow;
    private bool _ready;

    public bool HasSelection => SelectedRow is not null;
    public bool IsEmpty => !IsLoading && TotalCount == 0;
    public bool CanImport => ProjectFilter?.Id is not null && !IsBusy;
    public string PageText => $"Página {Page} de {TotalPages} · {TotalCount} arquivo(s)";

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

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanImport));
    partial void OnPageSizeChanged(int value) { if (_ready) { Page = 1; _ = LoadAsync(); } }

    [RelayCommand] private async Task ApplySearch() { Page = 1; await LoadAsync(); }
    [RelayCommand] private async Task NextPage() { if (Page < TotalPages) { Page++; await LoadAsync(); } }
    [RelayCommand] private async Task PreviousPage() { if (Page > 1) { Page--; await LoadAsync(); } }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var q = new AnalyzedFileQuery(ProjectFilter?.Id, Search, new PageRequest(Page, PageSize));
            var r = await runner.RunAsync<FileEvidenceService, PagedResult<AnalyzedFileSummary>>(s => s.ListAsync(q));
            Items.Clear();
            foreach (var f in r.Items) Items.Add(new FileRow(f));
            TotalCount = r.TotalCount;
            TotalPages = r.TotalPages;
        }
        catch (Exception ex) { notify.Error($"Não foi possível carregar os arquivos: {ex.Message}"); }
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
        var path = dialogs.OpenFile("Importar arquivo para análise", "Todos os arquivos (*.*)|*.*|Executáveis e bibliotecas|*.exe;*.dll;*.so;*.sys|Pacotes de dados|*.pak;*.dat;*.bin");
        if (path is null) return;
        IsBusy = true;
        try
        {
            var r = await runner.RunAsync<FileEvidenceService, OperationResult<Guid>>(
                s => s.ImportAsync(new ImportFileInput(projectId, ImportEnvironment?.Id, path, null)));
            if (r.Success) notify.Success($"Arquivo importado: {Path.GetFileName(path)}. O original não foi alterado.");
            else notify.Error(r.Error!);
        }
        finally { IsBusy = false; }
        await LoadAsync();
    }

    [RelayCommand] private Task OpenBinary(FileRow? row) => row is null ? Task.CompletedTask : navigator.NavigateAsync(NavigationCatalog.ResearchBinary, row.Id);
    [RelayCommand] private Task OpenGhidra(FileRow? row) => row is null ? Task.CompletedTask : navigator.NavigateAsync(NavigationCatalog.ResearchGhidra, row.Id);

    [RelayCommand]
    private async Task Details(FileRow? row)
    {
        if (row is null) return;
        var (f, path) = await FileOptions.ResolveAsync(runner, row.Id);
        if (f is null) { notify.Warning("Arquivo não encontrado."); return; }
        var sb = new StringBuilder();
        sb.AppendLine($"Nome: {f.Name}");
        sb.AppendLine($"Projeto: {f.Project?.Name} · Ambiente: {f.Environment?.Name ?? "—"}");
        sb.AppendLine($"Formato: {f.Format} — {f.FormatDescription}");
        if (f.Architecture is not null) sb.AppendLine($"Arquitetura: {f.Architecture}");
        if (f.Summary is not null) sb.AppendLine($"Resumo: {f.Summary}");
        sb.AppendLine($"Tamanho: {Format.Bytes(f.SizeBytes)} ({f.SizeBytes:N0} bytes)");
        sb.AppendLine($"SHA-256: {f.Sha256}");
        if (f.OriginalPath is not null) sb.AppendLine($"Arquivo de origem: {f.OriginalPath}");
        sb.AppendLine($"Cópia guardada: {path}");
        sb.AppendLine($"Ghidra: {FileEvidenceService.StatusText(f.GhidraStatus)}" +
                      (f.GhidraFunctionCount is { } fc ? $" · {fc} função(ões), {f.GhidraStringCount} string(s), Ghidra {f.GhidraVersion}" : "") +
                      (f.GhidraError is { } ge ? $" · {ge}" : ""));
        sb.AppendLine($"Importado em: {f.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        Report = sb.ToString();
    }

    [RelayCommand]
    private async Task Export(FileRow? row)
    {
        if (row is null) return;
        var (f, source) = await FileOptions.ResolveAsync(runner, row.Id);
        if (f is null || source is null) return;
        var target = dialogs.SaveFile("Exportar cópia do arquivo", f.Name, "Todos os arquivos (*.*)|*.*");
        if (target is null) return;
        try
        {
            File.Copy(source, target, overwrite: true);
            notify.Success($"Cópia exportada para {target}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notify.Error($"Não foi possível exportar: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task ShowInFolder(FileRow? row)
    {
        if (row is null) return;
        var (_, path) = await FileOptions.ResolveAsync(runner, row.Id);
        if (path is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand] private void CloseReport() => Report = null;
}

// =====================================================================================
// Research › Binary Analysis
// =====================================================================================

public sealed record SectionRow(string Name, string Address, string VirtualSize, string RawSize, string Permissions, string Contents, string Entropy, bool HighEntropy);
public sealed record ImportRow(string Library, string Function);
public sealed record ExportRow(uint Ordinal, string Name, string Address, string Forward);
public sealed record StringRow(string Offset, string Encoding, string Value);

public sealed partial class BinaryAnalysisViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs, ViewModeService mode)
    : ObservableObject, IPage
{
    private string? _path;
    private List<ImportRow> _allImports = [];
    private List<StringRow> _allStrings = [];

    public string Title => "Binary Analysis";
    public ViewModeService Mode { get; } = mode;
    public ObservableCollection<FileOption> Files { get; } = [];
    public ObservableCollection<FactRow> Facts { get; } = [];
    public ObservableCollection<SectionRow> Sections { get; } = [];
    public ObservableCollection<ExportRow> Exports { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    [ObservableProperty] private IReadOnlyList<ImportRow> _imports = [];
    [ObservableProperty] private IReadOnlyList<StringRow> _strings = [];

    [ObservableProperty] private FileOption? _selectedFile;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _simpleSummary = "";
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _importFilter = "";
    [ObservableProperty] private string _stringFilter = "";
    [ObservableProperty] private int _minStringLength = StringExtractor.DefaultMinLength;
    [ObservableProperty] private string _stringStatus = "";
    [ObservableProperty] private string _importStatus = "";
    [ObservableProperty] private string _hexOffset = "0";
    [ObservableProperty] private string _hexLength = "4096";
    [ObservableProperty] private string _hexText = "";
    private bool _loadingOptions;

    public bool HasFile => _path is not null;
    /// <summary>Quadro de detalhes (hashes, endereços, tabelas): só no Modo Técnico.</summary>
    public bool ShowDetails => HasFile && Mode.IsTechnical;
    public bool HasNoFiles => Files.Count == 0;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        // Evento fraco: o serviço de modo é único no app e não deve manter esta tela viva.
        System.ComponentModel.PropertyChangedEventManager.AddHandler(Mode, (_, _) => OnPropertyChanged(nameof(ShowDetails)), nameof(ViewModeService.IsTechnical));
        _loadingOptions = true;
        Files.Clear();
        foreach (var f in await FileOptions.LoadAsync(runner)) Files.Add(f);
        OnPropertyChanged(nameof(HasNoFiles));
        _loadingOptions = false;
        SelectedFile = parameter is Guid id ? Files.FirstOrDefault(f => f.Id == id) : Files.FirstOrDefault();
    }

    async partial void OnSelectedFileChanged(FileOption? value)
    {
        if (_loadingOptions || value is null) return;
        var (file, path) = await FileOptions.ResolveAsync(runner, value.Id);
        if (file is null || path is null || !File.Exists(path)) { notify.Warning("A cópia guardada deste arquivo não foi encontrada."); return; }
        await OpenAsync(path, $"Evidência: {file.Name} (cópia guardada, SHA-256 {file.Sha256[..16]}…)");
    }

    [RelayCommand]
    private async Task OpenFromDisk()
    {
        var path = dialogs.OpenFile("Analisar arquivo (somente leitura)", "Todos os arquivos (*.*)|*.*");
        if (path is null) return;
        _loadingOptions = true;
        SelectedFile = null;
        _loadingOptions = false;
        await OpenAsync(path, $"Arquivo do computador (somente leitura, não é guardado como evidência): {path}");
    }

    private async Task OpenAsync(string path, string source)
    {
        IsLoading = true;
        Facts.Clear(); Sections.Clear(); Imports = []; Exports.Clear(); Strings = []; Warnings.Clear();
        _allImports = []; _allStrings = [];
        SimpleSummary = HexText = StringStatus = ImportStatus = "";
        try
        {
            _path = path;
            SourceText = source;
            var id = await Task.Run(() => FileIdentifier.IdentifyAsync(path));
            Facts.Add(new FactRow("Formato", $"{id.Format} — {id.Description}"));
            Facts.Add(new FactRow("Tamanho", $"{Format.Bytes(id.Size)} ({id.Size:N0} bytes)"));
            Facts.Add(new FactRow("SHA-256", id.Sha256));
            Facts.Add(new FactRow("SHA-1", id.Sha1));
            Facts.Add(new FactRow("Entropia", $"{id.Entropy:0.000} bits/byte" + (id.Entropy > 7.5 ? " (alta: compactado ou cifrado?)" : "")));

            var simple = new StringBuilder();
            switch (id.Format)
            {
                case "PE":
                    var pe = await Task.Run(() => PeAnalyzer.Analyze(path));
                    FillPe(pe, simple);
                    break;
                case "ELF":
                    var elf = await Task.Run(() => ElfAnalyzer.Analyze(path));
                    FillElf(elf, simple);
                    break;
                default:
                    simple.Append(id.Format switch
                    {
                        "PAK (Unreal Engine)" => "Pacote de dados de um jogo feito com Unreal Engine. ",
                        "ZIP" or "7z" or "RAR" or "GZIP" or "Zstandard" => "Arquivo compactado. ",
                        "Desconhecido" => "O REA não reconheceu o tipo deste arquivo pelos primeiros bytes. ",
                        _ => $"{id.Description}. ",
                    });
                    if (id.Entropy > 7.5) simple.Append("O conteúdo parece embaralhado, o que costuma indicar compactação ou criptografia. ");
                    break;
            }
            simple.Append($"Tamanho: {Format.Bytes(id.Size)}.");
            SimpleSummary = simple.ToString();
            OnPropertyChanged(nameof(HasFile));
            OnPropertyChanged(nameof(ShowDetails));
            await ExtractStringsAsync();
            await ReadHexAsync();
        }
        catch (Exception ex)
        {
            notify.Error($"Não foi possível analisar: {ex.Message}");
            SimpleSummary = $"Não foi possível ler a estrutura deste arquivo: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private void FillPe(PeInfo pe, StringBuilder simple)
    {
        simple.Append(pe.IsDll ? "Biblioteca do Windows (usada por outros programas)" : "Programa do Windows");
        simple.Append(pe.Is64Bit ? " de 64 bits" : " de 32 bits");
        simple.Append(pe.Subsystem.StartsWith("Aplicativo com janela", StringComparison.Ordinal) ? ", com janela. " : pe.Subsystem.StartsWith("Aplicativo de console", StringComparison.Ordinal) ? ", de linha de comando. " : ". ");
        if (pe.Version is { } v)
        {
            if (v.CompanyName is not null) simple.Append($"Publicado por {v.CompanyName}. ");
            if (v.ProductName is not null) simple.Append($"Produto: {v.ProductName} {v.ProductVersion}. ");
        }
        if (pe.IsDotNet) simple.Append("Feito em .NET (código gerenciado). ");
        simple.Append($"Usa {Ui.Plural(pe.Imports.Count, "biblioteca", "bibliotecas")} externas");
        simple.Append(pe.Exports.Count > 0 ? $" e oferece {Ui.Plural(pe.Exports.Count, "função", "funções")} para outros programas. " : ". ");
        if (pe.HasEmbeddedSignature) simple.Append("Tem assinatura digital embutida (o REA não confere se ela é válida). ");
        if (pe.Sections.Any(s => s.Entropy > 7.2 && s.RawSize > 4096)) simple.Append("Parte do código parece compactada ou protegida. ");

        Facts.Add(new FactRow("Arquitetura", pe.Machine));
        Facts.Add(new FactRow("Tipo", pe.IsDll ? "DLL" : "Executável"));
        Facts.Add(new FactRow("Subsistema", pe.Subsystem));
        Facts.Add(new FactRow("Data no cabeçalho", pe.LinkTimestamp?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? $"não é data (0x{pe.RawTimestamp:X8})"));
        Facts.Add(new FactRow("Image base", $"0x{pe.ImageBase:X}"));
        Facts.Add(new FactRow("Entry point (RVA)", $"0x{pe.EntryPointRva:X}"));
        Facts.Add(new FactRow(".NET", pe.IsDotNet ? $"Sim ({pe.DotNetRuntime})" : "Não"));
        Facts.Add(new FactRow("Assinatura embutida", pe.HasEmbeddedSignature ? "Presente (validade não verificada)" : "Ausente"));
        if (pe.Version is { } ver)
        {
            if (ver.CompanyName is not null) Facts.Add(new FactRow("Empresa", ver.CompanyName));
            if (ver.ProductName is not null) Facts.Add(new FactRow("Produto", ver.ProductName));
            if (ver.FileDescription is not null) Facts.Add(new FactRow("Descrição", ver.FileDescription));
            if (ver.FileVersion is not null) Facts.Add(new FactRow("Versão do arquivo", ver.FileVersion));
            if (ver.OriginalFilename is not null) Facts.Add(new FactRow("Nome original", ver.OriginalFilename));
        }
        foreach (var s in pe.Sections)
            Sections.Add(new SectionRow(s.Name, $"0x{s.VirtualAddress:X}", Format.Bytes(s.VirtualSize), Format.Bytes(s.RawSize),
                s.Permissions, s.Contents, s.Entropy.ToString("0.00", CultureInfo.CurrentCulture), s.Entropy > 7.2));
        _allImports = pe.Imports.SelectMany(i => i.Functions.Select(f => new ImportRow(i.Library, f))).ToList();
        ApplyImportFilter();
        foreach (var e in pe.Exports)
            Exports.Add(new ExportRow(e.Ordinal, e.Name ?? "(sem nome)", $"0x{e.Rva:X}", e.ForwardedTo ?? ""));
        foreach (var w in pe.Warnings) Warnings.Add(w);
    }

    private void FillElf(ElfInfo elf, StringBuilder simple)
    {
        simple.Append($"{elf.FileType} para Linux/Android ({elf.Machine}). ");
        Facts.Add(new FactRow("Arquitetura", elf.Machine));
        Facts.Add(new FactRow("Tipo", elf.FileType));
        Facts.Add(new FactRow("Classe", elf.Is64Bit ? "64 bits" : "32 bits"));
        Facts.Add(new FactRow("Ordem de bytes", elf.IsLittleEndian ? "little-endian" : "big-endian"));
        Facts.Add(new FactRow("ABI", elf.OsAbi));
        Facts.Add(new FactRow("Entry point", $"0x{elf.EntryPoint:X}"));
        Facts.Add(new FactRow("Cabeçalhos de programa", elf.ProgramHeaderCount.ToString(CultureInfo.CurrentCulture)));
        foreach (var s in elf.Sections)
            Sections.Add(new SectionRow(s.Name, $"0x{s.Address:X}", Format.Bytes((long)s.Size), Format.Bytes((long)s.Size), "—", $"tipo {s.Type}", "—", false));
        foreach (var w in elf.Warnings) Warnings.Add(w);
    }

    partial void OnImportFilterChanged(string value) => ApplyImportFilter();
    partial void OnStringFilterChanged(string value) => ApplyStringFilter();

    private void ApplyImportFilter()
    {
        var f = ImportFilter.Trim();
        var rows = f.Length == 0 ? _allImports : _allImports.Where(i => i.Library.Contains(f, StringComparison.OrdinalIgnoreCase) || i.Function.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        Imports = rows;
        ImportStatus = $"{rows.Count:N0} de {_allImports.Count:N0} função(ões) importada(s) de {_allImports.Select(i => i.Library).Distinct().Count()} biblioteca(s)";
    }

    [RelayCommand]
    private async Task ExtractStringsAsync()
    {
        if (_path is null) return;
        var min = Math.Clamp(MinStringLength, 2, 256);
        var path = _path;
        var r = await Task.Run(() => StringExtractor.ExtractAsync(path, min));
        _allStrings = r.Strings.Select(s => new StringRow($"0x{s.Offset:X8}", s.Encoding == StringEncodingKind.Ascii ? "ASCII" : "UTF-16", s.Value)).ToList();
        ApplyStringFilter();
        if (r.Truncated)
            Warnings.Add($"Strings: limite de {StringExtractor.DefaultMaxResults:N0} atingido; examinados {Format.Bytes(r.BytesScanned)} de {Format.Bytes(r.FileSize)}.");
    }

    private void ApplyStringFilter()
    {
        var f = StringFilter.Trim();
        var rows = f.Length == 0 ? _allStrings : _allStrings.Where(s => s.Value.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        Strings = rows;
        StringStatus = $"{rows.Count:N0} de {_allStrings.Count:N0} string(s) com ao menos {MinStringLength} caracteres";
    }

    [RelayCommand]
    private async Task ReadHexAsync()
    {
        if (_path is null) return;
        if (!TryParseNumber(HexOffset, out var offset) || offset < 0) { notify.Warning("Deslocamento inválido. Use decimal (4096) ou hexadecimal (0x1000)."); return; }
        if (!TryParseNumber(HexLength, out var length) || length <= 0) { notify.Warning("Quantidade de bytes inválida."); return; }
        var bytes = await FileRange.ReadAsync(_path, offset, (int)Math.Min(length, FileRange.MaxRangeBytes));
        HexText = bytes.Length == 0 ? "(nenhum byte nesta posição: além do fim do arquivo)" : HexDump.Format(bytes, FileRange.MaxRangeBytes, offset);
    }

    [RelayCommand]
    private async Task ExportStrings()
    {
        if (_allStrings.Count == 0) return;
        var target = dialogs.SaveFile("Exportar strings", "strings.txt", "Texto (*.txt)|*.txt");
        if (target is null) return;
        await File.WriteAllLinesAsync(target, Strings.Select(s => $"{s.Offset}\t{s.Encoding}\t{s.Value}"), Encoding.UTF8);
        notify.Success($"{Strings.Count:N0} string(s) exportada(s) para {target}.");
    }

    private static bool TryParseNumber(string text, out long value)
    {
        var t = text.Trim();
        return t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

// =====================================================================================
// Research › File Analysis
// =====================================================================================

public sealed record InventoryRow(string Path, string Size, string Format, string Sha);
public sealed record DuplicateRow(string Size, string Wasted, int Copies, string Paths, string Sha);
public sealed record ComparisonRow(string Change, string Path, string Details);

public sealed partial class FileAnalysisViewModel(NotificationService notify, IDialogService dialogs, ViewModeService mode) : ObservableObject, IPage
{
    private CancellationTokenSource? _cts;
    private DirectoryManifest? _manifest;
    private ManifestComparison? _comparison;
    private DirectoryManifest? _other;

    public string Title => "File Analysis";
    public ViewModeService Mode { get; } = mode;
    [ObservableProperty] private IReadOnlyList<InventoryRow> _files = [];
    public ObservableCollection<DuplicateRow> Duplicates { get; } = [];
    public ObservableCollection<ComparisonRow> Changes { get; } = [];
    public ObservableCollection<FactRow> Skipped { get; } = [];

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _summary = "Escolha uma pasta (por exemplo, a pasta de instalação de um cliente) para listar todos os arquivos com tamanho, tipo e impressão digital (SHA-256).";
    [ObservableProperty] private string _comparisonSummary = "";

    public bool HasManifest => _manifest is not null;
    public Task OnNavigatedToAsync(object? parameter) => Task.CompletedTask;

    [RelayCommand]
    private async Task ScanFolder()
    {
        var folder = dialogs.PickFolder("Escolha a pasta para inventariar");
        if (folder is null) return;
        var m = await ScanAsync(folder);
        if (m is null) return;
        _manifest = m;
        _comparison = null;
        _other = null;
        Changes.Clear();
        ComparisonSummary = "";
        ShowManifest(m);
    }

    private async Task<DirectoryManifest?> ScanAsync(string folder)
    {
        _cts = new CancellationTokenSource();
        IsScanning = true;
        var progress = new Progress<InventoryProgress>(p =>
            ProgressText = $"{p.FilesDone:N0} arquivo(s) · {Format.Bytes(p.BytesDone)} · {p.CurrentFile}");
        try { return await Task.Run(() => DirectoryInventory.ScanAsync(folder, progress, _cts.Token)); }
        catch (OperationCanceledException) { notify.Info("Inventário cancelado. Nada foi alterado."); return null; }
        catch (Exception ex) { notify.Error($"Não foi possível inventariar: {ex.Message}"); return null; }
        finally { IsScanning = false; ProgressText = ""; _cts.Dispose(); _cts = null; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    private void ShowManifest(DirectoryManifest m)
    {
        Duplicates.Clear(); Skipped.Clear();
        Files = m.Files.Select(f => new InventoryRow(f.RelativePath, Format.Bytes(f.Size), f.Format, f.Sha256[..16] + "…")).ToList();
        var dups = DirectoryInventory.FindDuplicates(m);
        foreach (var d in dups)
            Duplicates.Add(new DuplicateRow(Format.Bytes(d.Size), Format.Bytes(d.WastedBytes), d.Paths.Count, string.Join("\n", d.Paths), d.Sha256[..16] + "…"));
        foreach (var s in m.Skipped) Skipped.Add(new FactRow(s.RelativePath.Length == 0 ? "(raiz)" : s.RelativePath, s.Reason));
        var byFormat = m.Files.GroupBy(f => f.Format).OrderByDescending(g => g.Sum(f => f.Size)).Take(4)
            .Select(g => $"{g.Key}: {g.Count():N0}");
        Summary = $"{m.RootPath} — {Ui.Plural(m.Files.Count, "arquivo", "arquivos")}, {Format.Bytes(m.TotalBytes)}. " +
                  $"Tipos mais comuns: {string.Join(", ", byFormat)}. " +
                  (dups.Count > 0 ? $"{Ui.Plural(dups.Count, "grupo", "grupos")} de arquivos repetidos ({Format.Bytes(dups.Sum(d => d.WastedBytes))} que poderiam ser economizados). " : "Nenhum arquivo repetido. ") +
                  (m.Skipped.Count > 0 ? $"{Ui.Plural(m.Skipped.Count, "item não lido", "itens não lidos")} (veja a aba Ignorados)." : "");
        OnPropertyChanged(nameof(HasManifest));
    }

    [RelayCommand]
    private async Task SaveManifest()
    {
        if (_manifest is null) return;
        var target = dialogs.SaveFile("Salvar manifesto", $"manifesto-{DateTime.Now:yyyyMMdd-HHmm}.json", "Manifesto JSON (*.json)|*.json");
        if (target is null) return;
        try
        {
            await DirectoryInventory.SaveAsync(_manifest, target);
            notify.Success($"Manifesto salvo em {target}. Use-o depois para comparar com outra versão.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notify.Error($"Não foi possível salvar: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task OpenManifest()
    {
        var path = dialogs.OpenFile("Abrir manifesto", "Manifesto JSON (*.json)|*.json");
        if (path is null) return;
        try
        {
            _manifest = await DirectoryInventory.LoadAsync(path);
            _comparison = null; _other = null; Changes.Clear(); ComparisonSummary = "";
            ShowManifest(_manifest);
        }
        catch (Exception ex) { notify.Error($"Manifesto inválido: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task CompareWithFolder()
    {
        if (_manifest is null) { notify.Warning("Faça primeiro o inventário da versão antiga (ou abra um manifesto)."); return; }
        var folder = dialogs.PickFolder("Escolha a pasta da versão nova");
        if (folder is null) return;
        var other = await ScanAsync(folder);
        if (other is not null) ShowComparison(other);
    }

    [RelayCommand]
    private async Task CompareWithManifest()
    {
        if (_manifest is null) { notify.Warning("Faça primeiro o inventário da versão antiga (ou abra um manifesto)."); return; }
        var path = dialogs.OpenFile("Manifesto da versão nova", "Manifesto JSON (*.json)|*.json");
        if (path is null) return;
        try { ShowComparison(await DirectoryInventory.LoadAsync(path)); }
        catch (Exception ex) { notify.Error($"Manifesto inválido: {ex.Message}"); }
    }

    private void ShowComparison(DirectoryManifest other)
    {
        _other = other;
        var c = _comparison = DirectoryInventory.Compare(_manifest!, other);
        Changes.Clear();
        foreach (var x in c.Changed) Changes.Add(new ComparisonRow("Alterado", x.RelativePath, $"{Format.Bytes(x.Before.Size)} → {Format.Bytes(x.After.Size)}"));
        foreach (var x in c.Added) Changes.Add(new ComparisonRow("Novo", x.RelativePath, $"{Format.Bytes(x.Size)} · {x.Format}"));
        foreach (var x in c.Removed) Changes.Add(new ComparisonRow("Removido", x.RelativePath, $"{Format.Bytes(x.Size)} · {x.Format}"));
        foreach (var x in c.Moved) Changes.Add(new ComparisonRow("Movido", x.From, $"agora em {x.To}"));
        ComparisonSummary = c.HasDifferences
            ? $"Comparado com {other.RootPath}: {c.Changed.Count} alterado(s), {c.Added.Count} novo(s), {c.Removed.Count} removido(s), {c.Moved.Count} movido(s), {c.Unchanged:N0} igual(is)."
            : $"Comparado com {other.RootPath}: nenhuma diferença ({c.Unchanged:N0} arquivo(s) iguais).";
    }

    [RelayCommand]
    private async Task ExportReport()
    {
        if (_manifest is null) return;
        var target = dialogs.SaveFile("Exportar relatório", $"relatorio-arquivos-{DateTime.Now:yyyyMMdd-HHmm}.md", "Markdown (*.md)|*.md");
        if (target is null) return;
        var m = _manifest;
        var sb = new StringBuilder();
        sb.AppendLine("# Relatório de arquivos — REA Advanced Suite").AppendLine();
        sb.AppendLine($"- Pasta: `{m.RootPath}`");
        sb.AppendLine($"- Inventário feito em: {m.CreatedUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"- Arquivos: {m.Files.Count:N0} · Total: {Format.Bytes(m.TotalBytes)} · Não lidos: {m.Skipped.Count}").AppendLine();
        sb.AppendLine("## Tipos de arquivo").AppendLine().AppendLine("| Tipo | Quantidade | Tamanho |").AppendLine("|---|---:|---:|");
        foreach (var g in m.Files.GroupBy(f => f.Format).OrderByDescending(g => g.Sum(f => f.Size)))
            sb.AppendLine($"| {g.Key} | {g.Count():N0} | {Format.Bytes(g.Sum(f => f.Size))} |");
        var dups = DirectoryInventory.FindDuplicates(m);
        sb.AppendLine().AppendLine("## Arquivos repetidos").AppendLine();
        if (dups.Count == 0) sb.AppendLine("Nenhum.");
        foreach (var d in dups) sb.AppendLine($"- {d.Paths.Count} cópias de {Format.Bytes(d.Size)} (SHA-256 `{d.Sha256}`): {string.Join(", ", d.Paths.Select(p => $"`{p}`"))}");
        if (_comparison is { } c && _other is not null)
        {
            sb.AppendLine().AppendLine($"## Comparação com `{_other.RootPath}`").AppendLine();
            sb.AppendLine($"Alterados: {c.Changed.Count} · Novos: {c.Added.Count} · Removidos: {c.Removed.Count} · Movidos: {c.Moved.Count} · Iguais: {c.Unchanged:N0}").AppendLine();
            foreach (var r in Changes) sb.AppendLine($"- **{r.Change}** `{r.Path}` — {r.Details}");
        }
        if (m.Skipped.Count > 0)
        {
            sb.AppendLine().AppendLine("## Itens não lidos").AppendLine();
            foreach (var s in m.Skipped) sb.AppendLine($"- `{s.RelativePath}` — {s.Reason}");
        }
        await File.WriteAllTextAsync(target, sb.ToString(), Encoding.UTF8);
        notify.Success($"Relatório salvo em {target}.");
    }
}

// =====================================================================================
// Research › Ghidra Integration
// =====================================================================================

public sealed record FunctionRow(string Name, string Entry, long Size, string Kind, string Signature);
public sealed record ExternalRow(string Library, string Name);

public sealed partial class GhidraViewModel(ScopedRunner runner, NotificationService notify, INavigator navigator, ViewModeService mode)
    : ObservableObject, IPage
{
    private CancellationTokenSource? _cts;
    private GhidraExport? _export;
    public const int MaxLogLines = 5000;

    public string Title => "Ghidra Integration";
    public ViewModeService Mode { get; } = mode;
    public ObservableCollection<FileOption> Files { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    [ObservableProperty] private IReadOnlyList<FunctionRow> _functions = [];
    [ObservableProperty] private IReadOnlyList<ExternalRow> _externals = [];
    [ObservableProperty] private IReadOnlyList<StringRow> _strings = [];

    [ObservableProperty] private FileOption? _selectedFile;
    [ObservableProperty] private string _ghidraText = "";
    [ObservableProperty] private bool _ghidraFound;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _resultSummary = "";

    public bool HasNoFiles => Files.Count == 0;
    public bool CanRun => GhidraFound && SelectedFile is not null && !IsRunning;

    public async Task OnNavigatedToAsync(object? parameter)
    {
        var g = await runner.RunAsync<FileEvidenceService, GhidraInfo?>(s => Task.FromResult(s.LocateGhidra()));
        GhidraFound = g is not null;
        GhidraText = g is null
            ? "Ghidra não encontrado. Informe a pasta em Settings › Integrations (o Ghidra é seu; o REA não instala nada)."
            : $"Ghidra {g.Version} em {g.Directory}";
        Files.Clear();
        foreach (var f in await FileOptions.LoadAsync(runner)) Files.Add(f);
        OnPropertyChanged(nameof(HasNoFiles));
        SelectedFile = parameter is Guid id ? Files.FirstOrDefault(f => f.Id == id) : Files.FirstOrDefault();
    }

    partial void OnGhidraFoundChanged(bool value) => OnPropertyChanged(nameof(CanRun));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanRun));
    async partial void OnSelectedFileChanged(FileOption? value)
    {
        OnPropertyChanged(nameof(CanRun));
        await LoadStoredAsync();
    }
    partial void OnFilterChanged(string value) => ShowExport();

    private async Task LoadStoredAsync()
    {
        _export = null;
        ShowExport();
        StatusText = ResultSummary = "";
        if (SelectedFile is null) return;
        var (file, resultPath) = await runner.RunAsync<FileEvidenceService, (AnalyzedFile?, string?)>(async s =>
        {
            var f = await s.GetAsync(SelectedFile.Id);
            return (f, f is null ? null : s.ResolveGhidraResult(f));
        });
        if (file is null) return;
        StatusText = $"Situação: {FileEvidenceService.StatusText(file.GhidraStatus)}" + (file.GhidraError is { } e ? $" — {e}" : "");
        if (resultPath is not null && File.Exists(resultPath))
        {
            try
            {
                _export = await GhidraResultJson.LoadAsync(resultPath);
                ShowExport();
                StatusText += $" · resultado de {file.GhidraAnalyzedAt?.ToLocalTime():dd/MM/yyyy HH:mm} (Ghidra {file.GhidraVersion})";
            }
            catch (Exception ex) { StatusText += $" · resultado guardado ilegível: {ex.Message}"; }
        }
    }

    private void ShowExport()
    {
        if (_export is null) { Functions = []; Externals = []; Strings = []; return; }
        var f = Filter.Trim();
        bool Match(string s) => f.Length == 0 || s.Contains(f, StringComparison.OrdinalIgnoreCase);
        Functions = _export.Functions.Where(x => Match(x.Name) || Match(x.Entry))
            .Select(x => new FunctionRow(x.Name, x.Entry, x.Size, x.Thunk ? "Ponte (thunk)" : "Função", x.Signature ?? "")).ToList();
        Externals = _export.Externals.Where(x => Match(x.Name) || Match(x.Library)).Select(x => new ExternalRow(x.Library, x.Name)).ToList();
        Strings = _export.Strings.Where(x => Match(x.Value)).Select(x => new StringRow(x.Address, "", x.Value)).ToList();
        ResultSummary = $"O Ghidra encontrou {Ui.Plural(_export.Functions.Count, "função", "funções")}, " +
                        $"{Ui.Plural(_export.Externals.Count, "função externa usada", "funções externas usadas")} e " +
                        $"{Ui.Plural(_export.Strings.Count, "texto", "textos")} no programa \"{_export.Program}\" ({_export.Language})." +
                        (_export.FunctionsTruncated || _export.StringsTruncated ? " Listas limitadas pelo tamanho máximo de exportação." : "");
    }

    [RelayCommand]
    private async Task Run()
    {
        if (SelectedFile is null) return;
        var id = SelectedFile.Id;
        Log.Clear();
        IsRunning = true;
        StatusText = "Situação: Em análise… (arquivos grandes podem levar vários minutos)";
        _cts = new CancellationTokenSource();
        try
        {
            void OnLog(string line) => Ui.Post(() =>
            {
                Log.Add(line);
                while (Log.Count > MaxLogLines) Log.RemoveAt(0);
            });
            var r = await Task.Run(() => runner.RunAsync<FileEvidenceService, OperationResult>(s => s.RunGhidraAsync(id, OnLog, _cts.Token)));
            if (r.Success) notify.Success("Análise do Ghidra concluída.");
            else notify.Warning(r.Error ?? "A análise não foi concluída.");
        }
        catch (Exception ex) { notify.Error($"Falha ao executar o Ghidra: {ex.Message}"); }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
        await LoadStoredAsync();
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();
    [RelayCommand] private Task OpenSettings() => navigator.NavigateAsync(NavigationCatalog.SettingsIntegrations);
    [RelayCommand] private Task OpenFiles() => navigator.NavigateAsync(NavigationCatalog.EvidenceFiles);
}

// =====================================================================================
// Research › Kali Tools
// =====================================================================================

public sealed partial class KaliToolRow(KaliTool tool) : ObservableObject
{
    public KaliTool Tool { get; } = tool;
    public string Name => Tool.Name;
    public string Purpose => Tool.Purpose;
    [ObservableProperty] private string _status = "Não verificada";
    [ObservableProperty] private string _version = "";
}

public sealed partial class KaliViewModel(NotificationService notify) : ObservableObject, IPage
{
    private WslReport? _report;
    public string Title => "Kali Tools";
    public ObservableCollection<string> Distributions { get; } = [];
    public ObservableCollection<KaliToolRow> Tools { get; } = new(KaliCatalog.Tools.Select(t => new KaliToolRow(t)));

    [ObservableProperty] private string? _selectedDistribution;
    [ObservableProperty] private string _wslText = "Verificando o WSL…";
    [ObservableProperty] private bool _isBusy;

    public bool CanCheck => SelectedDistribution is not null && !IsBusy;
    partial void OnSelectedDistributionChanged(string? value) => OnPropertyChanged(nameof(CanCheck));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanCheck));

    public Task OnNavigatedToAsync(object? parameter) => Detect();

    [RelayCommand]
    private async Task Detect()
    {
        IsBusy = true;
        try
        {
            _report = await KaliCatalog.DetectAsync();
            Distributions.Clear();
            foreach (var d in _report.Distributions) Distributions.Add(d);
            SelectedDistribution = Distributions.FirstOrDefault(d => d.Contains("kali", StringComparison.OrdinalIgnoreCase)) ?? Distributions.FirstOrDefault();
            WslText = !_report.WslAvailable ? _report.Problem!
                : _report.Distributions.Count == 0
                    ? $"{_report.Problem} Para instalar o Kali, abra o PowerShell e execute: wsl --install -d kali-linux (o REA não instala nada sozinho)."
                    : $"WSL disponível com {Ui.Plural(_report.Distributions.Count, "distribuição", "distribuições")}: {string.Join(", ", _report.Distributions)}.";
        }
        catch (Exception ex) { WslText = $"Não foi possível consultar o WSL: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CheckTools()
    {
        if (_report is null || SelectedDistribution is null) return;
        IsBusy = true;
        try
        {
            foreach (var row in Tools)
            {
                row.Status = "Verificando…";
                var s = await KaliCatalog.CheckAsync(_report, SelectedDistribution, row.Tool);
                row.Status = s.Installed ? "Instalada" : s.Problem ?? "Não instalada";
                row.Version = s.Version ?? "";
            }
            notify.Info($"Verificação concluída em {SelectedDistribution}.");
        }
        catch (Exception ex) { notify.Error($"Falha na verificação: {ex.Message}"); }
        finally { IsBusy = false; }
    }
}

// =====================================================================================
// Settings › Integrations
// =====================================================================================

public sealed partial class IntegrationsViewModel(ScopedRunner runner, NotificationService notify, IDialogService dialogs, IntegrationSettings settings)
    : ObservableObject, IPage
{
    public string Title => "Integrations";
    public ObservableCollection<FactRow> Detected { get; } = [];

    [ObservableProperty] private string _ghidraDirectory = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isSaving;

    public Task OnNavigatedToAsync(object? parameter)
    {
        GhidraDirectory = settings.GhidraDirectory ?? "";
        Refresh();
        return Task.CompletedTask;
    }

    private void Refresh()
    {
        Detected.Clear();
        var g = GhidraHeadless.Locate(settings.GhidraDirectory);
        Detected.Add(new FactRow("Ghidra", g is null ? "Não encontrado" : $"{g.Version} — {g.Directory}" +
            (settings.GhidraDirectory is null ? " (via GHIDRA_INSTALL_DIR)" : " (pasta configurada)")));
        Detected.Add(new FactRow("TShark", TsharkCrossCheck.Locate() ?? "Não encontrado (instale o Wireshark)"));
        Detected.Add(new FactRow("WSL", KaliCatalog.LocateWsl() ?? "Não instalado"));
    }

    [RelayCommand]
    private void Browse()
    {
        var folder = dialogs.PickFolder("Pasta de instalação do Ghidra");
        if (folder is not null) GhidraDirectory = folder;
    }

    [RelayCommand]
    private async Task Save()
    {
        IsSaving = true;
        Error = null;
        try
        {
            var r = await runner.RunAsync<SettingsService, OperationResult>(s => s.SaveIntegrationsAsync(new IntegrationSettingsInput(GhidraDirectory)));
            if (r.Success) { notify.Success("Integrações salvas."); Refresh(); }
            else Error = r.Error;
        }
        finally { IsSaving = false; }
    }
}
