using System.Buffers.Binary;
using System.Text;
using REA.BinaryAnalysis;
using REA.PacketAnalyzer;

namespace REA.BinaryTests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rea-bin-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name, byte[] content)
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, content);
        return full;
    }
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public class PeAnalyzerTests
{
    private static readonly string Notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
    private static readonly string Kernel32 = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

    [Fact]
    public void Real_notepad_headers_sections_and_imports()
    {
        Assert.SkipUnless(File.Exists(Notepad), "notepad.exe não encontrado nesta máquina.");
        var pe = PeAnalyzer.Analyze(Notepad, TestContext.Current.CancellationToken);

        Assert.Equal(Environment.Is64BitOperatingSystem, pe.Is64Bit);
        Assert.False(pe.IsDll);
        Assert.False(pe.IsDotNet);
        Assert.Equal("Aplicativo com janela (GUI)", pe.Subsystem);
        Assert.NotEqual(0u, pe.EntryPointRva);
        var text = Assert.Single(pe.Sections, s => s.Name == ".text");
        Assert.Contains('X', text.Permissions);
        Assert.InRange(text.Entropy, 4.0, 8.0);
        Assert.Contains(pe.Imports, i => i.Library.StartsWith("KERNEL32", StringComparison.OrdinalIgnoreCase) ||
                                         i.Library.StartsWith("api-ms-win", StringComparison.OrdinalIgnoreCase));
        Assert.All(pe.Imports, i => Assert.NotEmpty(i.Functions));
        Assert.NotNull(pe.Version);
        Assert.Contains("Microsoft", pe.Version!.CompanyName);
    }

    [Fact]
    public void Real_kernel32_exports_include_names_and_forwarders()
    {
        Assert.SkipUnless(File.Exists(Kernel32), "kernel32.dll não encontrado.");
        var pe = PeAnalyzer.Analyze(Kernel32, TestContext.Current.CancellationToken);
        Assert.True(pe.IsDll);
        Assert.Contains(pe.Exports, e => e.Name == "CreateFileW");
        Assert.Contains(pe.Exports, e => e.ForwardedTo is not null && e.ForwardedTo.Contains('.'));
        Assert.DoesNotContain(pe.Warnings, w => w.Contains("truncada"));
    }

    [Fact]
    public void Managed_assembly_is_detected_as_dotnet()
    {
        var path = typeof(PeAnalyzer).Assembly.Location;
        var pe = PeAnalyzer.Analyze(path, TestContext.Current.CancellationToken);
        Assert.True(pe.IsDotNet);
        Assert.StartsWith("v4.0", pe.DotNetRuntime);
        Assert.True(PeAnalyzer.TryReadPeSignature(path, out _, out var isDotNet));
        Assert.True(isDotNet);
    }

    [Fact]
    public void Truncated_pe_is_rejected_not_misreported()
    {
        using var dir = new TempDir();
        var mz = new byte[200];
        mz[0] = (byte)'M'; mz[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(mz.AsSpan(0x3C), 0x80);
        var path = dir.File("bad.exe", mz);
        Assert.False(PeAnalyzer.TryReadPeSignature(path, out _, out _));
        Assert.Equal("MZ", FileIdentifier.DetectFormat(path).Format);
    }
}

public class FileIdentifierTests
{
    [Fact]
    public async Task Notepad_is_identified_as_pe_with_hashes()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Assert.SkipUnless(File.Exists(notepad), "notepad.exe não encontrado.");
        var id = await FileIdentifier.IdentifyAsync(notepad, TestContext.Current.CancellationToken);
        Assert.Equal("PE", id.Format);
        Assert.Equal(64, id.Sha256.Length);
        Assert.Equal(40, id.Sha1.Length);
        Assert.Equal(".exe", id.Extension);
    }

    [Fact]
    public void Unreal_pak_is_found_by_footer_magic()
    {
        using var dir = new TempDir();
        var data = new byte[4096];
        Random.Shared.NextBytes(data);
        var footer = data.Length - 204;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(footer), FileIdentifier.UnrealPakMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(footer + 4), 11);
        data[0] = 0; // evita coincidir com outro formato pelos bytes iniciais
        var (format, description) = FileIdentifier.DetectFormat(dir.File("game.pak", data));
        Assert.Equal("PAK (Unreal Engine)", format);
        Assert.Contains("versão de formato 11", description);
    }

    [Fact]
    public void Extension_does_not_decide_format()
    {
        using var dir = new TempDir();
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        Assert.Equal("PNG", FileIdentifier.DetectFormat(dir.File("disfarçado.dll", png)).Format);
    }

    [Fact]
    public void Entropy_bounds()
    {
        Assert.Equal(0, FileIdentifier.Entropy(new byte[100]));
        var all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Assert.Equal(8.0, FileIdentifier.Entropy(all), 6);
    }
}

