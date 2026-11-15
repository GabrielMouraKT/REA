using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace REA.BinaryAnalysis;

public sealed record FileIdentity(
    string Format,
    string Description,
    long Size,
    string Sha256,
    string Sha1,
    double Entropy,
    string? Extension);

/// <summary>
/// Identifica o formato pelos bytes (magic numbers), nunca só pela extensão. Arquivos desconhecidos ficam como
/// "Desconhecido", com a entropia como pista (alta entropia = provável compactação ou cifra).
/// </summary>
public static class FileIdentifier
{
    public const uint UnrealPakMagic = 0x5A6F12E1;

    public static async Task<FileIdentity> IdentifyAsync(string path, CancellationToken ct = default)
    {
        var info = new FileInfo(path);
        var (sha256, sha1, entropy) = await HashAndEntropyAsync(path, ct);
        var (format, description) = Detect(path, info.Length, entropy);
        return new FileIdentity(format, description, info.Length, sha256, sha1, entropy,
            string.IsNullOrEmpty(info.Extension) ? null : info.Extension.ToLowerInvariant());
    }

    /// <summary>Somente formato (sem hash), para inventários rápidos.</summary>
    public static (string Format, string Description) DetectFormat(string path)
    {
        var length = new FileInfo(path).Length;
        return Detect(path, length, entropy: null);
    }

    private static (string, string) Detect(string path, long length, double? entropy)
    {
        if (length == 0) return ("Vazio", "Arquivo vazio");
        Span<byte> h = stackalloc byte[64];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            n = fs.Read(h);
            h = h[..n];
            var pak = ProbeUnrealPak(fs, length);
            if (pak is not null) return ("PAK (Unreal Engine)", pak);
        }

        if (n >= 2 && h[0] == 'M' && h[1] == 'Z')
        {
            if (PeAnalyzer.TryReadPeSignature(path, out var is64, out var isDotNet))
                return ("PE", (isDotNet ? "Executável .NET (PE/CLI)" : "Executável Windows (PE)") + (is64 ? " 64 bits" : " 32 bits"));
            return ("MZ", "Executável DOS (MZ sem cabeçalho PE)");
        }
        if (StartsWith(h, 0x7F, (byte)'E', (byte)'L', (byte)'F')) return ("ELF", "Executável/biblioteca ELF (Linux/Android)");
        if (n >= 8 && BinaryPrimitives.ReadUInt32BigEndian(h) == 0xCAFEBABE)
            return BinaryPrimitives.ReadUInt32BigEndian(h[4..]) < 30
                ? ("Mach-O", "Mach-O universal (fat)")
                : ("Java class", "Classe Java compilada");
        if (n >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(h) is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE)
            return ("Mach-O", "Executável Mach-O (macOS/iOS)");
        if (StartsWith(h, (byte)'P', (byte)'K', 3, 4) || StartsWith(h, (byte)'P', (byte)'K', 5, 6))
            return ("ZIP", "Arquivo ZIP (também APK, JAR, DOCX…)");
        if (StartsWith(h, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C)) return ("7z", "Arquivo 7-Zip");
        if (StartsWith(h, (byte)'R', (byte)'a', (byte)'r', (byte)'!')) return ("RAR", "Arquivo RAR");
        if (StartsWith(h, 0x1F, 0x8B)) return ("GZIP", "Arquivo GZIP");
        if (StartsWith(h, 0x28, 0xB5, 0x2F, 0xFD)) return ("Zstandard", "Dados Zstandard");
        if (StartsWith(h, 0x89, (byte)'P', (byte)'N', (byte)'G')) return ("PNG", "Imagem PNG");
        if (StartsWith(h, 0xFF, 0xD8, 0xFF)) return ("JPEG", "Imagem JPEG");
        if (StartsWith(h, (byte)'G', (byte)'I', (byte)'F', (byte)'8')) return ("GIF", "Imagem GIF");
        if (n >= 12 && StartsWith(h, (byte)'R', (byte)'I', (byte)'F', (byte)'F') && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P')
            return ("WEBP", "Imagem WEBP");
        if (n >= 16 && Encoding.ASCII.GetString(h[..15]) == "SQLite format 3") return ("SQLite", "Banco SQLite 3");
        if (StartsWith(h, (byte)'%', (byte)'P', (byte)'D', (byte)'F')) return ("PDF", "Documento PDF");
        if (n >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(h) == 0x0A0D0D0A) return ("PCAPNG", "Captura de rede PCAPNG");
        if (n >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(h) is 0xA1B2C3D4 or 0xD4C3B2A1 or 0xA1B23C4D or 0x4D3CB2A1)
            return ("PCAP", "Captura de rede PCAP");
        if (StartsWith(h, (byte)'d', (byte)'e', (byte)'x', (byte)'\n')) return ("DEX", "Bytecode Android (DEX)");
        if (LooksLikeText(h)) return ("Texto", "Texto (ASCII/UTF-8)");

        var hint = entropy is { } e && e > 7.5
            ? $"Formato não identificado; entropia {e:0.00} bits/byte sugere dados compactados ou cifrados"
            : "Formato não identificado pelos bytes iniciais";
        return ("Desconhecido", hint);
    }

