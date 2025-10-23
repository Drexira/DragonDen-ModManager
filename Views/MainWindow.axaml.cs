using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.Storage;

namespace DragonDen.ModManager.Views; 

public partial class MainWindow : Window
{
    private TextBlock? _footerLeft;
    
    public MainWindow()
    {
        InitializeComponent();
        App.Toasts.Attach(ToastHost);
        
        _footerLeft = this.FindControl<TextBlock>("FooterLeft");

        App.ConfigChanged += () =>
        {
            var settings = this.FindDescendantOfType<SettingsPage>();
            settings?.RefreshFromConfig();
        };

        Opened += OnOpenedAsync;
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

        try
        {
            var modsDbPath = Paths.ModsDbPath;
            App.Db = new Db(modsDbPath);
            App.Db.Init();
        }
        catch
        {
            App.Toasts?.Show("Could not open mods database for this SPT folder.");
        }

        if (Spt.TryGetServerVersionThree(out var _three, out var majorTwo))
        {
            var installPage = this.FindDescendantOfType<BrowseModsPage>();
            installPage?.SelectSptMajor(majorTwo);
        }

        try
        {
            await ModCache.EnsureWarmAsync();
        }
        catch
        {
            // good girl action
        }
        
        var year = DateTime.Now.Year;
        _footerLeft!.Text = $"© {year} Dragon Den Mod Manager";
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
        catch { }
    }
}