public class ElfAnalyzerTests
{
    /// <summary>ELF64 LE mínimo: seções nula, .text e .shstrtab.</summary>
    public static byte[] SyntheticElf64()
    {
        var names = Encoding.ASCII.GetBytes("\0.text\0.shstrtab\0");
        const int shoff = 0x100;
        var buf = new byte[shoff + 3 * 64];
        var s = buf.AsSpan();
        s[0] = 0x7F; s[1] = (byte)'E'; s[2] = (byte)'L'; s[3] = (byte)'F';
        s[4] = 2; s[5] = 1; s[6] = 1; s[7] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(s[16..], 3);    // ET_DYN
        BinaryPrimitives.WriteUInt16LittleEndian(s[18..], 183);  // AArch64
        BinaryPrimitives.WriteUInt64LittleEndian(s[24..], 0x1040);
        BinaryPrimitives.WriteUInt64LittleEndian(s[40..], shoff);
        BinaryPrimitives.WriteUInt16LittleEndian(s[58..], 64);
        BinaryPrimitives.WriteUInt16LittleEndian(s[60..], 3);
        BinaryPrimitives.WriteUInt16LittleEndian(s[62..], 2);
        names.CopyTo(s[0x80..]);
        var text = s[(shoff + 64)..];
        BinaryPrimitives.WriteUInt32LittleEndian(text, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(text[4..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(text[16..], 0x1000);
        BinaryPrimitives.WriteUInt64LittleEndian(text[24..], 0x40);
        BinaryPrimitives.WriteUInt64LittleEndian(text[32..], 0x20);
        var str = s[(shoff + 128)..];
        BinaryPrimitives.WriteUInt32LittleEndian(str, 7);
        BinaryPrimitives.WriteUInt32LittleEndian(str[4..], 3);
        BinaryPrimitives.WriteUInt64LittleEndian(str[24..], 0x80);
        BinaryPrimitives.WriteUInt64LittleEndian(str[32..], (ulong)names.Length);
        return buf;
    }

    [Fact]
    public void Synthetic_elf64_header_and_sections()
    {
        using var dir = new TempDir();
        var path = dir.File("libgame.so", SyntheticElf64());
        var elf = ElfAnalyzer.Analyze(path);
        Assert.True(elf.Is64Bit);
        Assert.True(elf.IsLittleEndian);
        Assert.Equal("ARM64 (AArch64)", elf.Machine);
        Assert.StartsWith("Biblioteca compartilhada", elf.FileType);
        Assert.Equal(0x1040ul, elf.EntryPoint);
        Assert.Equal(["", ".text", ".shstrtab"], elf.Sections.Select(x => x.Name));
        Assert.Empty(elf.Warnings);
        Assert.Equal("ELF", FileIdentifier.DetectFormat(path).Format);
    }

    [Fact]
    public void Section_table_outside_file_is_a_warning()
    {
        using var dir = new TempDir();
        var data = SyntheticElf64()[..0x100];
        var elf = ElfAnalyzer.Analyze(dir.File("cut.so", data));
        Assert.Empty(elf.Sections);
        Assert.Single(elf.Warnings);
    }
}

public class StringExtractorTests
{
    private static async Task<StringScanResult> Scan(byte[] data, int min = 4, int max = 1000) =>
        await StringExtractor.ExtractAsync(new MemoryStream(data), min, max, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Ascii_and_utf16_at_both_alignments()
    {
        var data = new List<byte> { 0, 1, 2 };
        data.AddRange(Encoding.ASCII.GetBytes("HELLO"));        // offset 3
        data.AddRange([0, 0xFF]);                               // offset 8
        data.AddRange(Encoding.Unicode.GetBytes("Mundo"));      // offset 10 (par)
        data.AddRange([0xFF, 0xFE, 0xFD]);                      // offset 20
        data.AddRange(Encoding.Unicode.GetBytes("Odd!"));       // offset 23 (ímpar)
        data.Add(0xFF);
        var r = await Scan([.. data]);
        Assert.False(r.Truncated);
        Assert.Contains(new FoundString(3, StringEncodingKind.Ascii, "HELLO"), r.Strings);
        Assert.Contains(new FoundString(10, StringEncodingKind.Utf16LE, "Mundo"), r.Strings);
        Assert.Contains(new FoundString(23, StringEncodingKind.Utf16LE, "Odd!"), r.Strings);
        Assert.Equal(data.Count, r.BytesScanned);
    }

    [Fact]
    public async Task Strings_crossing_buffer_boundary_are_whole()
    {
        var data = new byte[200_000];
        var marker = Encoding.ASCII.GetBytes("BOUNDARY_STRING");
        marker.CopyTo(data, 65536 - 5);
        var r = await Scan(data);
        Assert.Contains(new FoundString(65531, StringEncodingKind.Ascii, "BOUNDARY_STRING"), r.Strings);
    }

    [Fact]
    public async Task Limit_reports_truncation_and_coverage()
    {
        var data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("abcd\0", 100)));
        var r = await Scan(data, max: 10);
        Assert.True(r.Truncated);
        Assert.Equal(10, r.Strings.Count);
        Assert.True(r.BytesScanned < data.Length);
        Assert.Equal(data.Length, r.FileSize);
    }

    [Fact]
    public void Hex_dump_shows_file_offset()
    {
        var text = HexDump.Format(new byte[] { 0x41, 0x42 }, baseOffset: 0x1000);
        Assert.StartsWith("001000  41 42", text);
    }
}

public class DirectoryInventoryTests
{
    [Fact]
    public async Task Scan_duplicates_compare_and_manifest_roundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        using var v1 = new TempDir();
        v1.File("bin/game.exe", [1, 2, 3]);
        v1.File("data/a.pak", [9, 9, 9, 9]);
        v1.File("data/copy.pak", [9, 9, 9, 9]);
        v1.File("old/readme.txt", Encoding.ASCII.GetBytes("readme text"));
        v1.File("gone.dat", [7]);

        using var v2 = new TempDir();
        v2.File("bin/game.exe", [1, 2, 4]);                                // alterado
        v2.File("data/a.pak", [9, 9, 9, 9]);                               // igual
        v2.File("data/copy.pak", [9, 9, 9, 9]);                            // igual
        v2.File("docs/readme.txt", Encoding.ASCII.GetBytes("readme text")); // movido
        v2.File("new.bin", [5, 5]);                                        // adicionado

        var m1 = await DirectoryInventory.ScanAsync(v1.Path, ct: ct);
        var m2 = await DirectoryInventory.ScanAsync(v2.Path, ct: ct);
        Assert.Equal(5, m1.Files.Count);
        Assert.Empty(m1.Skipped);
        Assert.Equal("Texto", m1.Files.Single(f => f.RelativePath == "old/readme.txt").Format);

        var dup = Assert.Single(DirectoryInventory.FindDuplicates(m1));
        Assert.Equal(["data/a.pak", "data/copy.pak"], dup.Paths);
        Assert.Equal(4, dup.WastedBytes);

        var c = DirectoryInventory.Compare(m1, m2);
        Assert.Equal("bin/game.exe", Assert.Single(c.Changed).RelativePath);
        Assert.Equal(("old/readme.txt", "docs/readme.txt"), (Assert.Single(c.Moved).From, c.Moved[0].To));
        Assert.Equal("new.bin", Assert.Single(c.Added).RelativePath);
        Assert.Equal("gone.dat", Assert.Single(c.Removed).RelativePath);
        Assert.Equal(2, c.Unchanged);

        var manifestPath = Path.Combine(v1.Path, "..", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await DirectoryInventory.SaveAsync(m1, manifestPath, ct);
            var loaded = await DirectoryInventory.LoadAsync(manifestPath, ct);
            Assert.Equal(m1.Files, loaded.Files);
            Assert.False(DirectoryInventory.Compare(m1, loaded).HasDifferences);
        }
        finally { File.Delete(manifestPath); }
    }

    [Fact]
    public async Task Junction_is_not_followed_and_is_reported()
    {
        using var target = new TempDir();
        target.File("secret.bin", [1]);
        using var root = new TempDir();
        root.File("real.txt", Encoding.ASCII.GetBytes("hello world"));
        // Junção (mklink /J) não exige privilégio de administrador, ao contrário do link simbólico.
        var link = Path.Combine(root.Path, "link");
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
               {
                   ArgumentList = { "/c", "mklink", "/J", link, target.Path },
                   UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
               })!)
        {
            await p.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.SkipUnless(p.ExitCode == 0 && Directory.Exists(link), "Não foi possível criar a junção.");
        }

        var m = await DirectoryInventory.ScanAsync(root.Path, ct: TestContext.Current.CancellationToken);
        Assert.Equal("real.txt", Assert.Single(m.Files).RelativePath);
        Assert.Equal("link", Assert.Single(m.Skipped).RelativePath);
    }
}
