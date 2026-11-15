using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using REA.Core;

namespace REA.Infrastructure;

/// <summary>
/// Backup do banco usando a API de backup online do SQLite (consistente mesmo com o app aberto).
/// Arquivos: backups/rea-AAAAMMDD-HHMMSS-fff-{motivo}.db
/// </summary>
internal sealed partial class SqliteBackupService(ReaPaths paths, IClock clock, ILogger<SqliteBackupService> logger) : IBackupService
{
    private const int KeepAutomatic = 14;
    private static readonly TimeSpan AutoInterval = TimeSpan.FromHours(24);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    [GeneratedRegex(@"^rea-(\d{8}-\d{6}-\d{3})-(manual|auto|antes-de-restaurar)\.db$")]
    private static partial Regex FileNamePattern();

    public async Task<BackupInfo> CreateAsync(string reason, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try { return CreateCore(reason); }
        finally { Gate.Release(); }
    }

    public Task<IReadOnlyList<BackupInfo>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(paths.BackupDirectory)) return Task.FromResult<IReadOnlyList<BackupInfo>>([]);
        var list = Directory.EnumerateFiles(paths.BackupDirectory, "rea-*.db")
            .Select(Parse)
            .OfType<BackupInfo>()
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<BackupInfo>>(list);
    }

    public async Task<BackupInfo?> EnsureRecentAsync(CancellationToken ct = default)
    {
        var latest = (await ListAsync(ct)).FirstOrDefault();
        if (latest is not null && clock.Now - latest.CreatedAt < AutoInterval) return null;
        if (!File.Exists(paths.DatabasePath)) return null;
        var created = await CreateAsync("auto", ct);
        Prune();
        return created;
    }

    public async Task<OperationResult> RestoreAsync(string fileName, CancellationToken ct = default)
    {
        // Só aceita nome de arquivo da pasta de backup (sem caminho), evitando path traversal.
        if (fileName != Path.GetFileName(fileName) || !FileNamePattern().IsMatch(fileName))
            return OperationResult.Fail("Backup não encontrado.");
        var source = Path.Combine(paths.BackupDirectory, fileName);
        if (!File.Exists(source)) return OperationResult.Fail("Backup não encontrado.");

        var check = Validate(source);
        if (check is not null) return OperationResult.Fail(check);

        await Gate.WaitAsync(ct);
        try
        {
            var safety = CreateCore("antes-de-restaurar");
            logger.LogInformation("Backup de segurança {File} criado antes da restauração", safety.FileName);
            SqliteConnection.ClearAllPools();
            using var from = Open(source, readOnly: true);
            using var to = Open(paths.DatabasePath, readOnly: false);
            from.BackupDatabase(to);
            SqliteConnection.ClearAllPools();
            logger.LogInformation("Banco restaurado a partir de {File}", fileName);
            return OperationResult.Ok();
        }
        catch (SqliteException ex)
        {
            logger.LogError(ex, "Falha ao restaurar {File}", fileName);
            return OperationResult.Fail($"Não foi possível restaurar o backup: {ex.Message}");
        }
        finally { Gate.Release(); }
    }

    private BackupInfo CreateCore(string reason)
    {
        Directory.CreateDirectory(paths.BackupDirectory);
        var stamp = clock.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var target = Path.Combine(paths.BackupDirectory, $"rea-{stamp}-{reason}.db");
        using (var from = Open(paths.DatabasePath, readOnly: false))
        using (var to = Open(target, readOnly: false))
        {
            from.BackupDatabase(to);
        }
        logger.LogInformation("Backup {File} criado ({Reason})", Path.GetFileName(target), reason);
        return Parse(target) ?? throw new InvalidOperationException("Nome de backup inválido gerado.");
    }

    private void Prune()
    {
        var autos = Directory.EnumerateFiles(paths.BackupDirectory, "rea-*-auto.db")
            .Select(Parse).OfType<BackupInfo>().OrderByDescending(b => b.CreatedAt).Skip(KeepAutomatic);
        foreach (var old in autos)
        {
            try { File.Delete(old.FullPath); }
            catch (IOException ex) { logger.LogWarning(ex, "Não foi possível remover backup antigo {File}", old.FileName); }
        }
    }

    private static string? Validate(string path)
    {
        try
        {
            using var conn = Open(path, readOnly: true);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            if (cmd.ExecuteScalar() as string != "ok") return "O arquivo de backup está corrompido.";
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            if (Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                return "O arquivo não é um backup do REA.";
            return null;
        }
        catch (SqliteException)
        {
            return "O arquivo de backup não é um banco SQLite válido.";
        }
    }

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private static BackupInfo? Parse(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        var m = FileNamePattern().Match(name);
        if (!m.Success) return null;
        if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var local)) return null;
        var reason = m.Groups[2].Value switch
        {
            "manual" => "Manual",
            "auto" => "Automático",
            _ => "Antes de restaurar",
        };
        return new BackupInfo(name, fullPath, new DateTimeOffset(local), new FileInfo(fullPath).Length, reason);
    }
}
