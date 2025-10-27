using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.Storage;
using DragonDen.ModManager.Utils;
using Color = Avalonia.Media.Color;
using Size = Avalonia.Size;

namespace DragonDen.ModManager.Views;

public partial class MainWindow : Window
{
    private readonly TextBlock? _footerCenter;
    private readonly TextBlock? _footerLeft;

    public MainWindow()
    {
        InitializeComponent();
        
        Opened += (_, __) => { 
            var scale = this.RenderScaling;
            ClientSize = new Size(1280, 720); 
        };
        
        Logger.Init(mirrorToConsole: false, retentionDays: 7);
        Logger.HookConsole();

        _footerLeft = this.FindControl<TextBlock>("FooterLeft");
        _footerCenter = this.FindControl<TextBlock>("FooterCenter");

        App.ConfigChanged += () =>
        {
            var settings = this.FindDescendantOfType<SettingsPage>();
            settings?.RefreshFromConfig();
        };

        Opened += OnOpenedAsync;

        if (App.Queue is not null)
        {
            App.Queue.Jobs.CollectionChanged += (_, __) =>
            {
                AttachJobHandlers();
                UpdateFooterCenter();
            };
            AttachJobHandlers();
        }

        UpdateFooterCenter();
    }

    private async void OnOpenedAsync(object? s, EventArgs e)
    {
        while (string.IsNullOrWhiteSpace(App.Config.Forge.Token))
        {
            var dlg = new TokenDialog();
            var res = await dlg.ShowDialog<TokenDialog.Result?>(this) ?? TokenDialog.Result.CloseApp;
            if (res == TokenDialog.Result.CloseApp)
            {
                Close();
                return;
            }
        }

        while (string.IsNullOrWhiteSpace(App.Config.Paths.SptRoot) ||
               !Directory.Exists(App.Config.Paths.SptRoot!))
        {
            var dlg = new FirstRunDialog();
            var res = await dlg.ShowDialog<FirstRunDialog.Result?>(this) ?? FirstRunDialog.Result.CloseApp;
            if (res == FirstRunDialog.Result.CloseApp)
            {
                Close();
                return;
            }
        }
        
        await SelfUpdateChecker.CheckOnStartupAsync(this);

        try
        {
            var modsDbPath = Paths.ModsDbPath;
            App.Db = new Db(modsDbPath);
            App.Db.Init();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindow] Error initializing mods db: {ex.Message}");
        }

        if (Spt.TryGetServerVersionThree(out var _three, out var majorTwo))
        {
            var installPage = this.FindDescendantOfType<BrowseModsPage>();
            installPage?.SelectSptMajor(majorTwo);
        }

        var year = DateTime.Now.Year;
        _footerLeft!.Text = $"© {year} Dragon Den Mod Manager";

        UpdateFooterCenter();
    }

    private void OnOpenKoFi(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/drexira",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Open Failed", "Unable to open the Ko-Fi page. Check your internet connection or default browser.");
            Console.WriteLine($"[MainWindow] Failed to open Ko-Fi link: {ex.Message}");
        }
    }

    private void AttachJobHandlers()
    {
        if (App.Queue?.Jobs is null) return;
        foreach (var j in App.Queue.Jobs)
        {
            j.PropertyChanged -= OnAnyJobPropertyChanged;
            j.PropertyChanged += OnAnyJobPropertyChanged;
        }
    }

    private void OnAnyJobPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallJob.IsIndeterminate) ||
            e.PropertyName == nameof(InstallJob.Progress) ||
            e.PropertyName == nameof(InstallJob.Status) ||
            e.PropertyName == "IsCompleted" ||
            e.PropertyName == nameof(InstallJob.Phase))
            UpdateFooterCenter();
    }

    private void UpdateFooterCenter()
    {
        if (_footerCenter is null) return;

        var total = App.Queue?.Jobs.Count ?? 0;
        var completed = App.Queue?.Jobs.Count(j => j.IsCompleted) ?? 0;

        if (total == 0 || completed == total)
        {
            _footerCenter.Text = "Installation Queue";
            _footerCenter.Foreground = this.FindResource("Dd.LightGrey") as IBrush ?? _footerCenter.Foreground;
            _footerCenter.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(_footerCenter, "Open installation queue");
            return;
        }

        _footerCenter.Text = $"Installation Queue — {completed}/{total} in Queue";
        _footerCenter.Foreground = new SolidColorBrush(Color.Parse("#FF8A00"));
        ToolTip.SetTip(_footerCenter, "Click to view progress");
    }

    private async void OnFooterCenterClick(object? sender, PointerPressedEventArgs e)
    {
        var existing = (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows.OfType<InstallationQueueDialog>().FirstOrDefault();

        try
        {
            if (existing is not null)
                existing.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindow] Error closing existing InstallationQueueDialog: {ex.Message}");
        }

        var dlg = new InstallationQueueDialog
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = true
        };
        dlg.Show(this);
    }
}