using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.Storage;
using DragonDen.ModManager.Views;
using ServicesCacheDb = DragonDen.ModManager.Services.CacheDb;

namespace DragonDen.ModManager;

public class App : Application
{
    public static Action<string>? SearchByAuthorRequested;
    public static Action? ShowBrowseRequested;

    private static readonly CancellationTokenSource _shutdownCts = new();
    private static readonly object _installsLock = new();
    private static bool _pendingInstallRefresh;

    private static CancellationTokenSource? _warmCts;
    private static volatile int _warmStarted;
    private EventWaitHandle? _activateEvent;
    private Task? _warmTask;
    public static Config Config { get; private set; } = null!;
    public static Db Db { get; set; } = null!;
    public static SevenZip SevenZip { get; private set; } = null!;
    public static InstallQueue Queue { get; private set; } = null!;
    public static ServicesCacheDb Cache { get; private set; } = null!;
    public static CancellationToken ShutdownToken => _shutdownCts.Token;

    public static event Action? ConfigChanged;
    public static event Action? InstallsChanged;

    public static void SaveConfig()
    {
        Config.Save(Paths.AppSettingsPath);
        ConfigChanged?.Invoke();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            Directory.CreateDirectory(Paths.CacheDir);
        }
        catch (Exception ex)
        {
            Logger.Error($"[App] Failed to create cache directory: {ex.Message}");
        }

        try
        {
            Directory.CreateDirectory(Paths.ModsDir);
        }
        catch (Exception ex)
        {
            Logger.Error($"[App] Failed to create mods directory: {ex.Message}");
        }

        try
        {
            Logger.Init(mirrorToConsole: true, retentionDays: 7, minLevel: Logger.Level.Info);
            Logger.HookConsole();
            Logger.Info("[Mod Manager] Logger ready.");
        }
        catch
        {
            // good girl action
        }

        Config = Config.Load(Paths.AppSettingsPath);

        var modsDbPath = Paths.ModsDbPath;
        Db = new Db(modsDbPath);
        Db.Init();

        SevenZip = new SevenZip(Paths.SevenZipPath);
        Queue = new InstallQueue(SevenZip, Db);

        Cache = new ServicesCacheDb(Paths.CacheDbPath);
        Cache.Init();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            Notifications.Current.Attach(desktop.MainWindow);
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DragonDen.ModManager:ACTIVATE");

            _ = Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        _activateEvent!.WaitOne();
                    }
                    catch
                    {
                        break;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        var win = desktop.MainWindow;
                        if (win is null) return;

                        if (win.WindowState == WindowState.Minimized)
                            win.WindowState = WindowState.Normal;

                        win.Topmost = true;
                        win.Topmost = false;

                        win.Activate();
                        win.Focus();
                    });
                }
            });

            desktop.Exit += (_, __) =>
            {
                try
                {
                    _shutdownCts.Cancel();
                }
                catch
                {
                    // good girl action
                }

                try
                {
                    _warmCts?.Cancel();
                }
                catch
                {
                    // good girl action
                }

                try
                {
                    _activateEvent?.Set();
                }
                catch
                {
                    // good girl action
                }

                try
                {
                    _activateEvent?.Dispose();
                }
                catch
                {
                    // good girl action
                }

                try
                {
                    Logger.Info("[Mod Manager] Shutting down.");
                    Logger.Shutdown();
                }
                catch
                {
                    // good girl action
                }
            };

            desktop.MainWindow.Opened += async (_, __) =>
            {
                const string githubIssues = "https://github.com/Drexira/DragonDen-ModManager/issues";
                const string discordInvite = "https://discord.gg/WelcomeToTarkov";
                const string modPage = "https://forge.sp-tarkov.com/mod/2396/dragon-den-mod-manager";

                if (!Config.UI.ExpertMode)
                {
                    var ok = await AlphaNoticeDialog.ShowAsync(desktop.MainWindow, githubIssues, discordInvite, modPage, CancellationToken.None);
                    if (!ok)
                    {
                        desktop.Shutdown();
                        return;
                    }
                }

                _warmCts = CancellationTokenSource.CreateLinkedTokenSource(ShutdownToken);
                _warmTask = WarmCacheOnLaunch(_warmCts.Token);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task WarmCacheOnLaunch(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _warmStarted, 1) == 1) return;
        try
        {
            await Cache.RefreshAllAsync(null, ct);
        }
        catch (OperationCanceledException e)
        {
            // good girl action
        }
        catch (Exception ex)
        {
            Logger.Error($"[App] Failed to warmup cache: {ex.Message}");
        }
    }


    public static void NotifyInstallsChanged()
    {
        lock (_installsLock)
        {
            if (_pendingInstallRefresh) return;
            _pendingInstallRefresh = true;
        }

        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                await Task.Delay(150).ConfigureAwait(true);
                InstallsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Error($"[App] Failed to install installs: {ex.Message}");
            }
            finally
            {
                lock (_installsLock)
                {
                    _pendingInstallRefresh = false;
                }
            }
        }, DispatcherPriority.Background);
    }

    public static void RaiseConfigChanged()
    {
        try
        {
            ConfigChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error("[App] Failed to raise config changed: " + ex.Message);
        }
    }

    public static void CancelWarmCache()
    {
        try
        {
            _warmCts?.Cancel();
        }
        catch (Exception ex)
        {
            Logger.Error("[App] Failed to cancel warm cache: " + ex.Message);
        }
    }

    public static string GetDetectedSptAB()
    {
        return Spt.TryGetServerVersionThree(out _, out var ab) && !string.IsNullOrWhiteSpace(ab) ? ab : "";
    }
}