using System.Buffers.Binary;
using System.Text;

namespace REA.BinaryAnalysis;

public sealed record ElfSection(string Name, uint Type, ulong Address, ulong Offset, ulong Size);

public sealed record ElfInfo(
    bool Is64Bit,
    bool IsLittleEndian,
    string OsAbi,
    string FileType,
    string Machine,
    ulong EntryPoint,
    int ProgramHeaderCount,
    int SectionCount,
    IReadOnlyList<ElfSection> Sections,
    IReadOnlyList<string> Warnings);

/// <summary>Cabeçalho ELF e tabela de seções. Tudo é validado contra o tamanho real do arquivo.</summary>
public static class ElfAnalyzer
{
    public const int MaxSections = 4096;

    public static ElfInfo Analyze(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var ident = new byte[64];
        var n = fs.ReadAtLeast(ident, 64, throwOnEndOfStream: false);
        if (n < 52 || ident[0] != 0x7F || ident[1] != 'E' || ident[2] != 'L' || ident[3] != 'F')
            throw new BadImageFormatException("Não é um arquivo ELF.");
        if (ident[4] is not (1 or 2)) throw new BadImageFormatException($"Classe ELF inválida ({ident[4]}).");
        if (ident[5] is not (1 or 2)) throw new BadImageFormatException($"Ordem de bytes ELF inválida ({ident[5]}).");

        var is64 = ident[4] == 2;
        var le = ident[5] == 1;
        if (is64 && n < 64) throw new BadImageFormatException("Cabeçalho ELF de 64 bits incompleto.");
        var h = ident.AsSpan();
        ushort U16(ReadOnlySpan<byte> s) => le ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s);
        uint U32(ReadOnlySpan<byte> s) => le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
        ulong U64(ReadOnlySpan<byte> s) => le ? BinaryPrimitives.ReadUInt64LittleEndian(s) : BinaryPrimitives.ReadUInt64BigEndian(s);
        ulong Word(ReadOnlySpan<byte> s) => is64 ? U64(s) : U32(s);

        var type = U16(h[16..]);
        var machine = U16(h[18..]);
        var entry = Word(h[24..]);
        var shoff = is64 ? U64(h[40..]) : U32(h[32..]);
        var o = is64 ? 52 : 40; // e_ehsize
        var phnum = U16(h[(o + 4)..]);
        var shentsize = U16(h[(o + 6)..]);
        var shnum = U16(h[(o + 8)..]);
        var shstrndx = U16(h[(o + 10)..]);

        var warnings = new List<string>();
        var raw = new List<(uint NameOffset, uint Type, ulong Addr, ulong Offset, ulong Size)>();
        var minEntry = is64 ? 64 : 40;
        if (shnum > 0 && shoff > 0)
        {
            if (shentsize < minEntry || shoff + (ulong)shentsize * shnum > (ulong)fs.Length)
                warnings.Add("Tabela de seções fora do arquivo ou com tamanho de entrada inválido; seções não lidas.");
            else
            {
                var entryBuf = new byte[shentsize];
                for (var i = 0; i < Math.Min((int)shnum, MaxSections); i++)
                {
                    fs.Position = (long)shoff + (long)i * shentsize;
                    fs.ReadExactly(entryBuf);
                    var e = entryBuf.AsSpan();
                    raw.Add(is64
                        ? (U32(e), U32(e[4..]), U64(e[16..]), U64(e[24..]), U64(e[32..]))
                        : (U32(e), U32(e[4..]), U32(e[12..]), U32(e[16..]), U32(e[20..])));
                }
            }
        }

        byte[]? names = null;
        if (shstrndx < raw.Count)
        {
            var (_, _, _, off, size) = raw[shstrndx];
            if (off + size <= (ulong)fs.Length && size <= 16 * 1024 * 1024)
            {
                names = new byte[size];
                fs.Position = (long)off;
                fs.ReadExactly(names);
            }
            else warnings.Add("Tabela de nomes de seção fora do arquivo.");
        }

        var sections = raw.Select(r => new ElfSection(NameAt(names, r.NameOffset), r.Type, r.Addr, r.Offset, r.Size)).ToList();
        return new ElfInfo(is64, le, OsAbiName(ident[7]), TypeName(type), MachineName(machine), entry, phnum, shnum, sections, warnings);
    }

    private static string NameAt(byte[]? table, uint offset)
    {
        if (table is null || offset >= table.Length) return $"(sem nome, índice {offset})";
        var end = Array.IndexOf(table, (byte)0, (int)offset);
        if (end < 0) end = table.Length;
        return Encoding.ASCII.GetString(table, (int)offset, end - (int)offset);
    }

    private static string TypeName(ushort t) => t switch
    {
        1 => "Objeto relocável (.o)",
        2 => "Executável",
        3 => "Biblioteca compartilhada (.so) ou executável PIE",
        4 => "Core dump",
        _ => $"Tipo {t}",
    };

    private static string MachineName(ushort m) => m switch
    {
        3 => "x86 (32 bits)",
        62 => "x64 (64 bits)",
        40 => "ARM (32 bits)",
        183 => "ARM64 (AArch64)",
        8 => "MIPS",
        243 => "RISC-V",
        20 => "PowerPC",
        21 => "PowerPC 64",
        _ => $"Máquina {m}",
    };

    private static string OsAbiName(byte b) => b switch
    {
        0 => "System V / Linux",
        3 => "Linux (GNU)",
        9 => "FreeBSD",
        _ => $"ABI {b}",
    };
}
