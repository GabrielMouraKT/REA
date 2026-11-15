using System.Text;

namespace REA.BinaryAnalysis;

public enum StringEncodingKind { Ascii, Utf16LE }

public sealed record FoundString(long Offset, StringEncodingKind Encoding, string Value);

/// <param name="Truncated">Verdadeiro quando o limite de resultados foi atingido e a leitura parou antes do fim.</param>
/// <param name="BytesScanned">Quantos bytes do arquivo foram examinados (cobertura real).</param>
public sealed record StringScanResult(IReadOnlyList<FoundString> Strings, bool Truncated, long BytesScanned, long FileSize);

/// <summary>
/// Extrai sequências de caracteres imprimíveis (ASCII e UTF-16LE) lendo o arquivo em blocos, sem carregá-lo inteiro.
/// Strings UTF-16 são procuradas nos dois alinhamentos (par e ímpar).
/// </summary>
public static class StringExtractor
{
    public const int DefaultMinLength = 4;
    public const int DefaultMaxResults = 100_000;
    /// <summary>Strings maiores que isto são cortadas (o deslocamento continua correto).</summary>
    public const int MaxValueLength = 4096;

    public static async Task<StringScanResult> ExtractAsync(string path, int minLength = DefaultMinLength,
        int maxResults = DefaultMaxResults, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
        return await ExtractAsync(fs, minLength, maxResults, ct);
    }

    public static async Task<StringScanResult> ExtractAsync(Stream stream, int minLength = DefaultMinLength,
        int maxResults = DefaultMaxResults, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minLength, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        var scanner = new Scanner(minLength, maxResults);
        var buffer = new byte[1 << 16];
        long scanned = 0;
        int read;
        while (!scanner.Full && (read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            var i = 0;
            for (; i < read && !scanner.Full; i++) scanner.Feed(buffer[i], scanned + i);
            scanned += i;
        }
        var truncated = scanner.Full;
        if (!truncated) scanner.Finish();
        var list = scanner.Results;
        list.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return new StringScanResult(list, truncated, scanned, stream.CanSeek ? stream.Length : scanned);
    }

    private sealed class Scanner(int minLength, int maxResults)
    {
        public readonly List<FoundString> Results = [];
        public bool Full => Results.Count >= maxResults;

        private readonly StringBuilder _ascii = new();
        private long _asciiStart;
        private int _asciiLength;
        private readonly StringBuilder[] _wide = [new(), new()];
        private readonly long[] _wideStart = new long[2];
        private readonly int[] _wideLength = new int[2];
        private int _prev = -1;

        public void Feed(byte b, long offset)
        {
            if (IsPrintable(b))
            {
                if (_asciiLength == 0) _asciiStart = offset;
                if (_ascii.Length < MaxValueLength) _ascii.Append((char)b);
                _asciiLength++;
            }
            else FlushAscii();

            if (_prev >= 0)
            {
                var a = (int)((offset - 1) & 1);
                if (IsPrintable((byte)_prev) && b == 0)
                {
                    if (_wideLength[a] == 0) _wideStart[a] = offset - 1;
                    if (_wide[a].Length < MaxValueLength) _wide[a].Append((char)_prev);
                    _wideLength[a]++;
                }
                else FlushWide(a);
            }
            _prev = b;
        }

        public void Finish()
        {
            FlushAscii();
            FlushWide(0);
            FlushWide(1);
        }

        private void FlushAscii()
        {
            if (_asciiLength >= minLength && !Full)
                Results.Add(new FoundString(_asciiStart, StringEncodingKind.Ascii, _ascii.ToString()));
            _ascii.Clear();
            _asciiLength = 0;
        }

        private void FlushWide(int a)
        {
            if (_wideLength[a] >= minLength && !Full)
                Results.Add(new FoundString(_wideStart[a], StringEncodingKind.Utf16LE, _wide[a].ToString()));
            _wide[a].Clear();
            _wideLength[a] = 0;
        }

        private static bool IsPrintable(byte b) => b is >= 0x20 and < 0x7F or 0x09;
    }
}

/// <summary>Leitura de uma faixa de bytes de um arquivo para o visualizador hexadecimal.</summary>
public static class FileRange
{
    public const int MaxRangeBytes = 1 << 20;

    public static async Task<byte[]> ReadAsync(string path, long offset, int count, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        count = Math.Clamp(count, 0, MaxRangeBytes);
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        if (offset >= fs.Length) return [];
        var len = (int)Math.Min(count, fs.Length - offset);
        var buffer = new byte[len];
        fs.Position = offset;
        await fs.ReadExactlyAsync(buffer, ct);
        return buffer;
    }
}
