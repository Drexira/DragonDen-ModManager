using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.ViewModels;

namespace DragonDen.ModManager.Views;

public partial class ChangeInstalledVersion : Window
{
    private readonly InstalledModRow? _row;

    public ChangeInstalledVersion()
    {
        InitializeComponent();

        PointerPressed += (_, e) =>
        {
            if (e.Source is Button) return;
            var pos = e.GetPosition(this);
            if (pos.Y <= 36 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                try { BeginMoveDrag(e); } catch { }
            }
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        }, RoutingStrategies.Tunnel);
    }

    public ChangeInstalledVersion(InstalledModRow row) : this()
    {
        _row = row;
        TitleText.Text = $"Choose version for {row.Name}";

        var versions = (row.Versions ?? Enumerable.Empty<ForgeClient.ModVersion>()).ToList();
        foreach (var v in versions)
        {
            var border = new Border { Classes = { "row" }, Child = BuildRow(v) };
            Rows.Children.Add(border);
        }
    }

    private Control BuildRow(ForgeClient.ModVersion v)
    {
        var left = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(v?.Version) ? "(unknown)" : v!.Version!,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(v?.SptVersionConstraint) ? "" : $"SPT {ToABFromConstraint(v!.SptVersionConstraint)}",
                    Opacity = 0.8
                }
            }
        };

        var installBtn = new Button
        {
            Content = "Install",
            Classes = { "action", "install" },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Tag = v
        };
        installBtn.Click += (_, __) => InstallChosenVersion(v);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        Grid.SetColumn(installBtn, 1);
        grid.Children.Add(installBtn);

        return grid;
    }

    private void InstallChosenVersion(ForgeClient.ModVersion chosen)
    {
        if (_row is null || chosen is null || string.IsNullOrWhiteSpace(chosen.Link))
            return;

        var detectedAB = App.GetDetectedSptAB();
        var chosenAB = ToABFromConstraint(chosen.SptVersionConstraint);
        if (!string.IsNullOrWhiteSpace(detectedAB) &&
            !string.Equals(detectedAB, chosenAB, StringComparison.OrdinalIgnoreCase))
        {
            Notifications.Current.ShowError("Version Mismatch", $"This version targets SPT {chosenAB}, but your install is {detectedAB}.");
            return;
        }

        var selectedVer = chosen.Version ?? "Custom Install";
        var installedVersion = _row.InstalledVersion ?? "Custom Install";
        if (string.Equals(selectedVer, installedVersion, StringComparison.OrdinalIgnoreCase))
        {
            Notifications.Current.ShowWarning("Already Installed", $"'{_row.Name}' is already on version {installedVersion}.");
            return;
        }

        App.Queue.EnqueueRemote(_row.Name, chosen.Link!, selectedVer, _row.Guid ?? "");
        Notifications.Current.ShowSuccess("Version Change Queued", $"'{_row.Name}' will change from {installedVersion} → {selectedVer}.");
        Close();
    }

    private static string ToABFromConstraint(string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint)) return "";
        var norm = SemverUtil.NormalizeToThreeParts(constraint) ?? constraint;
        var p = norm.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 2) return $"{p[0]}.{p[1]}";
        var m = System.Text.RegularExpressions.Regex.Match(constraint, @"(\d+)\.(\d+)");
        return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : "";
    }

    private void OnClose(object? s, RoutedEventArgs e) => Close();
}
