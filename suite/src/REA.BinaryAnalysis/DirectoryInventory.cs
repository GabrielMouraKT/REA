using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace REA.BinaryAnalysis;

public sealed record InventoryEntry(string RelativePath, long Size, string Sha256, string Format, DateTime LastWriteUtc);

public sealed record SkippedPath(string RelativePath, string Reason);

public sealed record DirectoryManifest(
    int SchemaVersion,
    string RootPath,
    DateTime CreatedUtc,
    IReadOnlyList<InventoryEntry> Files,
    IReadOnlyList<SkippedPath> Skipped)
{
    public long TotalBytes => Files.Sum(f => f.Size);
}

public sealed record InventoryProgress(int FilesDone, long BytesDone, string CurrentFile);

public sealed record DuplicateGroup(string Sha256, long Size, IReadOnlyList<string> Paths)
{
    /// <summary>Espaço que seria liberado mantendo uma única cópia.</summary>
    public long WastedBytes => Size * (Paths.Count - 1);
}

public sealed record MovedFile(string From, string To, string Sha256);

public sealed record ChangedFile(string RelativePath, InventoryEntry Before, InventoryEntry After);

public sealed record ManifestComparison(
    IReadOnlyList<InventoryEntry> Added,
    IReadOnlyList<InventoryEntry> Removed,
    IReadOnlyList<ChangedFile> Changed,
    IReadOnlyList<MovedFile> Moved,
    int Unchanged)
{
    public bool HasDifferences => Added.Count + Removed.Count + Changed.Count + Moved.Count > 0;
}

/// <summary>
/// Inventário de uma pasta: caminho relativo, tamanho, SHA-256 e formato de cada arquivo. Pontos de reanálise
/// (junções, links simbólicos) não são seguidos e aparecem como "ignorados" com o motivo; nada é omitido em silêncio.
/// </summary>
public static class DirectoryInventory
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static async Task<DirectoryManifest> ScanAsync(string root, IProgress<InventoryProgress>? progress = null,
        CancellationToken ct = default)
    {
        var rootInfo = new DirectoryInfo(Path.GetFullPath(root));
        if (!rootInfo.Exists) throw new DirectoryNotFoundException($"Pasta não encontrada: {root}");

        var files = new List<InventoryEntry>();
        var skipped = new List<SkippedPath>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootInfo);
        long bytes = 0;
        var buffer = new byte[1 << 16];

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            FileSystemInfo[] children;
            try { children = dir.GetFileSystemInfos(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                skipped.Add(new SkippedPath(Relative(rootInfo, dir.FullName), $"Pasta não pôde ser lida: {ex.Message}"));
                continue;
            }

            foreach (var child in children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Relative(rootInfo, child.FullName);
                if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    skipped.Add(new SkippedPath(rel, "Link ou junção (não seguido)"));
                    continue;
                }
                if (child is DirectoryInfo d) { pending.Push(d); continue; }

                var file = (FileInfo)child;
                try
                {
                    progress?.Report(new InventoryProgress(files.Count, bytes, rel));
                    var hash = await HashAsync(file.FullName, buffer, ct);
                    var (format, _) = FileIdentifier.DetectFormat(file.FullName);
                    files.Add(new InventoryEntry(rel, file.Length, hash, format, file.LastWriteTimeUtc));
                    bytes += file.Length;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    skipped.Add(new SkippedPath(rel, $"Arquivo não pôde ser lido: {ex.Message}"));
                }
            }
        }

        files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        progress?.Report(new InventoryProgress(files.Count, bytes, ""));
        return new DirectoryManifest(SchemaVersion, rootInfo.FullName, DateTime.UtcNow, files, skipped);
    }

    public static IReadOnlyList<DuplicateGroup> FindDuplicates(DirectoryManifest manifest) =>
        manifest.Files
            .Where(f => f.Size > 0)
            .GroupBy(f => f.Sha256)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup(g.Key, g.First().Size, g.Select(f => f.RelativePath).ToList()))
            .OrderByDescending(g => g.WastedBytes)
            .ToList();

    /// <summary>
    /// Compara dois inventários pelo caminho relativo. Um arquivo que sumiu de um caminho e apareceu, com o mesmo
    /// conteúdo, num caminho novo é contado como "movido" (não como removido + adicionado).
    /// </summary>
    public static ManifestComparison Compare(DirectoryManifest before, DirectoryManifest after)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var a = before.Files.ToDictionary(f => f.RelativePath, cmp);
        var b = after.Files.ToDictionary(f => f.RelativePath, cmp);

        var changed = new List<ChangedFile>();
        var unchanged = 0;
        foreach (var (path, old) in a)
        {
            if (!b.TryGetValue(path, out var now)) continue;
            if (old.Sha256 == now.Sha256) unchanged++;
            else changed.Add(new ChangedFile(path, old, now));
        }

        var removed = a.Values.Where(f => !b.ContainsKey(f.RelativePath)).ToList();
        var added = b.Values.Where(f => !a.ContainsKey(f.RelativePath)).ToList();

        var moved = new List<MovedFile>();
        var addedByHash = added.GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => new Queue<InventoryEntry>(g));
        foreach (var r in removed.ToList())
        {
            if (!addedByHash.TryGetValue(r.Sha256, out var candidates) || candidates.Count == 0) continue;
            var target = candidates.Dequeue();
            moved.Add(new MovedFile(r.RelativePath, target.RelativePath, r.Sha256));
            removed.Remove(r);
            added.Remove(target);
        }

        return new ManifestComparison(
            added.OrderBy(f => f.RelativePath, cmp).ToList(),
            removed.OrderBy(f => f.RelativePath, cmp).ToList(),
            changed.OrderBy(c => c.RelativePath, cmp).ToList(),
            moved.OrderBy(m => m.From, cmp).ToList(),
            unchanged);
    }

    public static async Task SaveAsync(DirectoryManifest manifest, string path, CancellationToken ct = default)
    {
        var temp = path + ".tmp";
        await using (var fs = File.Create(temp))
            await JsonSerializer.SerializeAsync(fs, manifest, Json, ct);
        File.Move(temp, path, overwrite: true);
    }

    public static async Task<DirectoryManifest> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        var m = await JsonSerializer.DeserializeAsync<DirectoryManifest>(fs, Json, ct)
                ?? throw new InvalidDataException("Manifesto vazio.");
        if (m.SchemaVersion != SchemaVersion)
            throw new InvalidDataException($"Versão de manifesto {m.SchemaVersion} não suportada (esperado {SchemaVersion}).");
        return m;
    }

    private static string Relative(DirectoryInfo root, string full)
    {
        var rel = Path.GetRelativePath(root.FullName, full);
        return rel == "." ? "" : rel.Replace('\\', '/');
    }

    private static async Task<string> HashAsync(string path, byte[] buffer, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, buffer.Length, useAsync: true);
        int read;
        while ((read = await fs.ReadAsync(buffer, ct)) > 0) sha.AppendData(buffer, 0, read);
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}
