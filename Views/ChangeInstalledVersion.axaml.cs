using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.Utils;
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
                try
                {
                    BeginMoveDrag(e);
                }
                catch
                {
                    // good girl action
                }
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
        for (var i = 0; i < versions.Count; i++)
        {
            var v = versions[i];
            var border = new Border { Classes = { "row" }, Child = BuildRow(v, i == 0) };
            Rows.Children.Add(border);
        }
    }

    private Control BuildRow(ForgeClient.ModVersion v, bool isLatest)
    {
        var verText = string.IsNullOrWhiteSpace(v?.Version) ? "(unknown)" : v!.Version!;
        var sptAB = string.IsNullOrWhiteSpace(v?.SptVersionConstraint) ? "" : ToABFromConstraint(v!.SptVersionConstraint);
        var hasDesc = !string.IsNullOrWhiteSpace(v?.Description);
        var descText = hasDesc ? HTMLUtils.HtmlToDisplay(v!.Description!) : "";
        var dlText = v?.Downloads is long d && d > 0 ? $"{FormatDownloads(d)}" : null;
        var dateText = FormatDate(v?.PublishedAt);

        var isInstalled = !string.IsNullOrWhiteSpace(_row?.InstalledVersion) &&
                          string.Equals(_row!.InstalledVersion, v?.Version, StringComparison.OrdinalIgnoreCase);

        var title = new TextBlock
        {
            Text = verText,
            FontWeight = FontWeight.SemiBold,
            FontSize = 16
        };

        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isLatest)
            chips.Children.Add(MakeChip("Latest", primary: true));

        if (!string.IsNullOrWhiteSpace(sptAB))
            chips.Children.Add(MakeChip($"SPT {sptAB}"));

        if (!string.IsNullOrWhiteSpace(dlText))
            chips.Children.Add(MakeChip(dlText!));

        if (!string.IsNullOrWhiteSpace(dateText))
            chips.Children.Add(MakeChip(dateText!));

        var headerLeft = new StackPanel { Spacing = 6 };
        headerLeft.Children.Add(title);
        headerLeft.Children.Add(chips);

        var installBtn = new Button
        {
            Content = isInstalled ? "Currently Installed" : "Install",
            Classes = { "action" },
            HorizontalAlignment = HorizontalAlignment.Right,
            Tag = v,
            IsEnabled = !isInstalled,
        };

        if (!isInstalled)
            installBtn.Classes.Add("install");

        installBtn.Click += (_, __) =>
        {
            if (!isInstalled)
                InstallChosenVersion(v);
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        header.Children.Add(headerLeft);
        Grid.SetColumn(installBtn, 1);
        header.Children.Add(installBtn);

        var outer = new Border
        {
            Classes = { "card" },
            Opacity = isInstalled ? 0.75 : 1.0
        };

        if (!hasDesc)
        {
            outer.Child = header;
            return outer;
        }

        var changelogText = new TextBlock
        {
            Text = descText,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.95,
            Margin = new Thickness(0)
        };

        var body = new Border
        {
            Classes = { "subtle" },
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 200,
                Content = changelogText
            }
        };

        var headerBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = new SolidColorBrush(Color.Parse("#0E1318")),
            Margin = new Thickness(10)
        };
        
        headerBar.Children.Add(new TextBlock
        {
            Text = "Changelog",
            FontWeight = FontWeight.SemiBold
        });

        var exp = new Expander
        {
            Classes = { "clean" },
            IsExpanded = isLatest,
            Header = new TextBlock
            {
                Text = "Changelog",
                FontWeight = FontWeight.SemiBold
            },
            Content = body
        };

        outer.Child = new StackPanel
        {
            Spacing = 8,
            Children = { header, exp }
        };

        return outer;
    }

    private Border MakeChip(string text, bool primary = false)
    {
        var subtleBrush = (this.TryFindResource("Dd.Subtle", out var sb) && sb is IBrush br)
            ? br
            : new SolidColorBrush(Color.Parse("#9AA4AE"));

        var b = new Border { Classes = { "chip" } };
        if (primary) b.Classes.Add("primary");

        b.Child = new TextBlock
        {
            Text = text,
            Foreground = primary
                ? new SolidColorBrush(Color.Parse("#9CC6FF"))
                : subtleBrush,
            FontSize = 12,
            FontWeight = primary ? FontWeight.SemiBold : FontWeight.Normal
        };
        return b;
    }

    private static string FormatDownloads(long d)
    {
        if (d >= 1_000_000) return $"{d / 1_000_000d:0.#}M downloads";
        if (d >= 1_000) return $"{d / 1_000d:0.#}k downloads";
        return $"{d} downloads";
    }

    private static string? FormatDate(DateTimeOffset? dto)
    {
        if (dto is { } v) return v.ToLocalTime().ToString("yyyy MMM d");
        return null;
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
            Console.WriteLine($"[ChangeInstalledVersion] Aborting install due to SPT AB mismatch: detected={detectedAB}, chosen={chosenAB}");
            return;
        }

        var selectedVer = chosen.Version ?? "Custom Install";
        var installedVersion = _row.InstalledVersion ?? "Custom Install";
        if (string.Equals(selectedVer, installedVersion, StringComparison.OrdinalIgnoreCase))
        {
            Notifications.Current.ShowWarning("Already Installed", $"'{_row.Name}' is already on version {installedVersion}.");
            Console.WriteLine($"[ChangeInstalledVersion] Aborting install because selected version is already installed: {installedVersion}");
            return;
        }

        App.Queue.EnqueueRemote(_row.Name, chosen.Link!, selectedVer, _row.Guid ?? "");
        Notifications.Current.ShowSuccess("Version Change Queued", $"'{_row.Name}' will change from {installedVersion} → {selectedVer}.");
        Console.WriteLine($"[ChangeInstalledVersion] Queued version change for '{_row.Name}': {installedVersion} → {selectedVer}");
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