    /// <summary>
    /// PAK do Unreal: o "magic" fica no rodapé, em posição que varia por versão. Procura nos últimos 2 KB e lê a versão
    /// logo após o magic. Não presume nada sobre índice, compressão ou cifra.
    /// </summary>
    public static string? ProbeUnrealPak(Stream fs, long length)
    {
        if (length < 64) return null;
        var tail = (int)Math.Min(2048, length);
        var buf = new byte[tail];
        fs.Position = length - tail;
        fs.ReadExactly(buf);
        for (var i = tail - 8; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(i)) != UnrealPakMagic) continue;
            var version = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(i + 4));
            if (version is 0 or > 64) continue;
            return $"Pacote PAK do Unreal Engine, versão de formato {version} (rodapé no byte {length - tail + i}). " +
                   "Índice, compressão (ex.: Oodle/Zlib) e cifra precisam de análise específica.";
        }
        return null;
    }

    public static double Entropy(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return 0;
        Span<long> counts = stackalloc long[256];
        foreach (var b in data) counts[b]++;
        return EntropyFromCounts(counts, data.Length);
    }

    internal static double EntropyFromCounts(ReadOnlySpan<long> counts, long total)
    {
        double e = 0;
        foreach (var c in counts)
        {
            if (c == 0) continue;
            var p = c / (double)total;
            e -= p * Math.Log2(p);
        }
        return e;
    }

    private static async Task<(string, string, double)> HashAndEntropyAsync(string path, CancellationToken ct)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var counts = new long[256];
        long total = 0;
        var buffer = new byte[1 << 16];
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, buffer.Length, useAsync: true);
        int read;
        while ((read = await fs.ReadAsync(buffer, ct)) > 0)
        {
            sha256.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
            for (var i = 0; i < read; i++) counts[buffer[i]]++;
            total += read;
        }
        return (Convert.ToHexString(sha256.GetHashAndReset()), Convert.ToHexString(sha1.GetHashAndReset()),
            total == 0 ? 0 : EntropyFromCounts(counts, total));
    }

    private static bool StartsWith(ReadOnlySpan<byte> h, params byte[] magic) => h.Length >= magic.Length && h[..magic.Length].SequenceEqual(magic);

    private static bool LooksLikeText(ReadOnlySpan<byte> h)
    {
        if (h.Length < 8) return false;
        var printable = 0;
        foreach (var b in h) if (b is 9 or 10 or 13 || (b >= 0x20 && b < 0x7F) || b >= 0xC2) printable++;
        return printable >= h.Length * 0.95 && !h.Contains((byte)0);
    }
}
