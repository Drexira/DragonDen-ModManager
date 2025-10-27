using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.Utils;

namespace DragonDen.ModManager.Views;

public partial class UpdateAvailableDialog : Window
{
    private readonly string _pageUrl;

    private UpdateAvailableDialog()
    {
        InitializeComponent();

        _pageUrl = "";
        var current = SelfUpdateChecker.GetCurrentAppVersion();

        TitleText.Text = "Update Available";
        LatestChip.Text = "v0.0.0";
        CurrentChip.Text = $"Current v{current}";
        ChangelogText.Text = "";

        ShowPageBtn.Click += OnShowPage;
        LaterBtn.Click += OnLater;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public UpdateAvailableDialog(string latestVersion, string changelogHtml, string pageUrl)
        : this()
    {
        _pageUrl = pageUrl ?? "";
        var current = SelfUpdateChecker.GetCurrentAppVersion();
        TitleText.Text = $"Update Available";
        LatestChip.Text = $"Latest v{latestVersion}";
        CurrentChip.Text = $"Current v{current}";
        ChangelogText.Text = string.IsNullOrWhiteSpace(changelogHtml) ? "No changelog provided." : HTMLUtils.HtmlToDisplay(changelogHtml).Trim();
    }

    private void OnShowPage(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pageUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _pageUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("[UpdateAvailableDialog] Failed to open update page: " + ex.Message);
        }
    }

    private void OnLater(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            OnShowPage(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnDragWindow(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            try
            {
                BeginMoveDrag(e);
            }
            catch
            {
                // good girl action
            }
        }
    }
}