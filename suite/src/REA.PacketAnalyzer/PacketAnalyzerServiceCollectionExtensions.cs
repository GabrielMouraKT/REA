using Microsoft.Extensions.DependencyInjection;
using REA.Core;

namespace REA.PacketAnalyzer;

public static class PacketAnalyzerServiceCollectionExtensions
{
    public static IServiceCollection AddReaPacketAnalyzer(this IServiceCollection services)
    {
        services.AddSingleton<ICaptureFileAnalyzer, CaptureFileAnalyzer>();
        services.AddSingleton<IFileHasher, Sha256FileHasher>();
        return services;
    }
}
