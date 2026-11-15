using System.Diagnostics;
using System.Text;

namespace REA.ToolIntegration;

public sealed record ProcessOutcome(int ExitCode, bool TimedOut, IReadOnlyList<string> Lines);

/// <summary>
/// Executa um processo externo com argumentos em lista (sem montar linha de comando com texto do usuário),
/// repassa cada linha de saída ao vivo e, se cancelado ou estourar o tempo, encerra a árvore inteira de processos.
/// </summary>
public static class ProcessRunner
{
    public const int MaxRetainedLines = 20_000;

    public static async Task<ProcessOutcome> RunAsync(ProcessStartInfo psi, Action<string>? onLine = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding ??= Encoding.UTF8;
        psi.StandardErrorEncoding ??= Encoding.UTF8;

        var lines = new List<string>();
        var dropped = 0;
        void Accept(string? line)
        {
            if (line is null) return;
            lock (lines)
            {
                if (lines.Count < MaxRetainedLines) lines.Add(line);
                else dropped++;
            }
            onLine?.Invoke(line);
        }

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => Accept(e.Data);
        p.ErrorDataReceived += (_, e) => Accept(e.Data);
        if (!p.Start()) throw new InvalidOperationException($"Não foi possível iniciar {psi.FileName}.");
        p.StandardInput.Close();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var timeoutCts = timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await p.WaitForExitAsync(linked.Token);
            p.WaitForExit(); // garante que os eventos de saída terminaram
        }
        catch (OperationCanceledException)
        {
            KillTree(p);
            if (ct.IsCancellationRequested) throw;
            if (dropped > 0) lines.Add($"… {dropped} linhas de log não guardadas.");
            return new ProcessOutcome(-1, true, lines);
        }
        if (dropped > 0) lines.Add($"… {dropped} linhas de log não guardadas.");
        return new ProcessOutcome(p.ExitCode, false, lines);
    }

    private static void KillTree(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        try { p.WaitForExit(10_000); } catch (InvalidOperationException) { }
    }
}
