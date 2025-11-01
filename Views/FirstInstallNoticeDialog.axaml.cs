using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace DragonDen.ModManager.Views;

public partial class FirstInstallDialog : Window
{
    private readonly string? modName;
    private readonly string? modGuid;
    private readonly string? modUrl;

    private readonly TaskCompletionSource<bool> resultTcs;
    private bool pageOpened;

    public FirstInstallDialog()
    {
        InitializeComponent();

        resultTcs = new TaskCompletionSource<bool>();

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
        pageOpened = false;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
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

        if (e.Key == Key.Enter && pageOpened && InstallBtn.IsEnabled)
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

        pageOpened = true;
        InstallBtn.IsEnabled = true;
        CountdownText.Text = "You can install now";
        InstallBtn.Focus();
    }

    private void OnInstallClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Services.HasInstalledBefore.RecordModInstalled(modGuid, modName, modUrl);
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