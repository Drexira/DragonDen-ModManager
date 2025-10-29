using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace DragonDen.ModManager.Views;

public partial class FirstInstallDialog : Window
{
    private readonly string? modName;
    private readonly string? modGuid;
    private readonly string? modUrl;

    private TimeSpan waitAfterOpen = TimeSpan.FromSeconds(6);
    private bool openedPage;
    private DateTimeOffset enableAt;
    private readonly DispatcherTimer uiTimer;
    private readonly TaskCompletionSource<bool> resultTcs;

    public FirstInstallDialog()
    {
        InitializeComponent();

        resultTcs = new TaskCompletionSource<bool>();
        uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, OnTick);

        Opened += OnOpened;
        Closing += OnClosing;

        InstallBtn.Click += OnInstallClicked;
        CancelBtn.Click += OnCancelClicked;
        CloseBtn.Click += OnCancelClicked;
        OpenPageBtn.Click += OnOpenPageClicked;

        KeyDown += OnKeyDown;
    }

    public FirstInstallDialog(string modName, string modGuid, string modUrl) : this()
    {
        this.modName = modName;
        this.modGuid = modGuid;
        this.modUrl = modUrl;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        TitleText.Text = string.IsNullOrWhiteSpace(modName) ? "First Install" : "First Install of " + modName;
        SubTitleText.Text = string.IsNullOrWhiteSpace(modName) ? "" : "Review details before you continue";
        BodyText.Text = "Read the Forge page before installing.";
        BodyText2.Text = "Check requirements, incompatibilities, and install notes.";
        CountdownText.Text = "Install available after opening the mod page";
        InstallBtn.IsEnabled = false;
        uiTimer.Start();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        uiTimer.Stop();
        resultTcs.TrySetResult(false);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            resultTcs.TrySetResult(false);
            Close();
            return;
        }

        if (e.Key == Key.Enter && InstallBtn.IsEnabled)
        {
            OnInstallClicked(this, null!);
            e.Handled = true;
        }
    }

    private async void OnOpenPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(modUrl))
        {
            CountdownText.Text = "No mod page url";
            return;
        }

        try
        {
            var top = GetTopLevel(this);
            if (top != null)
                await top.Launcher.LaunchUriAsync(new Uri(modUrl));
        }
        catch
        {
            // good girl action
        }

        openedPage = true;
#if DEBUG
        waitAfterOpen = TimeSpan.FromSeconds(1);
#endif
        enableAt = DateTimeOffset.UtcNow + waitAfterOpen;
        UpdateCountdown();
    }

    private void OnInstallClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            App.Db.RecordModPageAcknowledgement(modGuid, modName, modUrl);
        }
        catch
        {
            // good girl action
        }

        resultTcs.TrySetResult(true);
        Close();
    }

    private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        resultTcs.TrySetResult(false);
        Close();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!openedPage)
        {
            InstallBtn.IsEnabled = false;
            CountdownText.Text = "Install available after opening the mod page";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now >= enableAt)
        {
            InstallBtn.IsEnabled = true;
            CountdownText.Text = "You can install now";
        }
        else
        {
            InstallBtn.IsEnabled = false;
            UpdateCountdown();
        }
    }

    private void UpdateCountdown()
    {
        var now = DateTimeOffset.UtcNow;
        var remaining = enableAt > now ? enableAt - now : TimeSpan.Zero;
        var s = Math.Ceiling(remaining.TotalSeconds);
        CountdownText.Text = openedPage ? "Install available in " + s.ToString("0") + "s" : "Install available after opening the mod page";
    }

    public static async Task<bool> ShowAsync(Window owner, string modName, string modGuid, string modUrl, CancellationToken ct = default)
    {
        var dlg = new FirstInstallDialog(modName, modGuid, modUrl);

        var shown = dlg.ShowDialog<bool>(owner);
        var waitTask = dlg.resultTcs.Task;

        _ = shown;

        using var reg = ct.Register(() =>
        {
            try
            {
                dlg.resultTcs.TrySetResult(false);
            }
            catch
            {
                // good girl action
            }

            try
            {
                dlg.Close(false);
            }
            catch
            {
                // good girl action
            }
        });

        var ok = await waitTask.ConfigureAwait(true);
        return ok;
    }

    public bool Result
    {
        get; private set; 
    }
}