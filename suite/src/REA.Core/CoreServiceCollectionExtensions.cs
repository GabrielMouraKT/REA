using Microsoft.Extensions.DependencyInjection;

namespace REA.Core;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddReaCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IOperatorContext, WindowsOperatorContext>();
        services.AddScoped<ProjectService>();
        services.AddScoped<EnvironmentService>();
        services.AddScoped<DashboardService>();
        return services;
    }
}
