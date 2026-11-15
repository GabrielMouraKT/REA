using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace REA.BinaryAnalysis;

public sealed record PeSection(
    string Name,
    uint VirtualAddress,
    uint VirtualSize,
    uint RawOffset,
    uint RawSize,
    string Permissions,
    string Contents,
    double Entropy);

public sealed record PeImport(string Library, IReadOnlyList<string> Functions);

public sealed record PeExport(uint Ordinal, string? Name, uint Rva, string? ForwardedTo);

public sealed record PeVersion(
    string? CompanyName,
    string? ProductName,
    string? FileDescription,
    string? FileVersion,
    string? ProductVersion,
    string? OriginalFilename);

public sealed record PeInfo(
    string Machine,
    bool Is64Bit,
    bool IsDll,
    string Subsystem,
    /// <summary>Data do cabeçalho; nula quando o binário usa build reprodutível (o campo vira um hash, não uma data).</summary>
    DateTimeOffset? LinkTimestamp,
    uint RawTimestamp,
    ulong ImageBase,
    uint EntryPointRva,
    bool IsDotNet,
    string? DotNetRuntime,
    /// <summary>Há um bloco de assinatura Authenticode embutido. A validade da assinatura NÃO é verificada.</summary>
    bool HasEmbeddedSignature,
    IReadOnlyList<PeSection> Sections,
    IReadOnlyList<PeImport> Imports,
    IReadOnlyList<PeExport> Exports,
    PeVersion? Version,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Lê cabeçalhos PE com <see cref="PEReader"/>; imports e exports são lidos das tabelas por RVA, com limites para
/// arquivos malformados (o que passar do limite vira aviso, nunca é descartado em silêncio).
/// </summary>
public static class PeAnalyzer
{
    public const int MaxLibraries = 4096;
    public const int MaxFunctionsPerLibrary = 20_000;
    public const int MaxExports = 100_000;
    private const int MaxNameLength = 1024;

    public static bool TryReadPeSignature(string path, out bool is64, out bool isDotNet)
    {
        is64 = isDotNet = false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var headers = new PEHeaders(fs);
            if (headers.PEHeader is null) return false;
            is64 = headers.PEHeader.Magic == PEMagic.PE32Plus;
            isDotNet = headers.CorHeader is not null;
            return true;
        }
        catch (BadImageFormatException) { return false; }
    }

    public static PeInfo Analyze(string path, CancellationToken ct = default)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var pe = new PEReader(fs, PEStreamOptions.LeaveOpen);
        var headers = pe.PEHeaders;
        var h = headers.PEHeader ?? throw new BadImageFormatException("Arquivo sem cabeçalho PE opcional.");
        var coff = headers.CoffHeader;
        var warnings = new List<string>();

        var reproducible = false;
        try { reproducible = pe.ReadDebugDirectory().Any(d => d.Type == DebugDirectoryEntryType.Reproducible); }
        catch (BadImageFormatException) { warnings.Add("Diretório de depuração malformado; ignorado."); }
        var raw = unchecked((uint)coff.TimeDateStamp);
        DateTimeOffset? stamp = reproducible || raw == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(raw);
        if (reproducible) warnings.Add("Build reprodutível: o campo de data do cabeçalho é um hash, não a data de compilação.");

        string? runtime = null;
        if (headers.CorHeader is not null)
        {
            try { runtime = pe.GetMetadataReader().MetadataVersion; }
            catch (BadImageFormatException) { warnings.Add("Metadados .NET malformados."); }
        }

        var sections = new List<PeSection>();
        foreach (var s in headers.SectionHeaders)
        {
            ct.ThrowIfCancellationRequested();
            sections.Add(new PeSection(s.Name, (uint)s.VirtualAddress, (uint)s.VirtualSize, (uint)s.PointerToRawData,
                (uint)s.SizeOfRawData, Permissions(s.SectionCharacteristics), Contents(s.SectionCharacteristics),
                RangeEntropy(fs, s.PointerToRawData, s.SizeOfRawData)));
        }

        var is64 = h.Magic == PEMagic.PE32Plus;
        var imports = ReadImports(pe, h.ImportTableDirectory, is64, warnings, ct);
        var exports = ReadExports(pe, h.ExportTableDirectory, warnings, ct);

        PeVersion? version = null;
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            if ((v.CompanyName ?? v.ProductName ?? v.FileDescription ?? v.FileVersion ?? v.OriginalFilename) is not null)
                version = new PeVersion(Clean(v.CompanyName), Clean(v.ProductName), Clean(v.FileDescription),
                    Clean(v.FileVersion), Clean(v.ProductVersion), Clean(v.OriginalFilename));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Recurso de versão não lido: {ex.Message}");
        }

        return new PeInfo(MachineName(coff.Machine), is64, coff.Characteristics.HasFlag(Characteristics.Dll),
            SubsystemName(h.Subsystem), stamp, raw, h.ImageBase, (uint)h.AddressOfEntryPoint,
            headers.CorHeader is not null, runtime, h.CertificateTableDirectory.Size > 0,
            sections, imports, exports, version, warnings);
    }

    private static List<PeImport> ReadImports(PEReader pe, DirectoryEntry dir, bool is64, List<string> warnings, CancellationToken ct)
    {
        var result = new List<PeImport>();
        if (dir.RelativeVirtualAddress == 0 || dir.Size == 0) return result;
        var table = Reader(pe, dir.RelativeVirtualAddress);
        if (table.Length == 0) { warnings.Add("Tabela de imports aponta para fora das seções."); return result; }

        while (table.RemainingBytes >= 20)
        {
            ct.ThrowIfCancellationRequested();
            var originalThunk = table.ReadUInt32();
            table.Offset += 8; // TimeDateStamp + ForwarderChain
            var nameRva = table.ReadUInt32();
            var firstThunk = table.ReadUInt32();
            if (originalThunk == 0 && nameRva == 0 && firstThunk == 0) break;
            if (result.Count >= MaxLibraries)
            {
                warnings.Add($"Mais de {MaxLibraries} bibliotecas importadas; lista truncada.");
                break;
            }

            var library = ReadAsciiZ(pe, nameRva) ?? $"(nome ilegível em RVA 0x{nameRva:X})";
            var functions = new List<string>();
            var thunks = Reader(pe, (int)(originalThunk != 0 ? originalThunk : firstThunk));
            var size = is64 ? 8 : 4;
            while (thunks.RemainingBytes >= size)
            {
                var entry = is64 ? thunks.ReadUInt64() : thunks.ReadUInt32();
                if (entry == 0) break;
                if (functions.Count >= MaxFunctionsPerLibrary)
                {
                    warnings.Add($"{library}: mais de {MaxFunctionsPerLibrary} funções; lista truncada.");
                    break;
                }
                var byOrdinal = is64 ? (entry & 0x8000_0000_0000_0000UL) != 0 : (entry & 0x8000_0000UL) != 0;
                if (byOrdinal) { functions.Add($"#{entry & 0xFFFF}"); continue; }
                var hintName = (int)(entry & 0x7FFF_FFFF);
                functions.Add(ReadAsciiZ(pe, (uint)hintName + 2) ?? $"(nome ilegível em RVA 0x{hintName:X})");
            }
            result.Add(new PeImport(library, functions));
        }
        return result;
    }

    private static List<PeExport> ReadExports(PEReader pe, DirectoryEntry dir, List<string> warnings, CancellationToken ct)
    {
        var result = new List<PeExport>();
        if (dir.RelativeVirtualAddress == 0 || dir.Size == 0) return result;
        var r = Reader(pe, dir.RelativeVirtualAddress);
        if (r.Length < 40) { warnings.Add("Tabela de exports aponta para fora das seções."); return result; }
        r.Offset = 16;
        var ordinalBase = r.ReadUInt32();
        var functionCount = r.ReadUInt32();
        var nameCount = r.ReadUInt32();
        var functionsRva = r.ReadUInt32();
        var namesRva = r.ReadUInt32();
        var ordinalsRva = r.ReadUInt32();

        if (functionCount > MaxExports)
        {
            warnings.Add($"Tabela declara {functionCount} exports; lidos os primeiros {MaxExports}.");
            functionCount = MaxExports;
        }

        var names = new Dictionary<uint, string>();
        var nameReader = Reader(pe, (int)namesRva);
        var ordReader = Reader(pe, (int)ordinalsRva);
        for (uint i = 0; i < nameCount && nameReader.RemainingBytes >= 4 && ordReader.RemainingBytes >= 2; i++)
        {
            var nRva = nameReader.ReadUInt32();
            var index = ordReader.ReadUInt16();
            if (ReadAsciiZ(pe, nRva) is { } n) names.TryAdd(index, n);
        }

        var funcs = Reader(pe, (int)functionsRva);
        var dirStart = (uint)dir.RelativeVirtualAddress;
        var dirEnd = dirStart + (uint)dir.Size;
        for (uint i = 0; i < functionCount && funcs.RemainingBytes >= 4; i++)
        {
            ct.ThrowIfCancellationRequested();
            var rva = funcs.ReadUInt32();
            if (rva == 0) continue;
            var forwarded = rva >= dirStart && rva < dirEnd ? ReadAsciiZ(pe, rva) : null;
            result.Add(new PeExport(ordinalBase + i, names.GetValueOrDefault(i), rva, forwarded));
        }
        if (result.Count == 0 && functionCount > 0) warnings.Add("Tabela de exports ilegível.");
        return result;
    }

    private static BlobReader Reader(PEReader pe, int rva)
    {
        if (rva <= 0) return default;
        try { return pe.GetSectionData(rva).GetReader(); }
        catch (BadImageFormatException) { return default; }
    }

    private static string? ReadAsciiZ(PEReader pe, uint rva)
    {
        var r = Reader(pe, (int)rva);
        if (r.Length == 0) return null;
        var sb = new StringBuilder();
        while (r.RemainingBytes > 0 && sb.Length < MaxNameLength)
        {
            var b = r.ReadByte();
            if (b == 0) return sb.ToString();
            if (b is < 0x20 or > 0x7E) return null;
            sb.Append((char)b);
        }
        return null;
    }

    private static double RangeEntropy(FileStream fs, int offset, int size)
    {
        if (offset <= 0 || size <= 0 || offset >= fs.Length) return 0;
        var len = (int)Math.Min(size, fs.Length - offset);
        var counts = new long[256];
        var buffer = new byte[Math.Min(len, 1 << 16)];
        fs.Position = offset;
        var remaining = len;
        while (remaining > 0)
        {
            var read = fs.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0) break;
            for (var i = 0; i < read; i++) counts[buffer[i]]++;
            remaining -= read;
        }
        return FileIdentifier.EntropyFromCounts(counts, len - remaining);
    }

    private static string Permissions(SectionCharacteristics c) =>
        (c.HasFlag(SectionCharacteristics.MemRead) ? "L" : "-") +
        (c.HasFlag(SectionCharacteristics.MemWrite) ? "E" : "-") +
        (c.HasFlag(SectionCharacteristics.MemExecute) ? "X" : "-");

    private static string Contents(SectionCharacteristics c)
    {
        var parts = new List<string>();
        if (c.HasFlag(SectionCharacteristics.ContainsCode)) parts.Add("Código");
        if (c.HasFlag(SectionCharacteristics.ContainsInitializedData)) parts.Add("Dados");
        if (c.HasFlag(SectionCharacteristics.ContainsUninitializedData)) parts.Add("Dados não inicializados");
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    private static string MachineName(Machine m) => m switch
    {
        Machine.I386 => "x86 (32 bits)",
        Machine.Amd64 => "x64 (64 bits)",
        Machine.Arm64 => "ARM64",
        Machine.Arm or Machine.ArmThumb2 or Machine.Thumb => "ARM (32 bits)",
        Machine.IA64 => "Itanium",
        Machine.Unknown => "Neutra (AnyCPU)",
        _ => m.ToString(),
    };

    private static string SubsystemName(Subsystem s) => s switch
    {
        Subsystem.WindowsGui => "Aplicativo com janela (GUI)",
        Subsystem.WindowsCui => "Aplicativo de console",
        Subsystem.Native => "Nativo (driver ou componente do sistema)",
        Subsystem.EfiApplication or Subsystem.EfiBootServiceDriver or Subsystem.EfiRuntimeDriver or Subsystem.EfiRom => "UEFI",
        Subsystem.Unknown => "Não informado",
        _ => s.ToString(),
    };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
