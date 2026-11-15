using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using REA.Core;
using REA.Desktop.Services;
using REA.Desktop.ViewModels;
using REA.Infrastructure;
using REA.NetworkCapture;
using REA.PacketAnalyzer;
using Serilog;

namespace REA.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = ReaPaths.Default();
        paths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(paths.LogDirectory, "rea-.log"), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddReaCore();
                services.AddReaInfrastructure(paths);
                services.AddReaPacketAnalyzer();
                services.AddReaNetworkCapture();
                services.AddSingleton<NetworkCaptureViewModel>();
                services.AddTransient<PacketInspectorViewModel>();
                services.AddTransient<TcpStreamsViewModel>();
                services.AddTransient<CapturesViewModel>();
                services.AddSingleton<NotificationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<ScopedRunner>();
                services.AddSingleton<IActivityLogReader, ActivityLogReader>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<INavigator>(sp => sp.GetRequiredService<ShellViewModel>());
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProjectsViewModel>();
                services.AddTransient<EnvironmentsViewModel>();
                services.AddTransient<ActivityViewModel>();
                services.AddTransient<DatabaseViewModel>();
                services.AddTransient<ToolsViewModel>();
                services.AddTransient<BackupViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        try
        {
            await _host.StartAsync();
            await _host.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            Log.Information("REA Advanced Suite iniciado. Dados em {Root}", paths.Root);
            var recovered = await _host.Services.GetRequiredService<ScopedRunner>()
                .RunAsync<CaptureService, int>(s => s.RecoverInterruptedAsync());
            if (recovered > 0) Log.Warning("{Count} captura(s) interrompida(s) recuperada(s)", recovered);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Falha ao iniciar o banco");
            MessageBox.Show($"Não foi possível abrir o banco de dados em {paths.DatabasePath}.\n\n{ex.Message}",
                "REA Advanced Suite", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        var shell = _host.Services.GetRequiredService<ShellViewModel>();
        await shell.NavigateAsync(NavigationCatalog.Dashboard);

        // Backup automático diário em segundo plano (não bloqueia a interface).
        _ = Task.Run(async () =>
        {
            try
            {
                var b = await _host.Services.GetRequiredService<IBackupService>().EnsureRecentAsync();
                if (b is not null) Log.Information("Backup automático criado: {File}", b.FileName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Falha no backup automático");
                _host.Services.GetRequiredService<NotificationService>().Warning("O backup automático falhou. Veja os logs.");
            }
        });
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Erro não tratado na interface");
        _host?.Services.GetService<NotificationService>()?.Error($"Erro inesperado: {e.Exception.Message}");
        e.Handled = true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try { await _host.Services.GetRequiredService<NetworkCaptureViewModel>().StopIfRunningAsync(); }
            catch (Exception ex) { Log.Error(ex, "Falha ao encerrar a captura em andamento"); }
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}
