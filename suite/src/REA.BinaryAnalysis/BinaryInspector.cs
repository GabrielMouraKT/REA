using Microsoft.Extensions.DependencyInjection;
using REA.Core;

namespace REA.BinaryAnalysis;

/// <summary>Fatos de um arquivo para o registro de evidência: formato pelos bytes, arquitetura e um resumo legível.</summary>
internal sealed class BinaryInspector : IBinaryInspector
{
    public Task<BinaryFacts> InspectAsync(string path, CancellationToken ct = default) => Task.Run(() =>
    {
        var (format, description) = FileIdentifier.DetectFormat(path);
        switch (format)
        {
            case "PE":
                var pe = PeAnalyzer.Analyze(path, ct);
                var parts = new List<string>
                {
                    pe.IsDll ? "Biblioteca (DLL)" : "Executável",
                    pe.Subsystem,
                    $"{pe.Sections.Count} seção(ões)",
                    $"{pe.Imports.Count} biblioteca(s) importada(s)",
                };
                if (pe.Exports.Count > 0) parts.Add($"{pe.Exports.Count} função(ões) exportada(s)");
                if (pe.IsDotNet) parts.Add($".NET {pe.DotNetRuntime}");
                if (pe.Version?.ProductName is { } product) parts.Add($"{product} {pe.Version.ProductVersion}".Trim());
                if (pe.HasEmbeddedSignature) parts.Add("assinatura embutida (validade não verificada)");
                return new BinaryFacts(format, description, pe.Machine, string.Join(" · ", parts));
            case "ELF":
                var elf = ElfAnalyzer.Analyze(path);
                return new BinaryFacts(format, description, elf.Machine,
                    $"{elf.FileType} · {elf.OsAbi} · {elf.SectionCount} seção(ões)");
            default:
                return new BinaryFacts(format, description, null, null);
        }
    }, ct);
}

public static class BinaryAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddReaBinaryAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<IBinaryInspector, BinaryInspector>();
        return services;
    }
}
