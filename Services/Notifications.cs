using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace DragonDen.ModManager.Services;

public sealed class Notifications
{
    private static readonly Lazy<Notifications> _current = new(() => new Notifications());

    private WindowNotificationManager? _notificationManager;

    private Notifications()
    {
    }

    public static Notifications Current => _current.Value;

    public ObservableCollection<Notification> Others { get; } = new();
    public InstallJob? CurrentInstall { get; private set; }

    public event Action? OnInstallChanged;

    public void Attach(TopLevel topLevel)
    {
        _notificationManager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 4,
            Margin = new Thickness(12)
        };
    }

    public void ShowInfo(string title, string message, bool ephemeral = true, TimeSpan? timeout = null)
    {
        ShowCore(title, message, NotificationType.Information, ephemeral = true, timeout = TimeSpan.FromSeconds(3));
    }

    public void ShowSuccess(string title, string message, bool ephemeral = true, TimeSpan? timeout = null)
    {
        ShowCore(title, message, NotificationType.Success, ephemeral = true, timeout = TimeSpan.FromSeconds(3));
    }

    public void ShowWarning(string title, string message, bool ephemeral = true, TimeSpan? timeout = null)
    {
        ShowCore(title, message, NotificationType.Warning, ephemeral = true, timeout = TimeSpan.FromSeconds(5));
    }

    public void ShowError(string title, string message, bool ephemeral = true, TimeSpan? timeout = null)
    {
        ShowCore(title, message, NotificationType.Error, ephemeral = true, timeout = TimeSpan.FromSeconds(5));
    }

    // Back-compat name you already call in code:
    public void Show(string title, string message, bool ephemeral = true, TimeSpan? timeout = null)
    {
        ShowInfo(title, message, ephemeral, timeout);
    }

    public void BindInstall(InstallJob job)
    {
        Dispatch(() =>
        {
            CurrentInstall = job;
            OnInstallChanged?.Invoke();
        });
    }

    public void UnbindInstall(InstallJob job)
    {
        if (CurrentInstall?.Id != job.Id) return;

        Dispatch(() =>
        {
            CurrentInstall = null;
            OnInstallChanged?.Invoke();
        });
    }

    private void ShowCore(string title, string message, NotificationType kind, bool ephemeral, TimeSpan? timeout)
    {
        var n = new Notification
        {
            Title = title ?? "",
            Message = message ?? "",
            IsEphemeral = ephemeral,
            When = DateTimeOffset.Now,
            Kind = kind
        };

        void RunOnUi()
        {
            EnsureToastManager();

            _notificationManager?.Show(new Avalonia.Controls.Notifications.Notification(
                n.Title,
                n.Message,
                kind,
                timeout ?? TimeSpan.FromSeconds(4)));

            Others.Insert(0, n);
            if (ephemeral)
                _ = DismissLater(n, 3500);
        }

        if (Dispatcher.UIThread.CheckAccess()) RunOnUi();
        else Dispatcher.UIThread.Post(RunOnUi);
    }

    private async Task DismissLater(Notification n, int ms)
    {
        try
        {
            await Task.Delay(ms).ConfigureAwait(false);
            Dispatch(() => Others.Remove(n));
        }
        catch
        {
            // good girl action
        }
    }

    private void EnsureToastManager()
    {
        if (_notificationManager != null) return;

        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

        var top =
            desktop?.Windows?.FirstOrDefault(w => w?.IsActive == true)
            ?? desktop?.MainWindow;

        if (top != null)
            Attach(top);
    }

    private static void Dispatch(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }

    public sealed class Notification
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTimeOffset When { get; set; } = DateTimeOffset.Now;
        public bool IsEphemeral { get; set; }
        public NotificationType Kind { get; set; } = NotificationType.Information;
    }
}