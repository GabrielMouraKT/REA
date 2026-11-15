using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace REA.Desktop.Services;

public enum NotificationKind { Success, Info, Warning, Error }

public sealed partial class Notification(string message, NotificationKind kind) : ObservableObject
{
    public string Message { get; } = message;
    public NotificationKind Kind { get; } = kind;
    /// <summary>Rótulo em texto: o estado nunca é indicado só por cor.</summary>
    public string Label => Kind switch
    {
        NotificationKind.Success => "Concluído",
        NotificationKind.Info => "Info",
        NotificationKind.Warning => "Atenção",
        _ => "Erro",
    };
}

/// <summary>Notificações não intrusivas no canto da janela, com fechamento automático.</summary>
public sealed partial class NotificationService : ObservableObject
{
    public ObservableCollection<Notification> Items { get; } = [];

    public void Success(string message) => Show(message, NotificationKind.Success);
    public void Info(string message) => Show(message, NotificationKind.Info);
    public void Warning(string message) => Show(message, NotificationKind.Warning);
    public void Error(string message) => Show(message, NotificationKind.Error);

    public void Show(string message, NotificationKind kind)
    {
        void Add()
        {
            var n = new Notification(message, kind);
            Items.Add(n);
            while (Items.Count > 4) Items.RemoveAt(0);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == NotificationKind.Error ? 10 : 5) };
            timer.Tick += (_, _) => { timer.Stop(); Items.Remove(n); };
            timer.Start();
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Add(); else dispatcher.Invoke(Add);
    }

    [RelayCommand]
    private void Dismiss(Notification n) => Items.Remove(n);
}

public interface IDialogService
{
    /// <summary>Confirmação para ações destrutivas. Nunca usada em operações rotineiras.</summary>
    bool Confirm(string title, string message, string confirmLabel);
    string? Prompt(string title, string message, string initialValue, string confirmLabel);
}

public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message, string confirmLabel)
    {
        var dlg = new Views.DialogWindow(title, message, confirmLabel, input: null) { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true;
    }

    public string? Prompt(string title, string message, string initialValue, string confirmLabel)
    {
        var dlg = new Views.DialogWindow(title, message, confirmLabel, input: initialValue) { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.InputValue : null;
    }
}

/// <summary>Executa serviços em um escopo de DI próprio (DbContext novo por operação).</summary>
public sealed class ScopedRunner(IServiceScopeFactory scopes)
{
    public async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
