using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

public partial class BrowseModInstallDialog : Window
{
    private readonly SearchResultRow _row;

    public BrowseModInstallDialog()
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
                }
            }
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        }, RoutingStrategies.Tunnel);
    }

    public BrowseModInstallDialog(SearchResultRow row) : this()
    {
        _row = row;
        TitleText.Text = $"Install {row.Name}";

        var detectedAB = App.GetDetectedSptAB();

        var all = (row.Versions ?? Enumerable.Empty<ForgeClient.ModVersion>()).ToList();
        var filtered = string.IsNullOrWhiteSpace(detectedAB)
            ? OrderVersionsDesc(all)
            : OrderVersionsDesc(all).Where(v =>
                    string.Equals(ToABFromConstraint(v.SptVersionConstraint), detectedAB, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (filtered.Count == 0)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(detectedAB)
                    ? "No versions available."
                    : $"No versions available for your SPT {detectedAB}.",
                Margin = new Thickness(8),
                Opacity = 0.85
            });
            return;
        }

        for (var i = 0; i < filtered.Count; i++)
        {
            var v = filtered[i];
            var border = new Border { Classes = { "row" }, Child = BuildRow(v, i == 0) };
            Rows.Children.Add(border);
        }
    }
    
    private static List<ForgeClient.ModVersion> OrderVersionsDesc(IEnumerable<ForgeClient.ModVersion> versions)
    {
        var list = (versions ?? Enumerable.Empty<ForgeClient.ModVersion>()).ToList();
        list.Sort((a, b) =>
        {
            var va = a?.Version ?? "";
            var vb = b?.Version ?? "";
            var okA = SemverUtil.TryParseStrict(va, out var sva);
            var okB = SemverUtil.TryParseStrict(vb, out var svb);
            if (okA && okB) return svb.CompareSortOrderTo(sva);
            return string.Compare(vb, va, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private Control BuildRow(ForgeClient.ModVersion v, bool isLatest)
    {
        var verText = string.IsNullOrWhiteSpace(v?.Version) ? "(unknown)" : v!.Version!;
        var sptAB = string.IsNullOrWhiteSpace(v?.SptVersionConstraint) ? "" : ToABFromConstraint(v!.SptVersionConstraint);
        var hasDesc = !string.IsNullOrWhiteSpace(v?.Description);
        var descText = hasDesc ? HTMLUtils.HtmlToDisplay(v!.Description!) : "";
        var dlText = v?.Downloads is long d && d > 0 ? FormatDownloads(d) : null;
        var dateText = FormatDate(v?.PublishedAt);

        var headerLeft = new StackPanel { Spacing = 6 };
        headerLeft.Children.Add(new TextBlock
        {
            Text = verText,
            FontWeight = FontWeight.SemiBold,
            FontSize = 16
        });

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

        headerLeft.Children.Add(chips);

        var installBtn = new Button
        {
            Content = "Install",
            Classes = { "action", "install" },
            HorizontalAlignment = HorizontalAlignment.Right,
            Tag = v
        };
        installBtn.Click += (_, __) => Close(v);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        header.Children.Add(headerLeft);
        Grid.SetColumn(installBtn, 1);
        header.Children.Add(installBtn);

        var outer = new Border { Classes = { "card" } };

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

    private static string ToABFromConstraint(string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint)) return "";

        var first = constraint.Split(new[] { "||" }, 2, StringSplitOptions.None)[0];

        first = Regex.Replace(first, @"[xX\*]", "0");

        var norm = SemverUtil.NormalizeToThreeParts(first);
        if (!string.IsNullOrEmpty(norm))
        {
            var p = norm.Split('.');
            return $"{p[0]}.{p[1]}";
        }

        var m1 = Regex.Match(first, @"\b(?<maj>\d+)\.(?<min>\d+)(?:\.\d+)?");
        if (m1.Success) return $"{m1.Groups["maj"].Value}.{m1.Groups["min"].Value}";

        var m2 = Regex.Match(first, @"(\d+)\.(\d+)");
        return m2.Success ? $"{m2.Groups[1].Value}.{m2.Groups[2].Value}" : "";
    }

    private void OnClose(object? s, RoutedEventArgs e) => Close();
}