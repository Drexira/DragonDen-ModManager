using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DragonDen.ModManager.Services;

namespace DragonDen.ModManager.Views;

public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<bool> ShowMinimizeProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMinimize), true);

    public static readonly StyledProperty<bool> ShowMaximizeProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMaximize), true);

    public TitleBar()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, __) => UpdateMaxIcon();
        var host = VisualRoot as Window;
        if (host is not null)
            host.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.WindowStateProperty) UpdateMaxIcon();
            };

        GithubButton.IsVisible = host is InstallationQueueDialog;
    }

    public bool ShowMinimize
    {
        get => GetValue(ShowMinimizeProperty);
        set => SetValue(ShowMinimizeProperty, value);
    }

    public bool ShowMaximize
    {
        get => GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    private Window? Host => VisualRoot as Window;

    private void OnDragAreaPointerPressed(object? s, PointerPressedEventArgs e)
    {
        if (Host is null) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host.BeginMoveDrag(e);
    }

    private void OnDragAreaDoubleTapped(object? s, RoutedEventArgs e)
    {
        if (Host is null || !ShowMaximize) return;
        Host.WindowState = Host.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaxIcon();
    }

    private void OnMinimize(object? s, RoutedEventArgs e)
    {
        if (Host is null) return;
        Host.WindowState = WindowState.Minimized;
    }

    private void OnMaxRestore(object? s, RoutedEventArgs e)
    {
        if (Host is null || !ShowMaximize) return;
        Host.WindowState = Host.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaxIcon();
    }

    private void OnClose(object? s, RoutedEventArgs e)
    {
        Host?.Close();
    }

    private void UpdateMaxIcon()
    {
        if (Host is null) return;
        var icon = this.FindControl<TextBlock>("MaxIcon");
        if (icon is null) return;
        icon.Text = Host.WindowState == WindowState.Maximized ? "❐" : "▢";
    }

    private void OnOpenIssuesPage(object? sender, RoutedEventArgs e)
    {
        var url = "https://github.com/Drexira/DragonDen-ModManager/issues";

        if (!string.IsNullOrWhiteSpace(url))
            try
            {
                _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Notifications.Current.ShowError("Open Failed", "Couldn't open issues page in browser.");
                Console.WriteLine("[TitleBar] Failed to open issues page: " + ex);
            }

        Notifications.Current.ShowWarning("Missing URL", "No valid page URL was provided.");
        Console.WriteLine("[TitleBar] No page URL defined for issues page.");
    }
}