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
using Avalonia.Threading;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.Utils;
using DragonDen.ModManager.ViewModels;
using Semver;

namespace DragonDen.ModManager.Views;

public partial class ChangeInstalledVersion : Window
{
    private readonly InstalledModRow? _row;
    private bool _hydrated;

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

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_row == null || _hydrated) return;
        var cache = App.Cache;
        if (cache == null) return;

        try
        {
            var allCache = cache.GetAllModsBasic();
            CacheDb.ModRow? cacheRow = null;

            if (!string.IsNullOrWhiteSpace(_row.Guid))
            {
                cacheRow = allCache.FirstOrDefault(m =>
                    string.Equals(m.Guid, _row.Guid, StringComparison.OrdinalIgnoreCase));
            }

            if (cacheRow == null)
            {
                cacheRow = allCache.FirstOrDefault(m =>
                    string.Equals(m.Name, _row.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (cacheRow == null) return;

            await cache.EnsureVersionsCachedAsync(cacheRow.Id).ConfigureAwait(false);
            var fullVersions = cache.GetVersionsForMod(cacheRow.Id);

            if (fullVersions == null || fullVersions.Count == 0) return;
            if (!fullVersions.Any(v => !string.IsNullOrWhiteSpace(v.Description))) return;

            _hydrated = true;

            var sorted = OrderVersionsDesc(fullVersions);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Rows.Children.Clear();
                for (var i = 0; i < sorted.Count; i++)
                {
                    var v = sorted[i];
                    var border = new Border { Classes = { "row" }, Child = BuildRow(v, i == 0) };
                    Rows.Children.Add(border);
                }
            });
        }
        catch
        {
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
        var hasDesc = !string.IsNullOrWhiteSpace(v?.Description);
        var descText = hasDesc ? HTMLUtils.HtmlToDisplay(v!.Description!) : "";
        var dlText = v?.Downloads is long d && d > 0 ? FormatDownloads(d) : null;
        var dateText = FormatDate(v?.PublishedAt);

        var isInstalled = !string.IsNullOrWhiteSpace(_row?.InstalledVersion) &&
                          string.Equals(_row!.InstalledVersion, v?.Version, StringComparison.OrdinalIgnoreCase);

        var fikaKind = NormalizeFika(v?.FikaCompatibility);
        var fikaText = fikaKind switch
        {
            "compatible" => "Fika compatible",
            "incompatible" => "Fika incompatible",
            "unknown" => "Fika unknown",
            _ => ""
        };

        var supportedSpt = ResolveSupportedSptVersions(v?.SptVersionConstraint);

        var headerLeft = new StackPanel { Spacing = 6 };
        headerLeft.Children.Add(new TextBlock
        {
            Text = verText,
            FontWeight = FontWeight.SemiBold,
            FontSize = 16
        });

        var chipsTop = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isLatest)
            chipsTop.Children.Add(MakeChip("Latest", primary: true));

        if (!string.IsNullOrWhiteSpace(fikaText))
        {
            var primaryFika = fikaKind == "compatible";
            chipsTop.Children.Add(MakeChip(fikaText, primary: primaryFika));
        }

        if (!string.IsNullOrWhiteSpace(dlText))
            chipsTop.Children.Add(MakeChip(dlText!));

        if (!string.IsNullOrWhiteSpace(dateText))
            chipsTop.Children.Add(MakeChip(dateText!));

        headerLeft.Children.Add(chipsTop);

        if (supportedSpt.Count > 0)
        {
            var chipsBottom = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            foreach (var s in supportedSpt)
                chipsBottom.Children.Add(MakeChip($"SPT {s}"));

            headerLeft.Children.Add(chipsBottom);
        }

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

        var b = new Border
        {
            Classes = { "chip" },
            Margin = new Thickness(0, 0, 6, 0)
        };
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
            Logger.Warn($"[ChangeInstalledVersion] Aborting install due to SPT AB mismatch: detected={detectedAB}, chosen={chosenAB}");
            return;
        }

        var selectedVer = chosen.Version ?? "Custom Install";
        var installedVersion = _row.InstalledVersion ?? "Custom Install";
        if (string.Equals(selectedVer, installedVersion, StringComparison.OrdinalIgnoreCase))
        {
            Notifications.Current.ShowWarning("Already Installed", $"'{_row.Name}' is already on version {installedVersion}.");
            Logger.Warn($"[ChangeInstalledVersion] Aborting install because selected version is already installed: {installedVersion}");
            return;
        }

        App.Queue.EnqueueRemote(_row.Name, chosen.Link!, selectedVer, _row.Guid ?? "");
        Notifications.Current.ShowSuccess("Version Change Queued", $"'{_row.Name}' will change from {installedVersion} → {selectedVer}.");
        Logger.Info($"[ChangeInstalledVersion] Queued version change for '{_row.Name}': {installedVersion} → {selectedVer}");
        Close();
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

    private static string NormalizeFika(string? value)
    {
        return (value ?? "").Trim().ToLowerInvariant();
    }

    private static IReadOnlyList<string> ResolveSupportedSptVersions(string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint)) return Array.Empty<string>();
        if (App.Cache == null) return Array.Empty<string>();

        var (_, fulls) = App.Cache.GetAllSptTags();
        if (fulls == null || fulls.Count == 0) return Array.Empty<string>();

        SemVersionRange? range = null;
        if (SemVersionRange.TryParse(constraint, SemVersionRangeOptions.Loose, out var parsed))
        {
            range = parsed;
        }

        var matches = new List<(SemVersion ver, string tag)>();

        if (range != null)
        {
            foreach (var tag in fulls)
            {
                if (!SemverUtil.TryParseStrict(tag, out var sv)) continue;
                if (range.Contains(sv)) matches.Add((sv, tag));
            }
        }
        else
        {
            var normConstraint = SemverUtil.NormalizeToThreeParts(constraint);
            if (string.IsNullOrWhiteSpace(normConstraint)) return Array.Empty<string>();

            if (!SemverUtil.TryParseStrict(normConstraint, out var target)) return Array.Empty<string>();

            foreach (var tag in fulls)
            {
                if (!SemverUtil.TryParseStrict(tag, out var sv)) continue;
                if (sv.Equals(target)) matches.Add((sv, tag));
            }
        }

        matches.Sort((a, b) => b.ver.CompareSortOrderTo(a.ver));
        return matches.Select(m => m.tag).ToList();
    }

    private void OnClose(object? s, RoutedEventArgs e) => Close();
}