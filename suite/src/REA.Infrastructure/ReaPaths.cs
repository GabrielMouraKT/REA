namespace REA.Infrastructure;

/// <summary>
/// Pastas de dados do REA. Padrão: %LOCALAPPDATA%\REA. Pode ser trocado pela variável REA_HOME
/// (útil para testes e para instalações portáteis).
/// </summary>
public sealed class ReaPaths
{
    public string Root { get; }
    public string DataDirectory => Path.Combine(Root, "data");
    public string DatabasePath => Path.Combine(DataDirectory, "rea.db");
    public string BackupDirectory => Path.Combine(Root, "backups");
    public string LogDirectory => Path.Combine(Root, "logs");

    public ReaPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public static ReaPaths Default()
    {
        var custom = Environment.GetEnvironmentVariable("REA_HOME");
        var root = string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REA")
            : custom;
        return new ReaPaths(root);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string ConnectionString => $"Data Source={DatabasePath};Foreign Keys=True";
}
