using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace REA.ToolIntegration;

public sealed record GhidraInstallation(string Directory, string Version, string AnalyzeHeadless);

public sealed record GhidraFunction(string Name, string Entry, long Size, bool Thunk, string? Signature);

public sealed record GhidraExternal(string Library, string Name);

public sealed record GhidraString(string Address, string Value);

public sealed record GhidraExport(
    string Program,
    string Language,
    string Compiler,
    string ImageBase,
    IReadOnlyList<GhidraFunction> Functions,
    bool FunctionsTruncated,
    IReadOnlyList<GhidraExternal> Externals,
    IReadOnlyList<GhidraString> Strings,
    bool StringsTruncated);

public enum GhidraRunStatus { Concluido, Falhou, Cancelado, TempoEsgotado }

public sealed record GhidraRunResult(
    GhidraRunStatus Status,
    GhidraExport? Export,
    string? Error,
    TimeSpan Elapsed,
    IReadOnlyList<string> Log);

/// <summary>
/// Executa o Ghidra em modo headless (analyzeHeadless) num projeto temporário que é apagado ao final. O binário
/// original nunca é alterado. Funções, externos e strings são exportados pelo script ReaExport.java em JSON.
/// </summary>
public static class GhidraHeadless
{
    public const string ScriptName = "ReaExport.java";
    public const int DefaultMaxFunctions = 200_000;
    public const int DefaultMaxStrings = 100_000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Procura no caminho configurado, depois em GHIDRA_INSTALL_DIR. Nunca baixa nem instala nada.</summary>
    public static GhidraInstallation? Locate(string? configuredDirectory = null)
    {
        foreach (var dir in new[] { configuredDirectory, Environment.GetEnvironmentVariable("GHIDRA_INSTALL_DIR") })
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var install = Inspect(dir.Trim().Trim('"'));
            if (install is not null) return install;
        }
        return null;
    }

    public static GhidraInstallation? Inspect(string directory)
    {
        var bat = Path.Combine(directory, "support", "analyzeHeadless.bat");
        var props = Path.Combine(directory, "Ghidra", "application.properties");
        if (!File.Exists(bat) || !File.Exists(props)) return null;
        var version = File.ReadLines(props)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("application.version=", StringComparison.Ordinal))?["application.version=".Length..]
            ?? "desconhecida";
        return new GhidraInstallation(Path.GetFullPath(directory), version, bat);
    }

    public static async Task<GhidraRunResult> AnalyzeAsync(GhidraInstallation ghidra, string binaryPath,
        Action<string>? onLog = null, TimeSpan? timeout = null, int maxFunctions = DefaultMaxFunctions,
        int maxStrings = DefaultMaxStrings, string? importName = null, string? workRoot = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (!File.Exists(binaryPath))
            return new GhidraRunResult(GhidraRunStatus.Falhou, null, $"Arquivo não encontrado: {binaryPath}", sw.Elapsed, []);

        var work = Path.Combine(workRoot ?? Path.Combine(Path.GetTempPath(), "rea-ghidra"), Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(work, "projeto");
        var scriptDir = Path.Combine(work, "scripts");
        var output = Path.Combine(work, "export.json");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(scriptDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(scriptDir, ScriptName), ExportScript, ct);
            // Com nome de exibição, importa uma cópia temporária com esse nome (o Ghidra usa o nome do arquivo).
            var importPath = Path.GetFullPath(binaryPath);
            if (SafeFileName(importName) is { } safeName)
            {
                var inputDir = Path.Combine(work, "entrada");
                Directory.CreateDirectory(inputDir);
                importPath = Path.Combine(inputDir, safeName);
                File.Copy(binaryPath, importPath);
            }
            string[] args =
            [
                ghidra.AnalyzeHeadless, projectDir, "REA", "-import", importPath,
                "-scriptPath", scriptDir, "-postScript", ScriptName, output,
                maxFunctions.ToString(System.Globalization.CultureInfo.InvariantCulture),
                maxStrings.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-deleteProject",
            ];
            var bad = args.FirstOrDefault(a => a.IndexOfAny(['"', '%', '\r', '\n']) >= 0);
            if (bad is not null)
                return new GhidraRunResult(GhidraRunStatus.Falhou, null,
                    $"Caminho com caractere não suportado pelo Ghidra no Windows (\" ou %): {bad}", sw.Elapsed, []);

            // .bat exige cmd.exe; /s com aspas externas preserva as aspas de cada argumento.
            var psi = new ProcessStartInfo("cmd.exe")
            {
                Arguments = "/d /s /c \"" + string.Join(' ', args.Select(a => "\"" + a + "\"")) + "\"",
                WorkingDirectory = work,
            };
            ProcessOutcome outcome;
            try { outcome = await ProcessRunner.RunAsync(psi, onLog, timeout, ct); }
            catch (OperationCanceledException)
            {
                return new GhidraRunResult(GhidraRunStatus.Cancelado, null, "Análise cancelada; processo do Ghidra encerrado.", sw.Elapsed, []);
            }

            if (outcome.TimedOut)
                return new GhidraRunResult(GhidraRunStatus.TempoEsgotado, null,
                    $"Tempo limite de {timeout?.TotalMinutes:0.#} min atingido; processo do Ghidra encerrado.", sw.Elapsed, outcome.Lines);

            if (!File.Exists(output))
            {
                var reason = outcome.Lines.LastOrDefault(l => l.Contains("ERROR", StringComparison.Ordinal))
                             ?? $"O Ghidra terminou com código {outcome.ExitCode} sem gerar o resultado.";
                return new GhidraRunResult(GhidraRunStatus.Falhou, null, reason, sw.Elapsed, outcome.Lines);
            }

            await using var fs = File.OpenRead(output);
            var export = await JsonSerializer.DeserializeAsync<GhidraExport>(fs, Json, ct);
            return export is null
                ? new GhidraRunResult(GhidraRunStatus.Falhou, null, "Resultado do Ghidra vazio.", sw.Elapsed, outcome.Lines)
                : new GhidraRunResult(GhidraRunStatus.Concluido, export, null, sw.Elapsed, outcome.Lines);
        }
        catch (JsonException ex)
        {
            return new GhidraRunResult(GhidraRunStatus.Falhou, null, $"Resultado do Ghidra ilegível: {ex.Message}", sw.Elapsed, []);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { onLog?.Invoke($"Aviso: pasta temporária não removida: {work}"); }
            catch (UnauthorizedAccessException) { onLog?.Invoke($"Aviso: pasta temporária não removida: {work}"); }
        }
    }

    private static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) || c is '%' or '"' ? '_' : c).ToArray());
        return cleaned.Trim('.', ' ').Length == 0 ? null : cleaned[..Math.Min(cleaned.Length, 120)];
    }

    /// <summary>Script Java executado dentro do Ghidra. Escreve JSON à mão para não depender de bibliotecas.</summary>
    public const string ExportScript = """
        // Exporta funções, externos e strings para JSON (REA Advanced Suite).
        //@category REA
        import ghidra.app.script.GhidraScript;
        import ghidra.program.model.listing.*;
        import ghidra.program.model.symbol.*;
        import java.io.*;
        import java.nio.charset.StandardCharsets;

        public class ReaExport extends GhidraScript {
            private static void str(Writer w, String s) throws IOException {
                if (s == null) { w.write("null"); return; }
                StringBuilder b = new StringBuilder("\"");
                for (int i = 0; i < s.length(); i++) {
                    char c = s.charAt(i);
                    switch (c) {
                        case '"': b.append("\\\""); break;
                        case '\\': b.append("\\\\"); break;
                        case '\n': b.append("\\n"); break;
                        case '\r': b.append("\\r"); break;
                        case '\t': b.append("\\t"); break;
                        default:
                            if (c < 0x20) b.append(String.format("\\u%04x", (int) c)); else b.append(c);
                    }
                }
                w.write(b.append('"').toString());
            }

            @Override
            public void run() throws Exception {
                String[] args = getScriptArgs();
                if (args.length < 3) throw new IllegalArgumentException("uso: ReaExport <saida.json> <maxFuncoes> <maxStrings>");
                int maxFunctions = Integer.parseInt(args[1]);
                int maxStrings = Integer.parseInt(args[2]);
                File tmp = new File(args[0] + ".part");
                try (Writer w = new BufferedWriter(new OutputStreamWriter(new FileOutputStream(tmp), StandardCharsets.UTF_8))) {
                    w.write("{\"program\":"); str(w, currentProgram.getName());
                    w.write(",\"language\":"); str(w, currentProgram.getLanguageID().getIdAsString());
                    w.write(",\"compiler\":"); str(w, currentProgram.getCompilerSpec().getCompilerSpecID().getIdAsString());
                    w.write(",\"imageBase\":"); str(w, currentProgram.getImageBase().toString());

                    w.write(",\"functions\":[");
                    FunctionIterator it = currentProgram.getFunctionManager().getFunctions(true);
                    int n = 0; boolean truncated = false;
                    while (it.hasNext()) {
                        monitor.checkCancelled();
                        Function f = it.next();
                        if (n >= maxFunctions) { truncated = true; break; }
                        if (n++ > 0) w.write(',');
                        w.write("{\"name\":"); str(w, f.getName());
                        w.write(",\"entry\":"); str(w, f.getEntryPoint().toString());
                        w.write(",\"size\":" + f.getBody().getNumAddresses());
                        w.write(",\"thunk\":" + f.isThunk());
                        w.write(",\"signature\":"); str(w, f.getPrototypeString(false, false));
                        w.write('}');
                    }
                    w.write("],\"functionsTruncated\":" + truncated);

                    w.write(",\"externals\":[");
                    n = 0;
                    FunctionIterator ext = currentProgram.getFunctionManager().getExternalFunctions();
                    while (ext.hasNext()) {
                        monitor.checkCancelled();
                        Function f = ext.next();
                        ExternalLocation loc = f.getExternalLocation();
                        if (n++ > 0) w.write(',');
                        w.write("{\"library\":"); str(w, loc == null ? "" : loc.getLibraryName());
                        w.write(",\"name\":"); str(w, f.getName());
                        w.write('}');
                    }

                    w.write("],\"strings\":[");
                    n = 0; truncated = false;
                    DataIterator data = currentProgram.getListing().getDefinedData(true);
                    while (data.hasNext()) {
                        monitor.checkCancelled();
                        Data d = data.next();
                        if (!d.hasStringValue()) continue;
                        if (n >= maxStrings) { truncated = true; break; }
                        Object v = d.getValue();
                        if (n++ > 0) w.write(',');
                        w.write("{\"address\":"); str(w, d.getAddress().toString());
                        w.write(",\"value\":"); str(w, v == null ? "" : v.toString());
                        w.write('}');
                    }
                    w.write("],\"stringsTruncated\":" + truncated + "}");
                }
                File out = new File(args[0]);
                if (!tmp.renameTo(out)) throw new IOException("não foi possível gravar " + out);
                println("REA: exportação concluída em " + out);
            }
        }
        """;
}
