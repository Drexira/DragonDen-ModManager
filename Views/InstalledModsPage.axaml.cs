using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.ViewModels;

namespace DragonDen.ModManager.Views;

public partial class InstalledModsPage : UserControl
{
    private readonly SemaphoreSlim gate = new(2, 2);
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string _searchText = "";
    private StackPanel? _emptyState;

    public InstalledModsPage()
    {
        InitializeComponent();

        RefreshBtn.Click += async (_, __) => await ScanDiskAsync();
        CheckUpdatesBtn.Click += async (_, __) => await CheckUpdates();
        UpdateAllBtn.Click += async (_, __) => await UpdateAll();
        OpenClientBtn.Click += (_, __) => OpenFolder(Spt.ClientModsPath);
        OpenServerBtn.Click += (_, __) => OpenFolder(Spt.ServerModsPath);
        UninstallAllBtn.Click += async (_, __) => await UninstallAllAsync();

        SortBox.SelectionChanged += async (_, __) => await RefreshRows();
        UpdatesFirstChk.IsCheckedChanged += async (_, __) => await RefreshRows();
        
        _searchDebounce.Tick += async (_, __) =>
        {
            _searchDebounce.Stop();
            _searchText = (SearchBox.Text ?? "").Trim();
            await RefreshRows();
        };

        SearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                _searchDebounce.Stop();
                _searchDebounce.Start();
            }
        };

        SearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _searchDebounce.Stop();
                _searchText = (SearchBox.Text ?? "").Trim();
                await RefreshRows();
                e.Handled = true;
            }
        };

        ClearBtn.Click += async (_, __) =>
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = "";
                _searchText = "";
                await RefreshRows();
            }
        };

        AddHandler(Button.ClickEvent, OnAnyButtonClick, RoutingStrategies.Bubble);

        App.InstallsChanged += () => _ = RefreshRows();

        _ = RefreshRows();
    }

    private void OpenFolder(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            else
                App.Toasts.Show("Folder not found.");
        }
        catch
        {
            App.Toasts.Show("Could not open folder.");
        }
    }

    private async Task CheckUpdates()
    {
        var mods = App.Db.ListMods().ToList();
        var updatable = 0;

        var detectedAB = App.GetDetectedSptAB();

        foreach (var m in mods)
        {
            await gate.WaitAsync();
            try
            {
                var all = App.Cache.GetAllModsBasic();
                CacheDb.ModRow? cacheRow = null;
                if (!string.IsNullOrWhiteSpace(m.guid))
                    cacheRow = all.FirstOrDefault(r => string.Equals(r.Guid, m.guid, StringComparison.OrdinalIgnoreCase));
                else
                    cacheRow = all.FirstOrDefault(r => string.Equals(r.Name, m.name, StringComparison.OrdinalIgnoreCase));

                List<ForgeClient.ModVersion> versions = new();
                if (cacheRow != null)
                {
                    await App.Cache.EnsureVersionsCachedAsync(cacheRow.Id);
                    versions = await Storage.CacheDb.GetVersionsAsync(cacheRow.Id);
                }

                var candidates = versions
                    .Where(v => string.IsNullOrWhiteSpace(detectedAB) ||
                                string.Equals(ToABFromConstraint(v.SptVersionConstraint), detectedAB, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                candidates.Sort((x, y) => CompareSemverDesc(x?.Version, y?.Version));
                var latestForAB = candidates.FirstOrDefault();

                var latestVersion = latestForAB?.Version ?? "";
                if (!string.IsNullOrWhiteSpace(latestVersion) && IsUpdate(m.version, latestVersion))
                    updatable++;
            }
            finally
            {
                gate.Release();
            }

            await Task.Delay(120);
        }

        StatusText.Text = updatable == 0 ? "All up to date" : $"{updatable} mod(s) have updates";
        await RefreshRows();
    }

    private async Task UpdateAll()
    {
        var mods = App.Db.ListMods().ToList();
        var queued = 0;

        var detectedAB = App.GetDetectedSptAB();

        var groups = mods.GroupBy(m => string.IsNullOrWhiteSpace(m.guid)
            ? m.name.ToLowerInvariant()
            : "guid:" + m.guid.ToLowerInvariant());

        foreach (var g in groups)
        {
            var any = g.First();
            await gate.WaitAsync();
            try
            {
                var all = App.Cache.GetAllModsBasic();
                CacheDb.ModRow? cacheRow = null;
                if (!string.IsNullOrWhiteSpace(any.guid))
                    cacheRow = all.FirstOrDefault(r => string.Equals(r.Guid, any.guid, StringComparison.OrdinalIgnoreCase));
                else
                    cacheRow = all.FirstOrDefault(r => string.Equals(r.Name, any.name, StringComparison.OrdinalIgnoreCase));

                List<ForgeClient.ModVersion> versions = new();
                if (cacheRow != null)
                {
                    await App.Cache.EnsureVersionsCachedAsync(cacheRow.Id);
                    versions = await Storage.CacheDb.GetVersionsAsync(cacheRow.Id);
                }

                var candidates = versions
                    .Where(v => string.IsNullOrWhiteSpace(detectedAB) ||
                                string.Equals(ToABFromConstraint(v.SptVersionConstraint), detectedAB, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                candidates.Sort((x, y) => CompareSemverDesc(x?.Version, y?.Version));
                var latestForAB = candidates.FirstOrDefault();

                if (latestForAB != null && !string.IsNullOrWhiteSpace(latestForAB.Link))
                {
                    var lv = latestForAB.Version ?? "0.0.0";
                    if (IsUpdate(any.version, lv))
                    {
                        App.Queue.EnqueueRemote(any.name, latestForAB.Link!, lv, any.guid ?? "");
                        queued++;
                    }
                }
            }
            finally
            {
                gate.Release();
            }

            await Task.Delay(120);
        }

        StatusText.Text = queued == 0 ? "No updates queued" : $"Queued {queued} update(s)";
        await RefreshRows();
    }

    private async void OnAnyButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button b) return;

        if (b.Content?.ToString()?.Equals("Uninstall", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (b.Tag is InstalledModRow row1)
            {
                App.Db.UninstallByModIds(row1.ModIds);
                App.Toasts.Show($"Uninstalled {row1.Name}");
                await RefreshRows();
            }

            return;
        }

        if (b.Content?.ToString()?.Equals("Update", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (b.Tag is InstalledModRow row2)
            {
                if (row2.Latest?.Link is string url && !string.IsNullOrWhiteSpace(url))
                {
                    App.Queue.EnqueueRemote(row2.Name, url, row2.Latest?.Version ?? "0.0.0", row2.Guid ?? "");
                    App.Toasts.Show($"Queued update: {row2.Name}");
                }
                else
                {
                    App.Toasts.Show("No update link.");
                }
            }

            return;
        }

        if (b.Content?.ToString()?.Equals("Mod Page", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (b.Tag is InstalledModRow row3 && !string.IsNullOrWhiteSpace(row3.DetailUrl))
                try
                {
                    _ = Process.Start(new ProcessStartInfo { FileName = row3.DetailUrl, UseShellExecute = true });
                }
                catch
                {
                    App.Toasts.Show("Could not open browser.");
                }
            else App.Toasts.Show("No page URL for this mod.");

            return;
        }

        if (b.Content?.ToString()?.Equals("List Files", StringComparison.OrdinalIgnoreCase) == true)
            if (b.Tag is InstalledModRow row4)
                await ShowFilesDialog(row4);
        
        if (b.Content?.ToString()?.Equals("Edit Configs", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (b.Tag is InstalledModRow row5)
                await ShowConfigDialog(row5);
            return;
        }
    }
    
    public async Task RefreshFromSettingsAsync()
    {
        await ScanDiskAsync();
    }

    private async Task ShowFilesDialog(InstalledModRow row)
    {
        if (row.ModIds is null || row.ModIds.Count == 0) return;

        var merged = new List<(string path, string target)>();
        foreach (var id in row.ModIds)
            try
            {
                var part = App.Db.ListFilesForModId(id);
                if (part is { Count: > 0 }) merged.AddRange(part);
            }
            catch
            {
                // good girl action
            }

        var files = merged
            .GroupBy(t => (t.path?.Replace('\\', '/') ?? "", (t.target ?? "client").ToLowerInvariant()))
            .Select(g => (g.Key.Item1, g.Key.Item2))
            .OrderBy(t => t.Item2, StringComparer.Ordinal)
            .ThenBy(t => t.Item1, StringComparer.OrdinalIgnoreCase)
            .Select(t => (path: t.Item1, target: t.Item2))
            .ToList();

        var owner = (Window?)TopLevel.GetTopLevel(this);
        var dlg = new FilesDialog(row.Name, files)
        {
            ShowInTaskbar = false,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (owner is not null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }
    
    private static bool IsEditablePath(string p)
    {
        var ext = Path.GetExtension(p ?? "");
        return ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".json5", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jsonc", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".cfg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ini", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".toml", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ShowConfigDialog(InstalledModRow row)
    {
        if (row.ModIds is null || row.ModIds.Count == 0) return;

        var merged = new List<(string path, string target)>();
        foreach (var id in row.ModIds)
        {
            try
            {
                var part = App.Db.ListFilesForModId(id);
                if (part is { Count: > 0 }) merged.AddRange(part);
            }
            catch
            {
                // good girl action
            }
        }

        var items = new List<ConfigDialog.ConfigItem>();
        foreach (var (rel, target) in merged)
        {
            var unixRel = (rel ?? "").Replace('\\', '/');
            if (!IsEditablePath(unixRel)) continue;

            var baseRoot = (target ?? "client").Equals("server", StringComparison.OrdinalIgnoreCase)
                ? Spt.ServerModsPath
                : Spt.ClientModsPath;

            var full = Path.Combine(baseRoot, unixRel.Replace('/', Path.DirectorySeparatorChar));
            items.Add(new ConfigDialog.ConfigItem
            {
                DisplayPath = $"{target ?? "client"} • {unixRel}",
                FullPath = full
            });
        }

        if (items.Count == 0)
        {
            App.Toasts.Show("No editable config files found.");
            return;
        }

        var owner = (Window?)TopLevel.GetTopLevel(this);
        var dlg = new ConfigDialog(row.Name, items)
        {
            ShowInTaskbar = false,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (owner is not null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }

    private void OnOpenSource(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string url && !string.IsNullOrWhiteSpace(url))
            try
            {
                _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
                App.Toasts.Show("Could not open source link.");
            }
    }

    private async Task ScanDiskAsync()
    {
        StatusText.Text = "Scanning mods…";
        var stats = await Task.Run(() => InstalledScanner.ImportFromDisk());
        StatusText.Text = $"Refreshed: imported {stats.imported}, updated {stats.updated}, skipped {stats.skipped}.";
        await RefreshRows();
    }

    private static bool IsUpdate(string installed, string latest)
    {
        var okI = SemverUtil.TryParseStrict(installed, out var vi);
        var okL = SemverUtil.TryParseStrict(latest, out var vl);
        if (okI && okL) return vl.CompareSortOrderTo(vi) > 0;
        return !string.Equals(latest, installed, StringComparison.OrdinalIgnoreCase);
    }

    private async Task UninstallAllAsync()
    {
        var all = App.Db.ListMods();
        var allIds = all.Select(t => t.mod_id).Distinct().ToList();
        if (allIds.Count == 0)
        {
            App.Toasts.Show("No mods to uninstall.");
            return;
        }

        App.Db.UninstallByModIds(allIds);
        App.Toasts.Show("Uninstalled all mods.");
        await RefreshRows();
    }

    public async Task RefreshRows()
    {
        var sortTag = ((SortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "alpha").ToLowerInvariant();
        var updatesFirst = (UpdatesFirstChk?.IsChecked ?? false);

        var (allRows, statusText) = await BuildRowsAsync(sortTag, updatesFirst);
        var filtered = ApplySearchFilter(allRows, _searchText).ToList();

        ModsList.ItemsSource = filtered;
        StatusText.Text = filtered.Count == allRows.Count
            ? statusText
            : $"Showing {filtered.Count} of {allRows.Count} installed";
        _emptyState = this.FindControl<StackPanel>("EmptyState");
        if (_emptyState != null)
            _emptyState.IsVisible = allRows.Count == 0;
    }

    private async Task<(List<InstalledModRow> final, string statusText)> BuildRowsAsync(string sortTag, bool updatesFirst)
    {
        await Task.Yield();

        var allCache = App.Cache.GetAllModsBasic();

        var bestByName = new Dictionary<string, CacheDb.ModRow>(StringComparer.OrdinalIgnoreCase);
        var byGuid = new Dictionary<string, CacheDb.ModRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in allCache)
        {
            var nameKey = (m.Name ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(nameKey))
                if (!bestByName.TryGetValue(nameKey, out var prev) || IsBetter(m, prev))
                    bestByName[nameKey] = m;

            if (!string.IsNullOrWhiteSpace(m.Guid))
                byGuid[m.Guid] = m;
        }

        string ResolveThumb(string name, string guid)
        {
            string choose(CacheDb.ModRow? r)
            {
                var u = ForgeClient.ResolveImageUrl(r?.Thumbnail);
                return string.IsNullOrWhiteSpace(u) ? "" : u;
            }

            if (!string.IsNullOrWhiteSpace(guid) && byGuid.TryGetValue(guid, out var r1))
            {
                var u = choose(r1);
                if (!string.IsNullOrWhiteSpace(u)) return u;
            }

            if (!string.IsNullOrWhiteSpace(name) && bestByName.TryGetValue(name, out var r2))
            {
                var u = choose(r2);
                if (!string.IsNullOrWhiteSpace(u)) return u;
            }

            static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

            var norm = Norm(name);
            var hit = allCache.FirstOrDefault(r =>
            {
                var n = Norm(r.Name ?? "");
                return n.Contains(norm, StringComparison.Ordinal) || norm.Contains(n, StringComparison.Ordinal);
            });

            var u3 = ForgeClient.ResolveImageUrl(hit?.Thumbnail);
            if (!string.IsNullOrWhiteSpace(u3)) return u3;

            var safeName = Uri.EscapeDataString(name ?? "");
            return $"https://placehold.co/165x165/31343C/EEE.png?text={safeName}&font=source-sans-pro";
        }

        (string detail, bool hasPage, bool isCustom, List<string> authors, List<InstalledModRow.SourceButton> sources, string category)
            ResolveMeta(string name, string guid)
        {
            CacheDb.ModRow? row = null;
            if (!string.IsNullOrWhiteSpace(guid) && byGuid.TryGetValue(guid, out var gRow)) row = gRow;
            else if (bestByName.TryGetValue(name, out var nRow)) row = nRow;

            var detail = row?.DetailUrl ?? "";
            var hasPage = !string.IsNullOrWhiteSpace(detail);
            var custom = row == null;

            var authors = row?.Owners?.ToList() ?? new List<string>();
            var sources = new List<InstalledModRow.SourceButton>();
            if (row != null)
                foreach (var s in App.Cache.GetSourcesForMod(row.Id))
                    sources.Add(new InstalledModRow.SourceButton
                    {
                        Url = s.url,
                        Label = string.IsNullOrWhiteSpace(s.label) ? "Source" : s.label!
                    });

            var category = row?.CategorySlug ?? row?.CategoryName ?? "";
            return (detail, hasPage, custom, authors, sources, category);
        }

        var records = App.Db.ListMods();
        var groups = records
            .GroupBy(r => !string.IsNullOrWhiteSpace(r.guid) ? "guid:" + r.guid.ToLowerInvariant() : "name:" + r.name.ToLowerInvariant())
            .ToList();

        var detectedAB = App.GetDetectedSptAB();

        var rows = new ConcurrentBag<InstalledModRow>();
        var throttler = new SemaphoreSlim(6);

        var tasks = groups.Select(async g =>
        {
            await throttler.WaitAsync();
            try
            {
                var name = g.Select(x => x.name).FirstOrDefault() ?? "";
                var guid = g.Select(x => x.guid).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";

                var newestUnix = g.Select(x => x.installed_at).DefaultIfEmpty(0L).Max();
                var installedAt = newestUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(newestUnix) : (DateTimeOffset?)null;

                var installedBest = g.Select(x => x.version).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                var installedVersion = installedBest.FirstOrDefault() ?? "0.0.0";
                foreach (var v in installedBest)
                    if (SemverUtil.TryParseStrict(v, out var vs) &&
                        SemverUtil.TryParseStrict(installedVersion, out var vb) &&
                        vs.CompareSortOrderTo(vb) > 0)
                        installedVersion = v;

                var fileCount = (int)g.Sum(x => x.fileCount);

                var thumbUrl = ResolveThumb(name, guid);
                var (detail, hasPage, custom, authors, sources, category) = ResolveMeta(name, guid);

                CacheDb.ModRow? cacheRow = null;
                if (!string.IsNullOrWhiteSpace(guid) && byGuid.TryGetValue(guid, out var gRow)) cacheRow = gRow;
                else if (bestByName.TryGetValue(name, out var nRow)) cacheRow = nRow;

                var versionsForRow = new List<ForgeClient.ModVersion>();
                if (cacheRow != null)
                {
                    try
                    {
                        await App.Cache.EnsureVersionsCachedAsync(cacheRow.Id).ConfigureAwait(false);
                        versionsForRow = await Storage.CacheDb.GetVersionsAsync(cacheRow.Id).ConfigureAwait(false);
                    }
                    catch
                    {
                        // good girl action
                    }
                }

                var sorted = OrderVersionsForRow(versionsForRow);
                var filtered = string.IsNullOrWhiteSpace(detectedAB)
                    ? sorted
                    : sorted.Where(v => string.Equals(ToABFromConstraint(v.SptVersionConstraint), detectedAB, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                var latestForAB = filtered.FirstOrDefault();
                var latestVerText = latestForAB?.Version ?? "";
                var canUpdate = latestForAB != null && !string.IsNullOrWhiteSpace(latestForAB.Version) && IsUpdate(installedVersion, latestForAB.Version!);

                bool hasEditableConfigs = false;
                try
                {
                    var modIds = g.Select(x => x.mod_id).Distinct().ToList();
                    foreach (var mid in modIds)
                    {
                        var files = App.Db.ListFilesForModId(mid);
                        if (files is { Count: > 0 })
                        {
                            foreach (var (path, _) in files)
                            {
                                if (IsEditablePath(path))
                                {
                                    hasEditableConfigs = true;
                                    break;
                                }
                            }
                        }

                        if (hasEditableConfigs) break;
                    }
                }
                catch
                {
                    // good girl action
                }

                rows.Add(new InstalledModRow
                {
                    ModIds = g.Select(x => x.mod_id).Distinct().ToList(),
                    Name = name,
                    Guid = guid,
                    InstalledVersion = installedVersion,
                    FileCount = fileCount,
                    Thumbnail = thumbUrl,
                    DetailUrl = detail,
                    HasPage = hasPage,
                    IsCustom = custom,
                    IsOutdated = canUpdate,
                    CanUpdate = canUpdate,
                    LatestVersionText = latestVerText,
                    Versions = filtered,
                    Latest = latestForAB,
                    Authors = authors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    SourceButtons = sources,
                    Category = string.IsNullOrWhiteSpace(category) ? "(Uncategorized)" : category,
                    InstalledAt = installedAt,
                    InstalledAtText = installedAt.HasValue ? installedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "",
                    LatestPublishedText = latestForAB?.PublishedAt.HasValue == true ? latestForAB!.PublishedAt!.Value.LocalDateTime.ToString("yyyy-MM-dd") : "",
                    HasEditableConfigs = hasEditableConfigs
                });
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        IEnumerable<InstalledModRow> ordered = sortTag switch
        {
            "installed_desc" => rows.OrderByDescending(r => r.InstalledAt ?? DateTimeOffset.MinValue),
            "installed_asc" => rows.OrderBy(r => r.InstalledAt ?? DateTimeOffset.MaxValue),
            _ => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        };

        if (updatesFirst)
            ordered = ordered.OrderBy(r => r.IsOutdated ? 0 : 1).ThenBy(r => 0);

        var final = ordered.ToList();
        var statusText = final.Count == 0 ? "No installed mods." : $"{final.Count} installed mods";
        return (final, statusText);
    }

    private static IEnumerable<InstalledModRow> ApplySearchFilter(IEnumerable<InstalledModRow> rows, string query)
    {
        if (rows is null) return Array.Empty<InstalledModRow>();

        var isOutdated = !string.IsNullOrWhiteSpace(query) &&
                           query.IndexOf("#outdated", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isOutdated)
            rows = rows.Where(r => r.IsOutdated);

        var q = (query ?? string.Empty).Replace("#outdated", "", StringComparison.OrdinalIgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(q)) return rows;

        var authorMode = q.StartsWith("@");
        var needle = authorMode ? q[1..] : q;

        return rows.Where(r =>
        {
            if (authorMode)
                return r.Authors?.Any(a => a?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) == true;

            return
                (!string.IsNullOrWhiteSpace(r.Name)      && r.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(r.Guid)      && r.Guid.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(r.Category)  && r.Category.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(r.DetailUrl) && r.DetailUrl.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (r.Authors?.Any(a => a?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) == true);
        });
    }

    private void OnChangeVersion(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not ForgeClient.ModVersion chosen || string.IsNullOrWhiteSpace(chosen?.Link))
        {
            App.Toasts.Show("Pick a version to install.");
            return;
        }

        var row = btn.DataContext as InstalledModRow ?? ModsList.SelectedItem as InstalledModRow;
        if (row is null)
        {
            App.Toasts.Show("No mod selected.");
            return;
        }

        var detectedAB = App.GetDetectedSptAB();
        var chosenAB = ToABFromConstraint(chosen.SptVersionConstraint);
        if (!string.IsNullOrWhiteSpace(detectedAB) &&
            !string.Equals(detectedAB, chosenAB, StringComparison.OrdinalIgnoreCase))
        {
            App.Toasts.Show($"This version targets SPT {chosenAB}, but your install is {detectedAB}.");
            return;
        }

        var selectedVer = chosen.Version ?? "0.0.0";
        if (string.Equals(selectedVer, row.InstalledVersion, StringComparison.OrdinalIgnoreCase))
        {
            App.Toasts.Show("Already on this version.");
            return;
        }

        App.Queue.EnqueueRemote(row.Name, chosen.Link!, selectedVer, row.Guid ?? "");
        App.Toasts.Show($"Changing {row.Name} → v{selectedVer}");
    }

    private void OnVersionBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ComboBox cb) return;

        void EnsureSelection()
        {
            if (cb.SelectedIndex < 0 && cb.ItemCount > 0)
                cb.SelectedIndex = 0;
        }

        Dispatcher.UIThread.Post(EnsureSelection, DispatcherPriority.Background);

        cb.PropertyChanged += (_, pe) =>
        {
            if (pe.Property == ItemsControl.ItemsSourceProperty ||
                pe.Property == ItemsControl.ItemCountProperty)
                Dispatcher.UIThread.Post(EnsureSelection, DispatcherPriority.Background);
        };
    }

    private void OnAuthorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string author || string.IsNullOrWhiteSpace(author))
            return;

        var main = (MainWindow?)TopLevel.GetTopLevel(this);
        if (main is not null)
        {
            var tabs = main.FindDescendantOfType<TabControl>();
            if (tabs is not null) tabs.SelectedIndex = 0;
            var browse = main.FindDescendantOfType<BrowseModsPage>();
            browse?.SearchByAuthor(author);
        }
    }

    private void OnOpenPageFromTitle(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var url = b.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            App.Toasts.Show("No page URL for this mod.");
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            App.Toasts.Show("Could not open browser.");
        }
    }

    private void OnOpenPageFromContext(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var url = mi.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            App.Toasts.Show("No page URL for this mod.");
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            App.Toasts.Show("Could not open browser.");
        }
    }

    private async void OnCopyLink(object? sender, RoutedEventArgs e)
    {
        var url = (sender as Control)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            App.Toasts.Show("No link to copy.");
            return;
        }

        try
        {
            var tl = TopLevel.GetTopLevel(this);
            if (tl?.Clipboard is not null)
            {
                await tl.Clipboard.SetTextAsync(url);
                App.Toasts.Show("Link copied.");
            }
            else
            {
                App.Toasts.Show(url);
            }
        }
        catch
        {
            App.Toasts.Show("Could not copy to clipboard.");
        }
    }    
    
    private async void OnCopyGuid(object? sender, RoutedEventArgs e)
    {
        var guid = (sender as Control)?.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(guid)) return;

        try
        {
            var tl = TopLevel.GetTopLevel(this);
            if (tl?.Clipboard != null)
            {
                await tl.Clipboard.SetTextAsync(guid);
                App.Toasts.Show("GUID copied.");
            }
        }
        catch
        {
            App.Toasts.Show("Could not copy GUID.");
        }
    }
    
    private void OnOpenThumb(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var url = mi.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            App.Toasts.Show("Could not open image.");
        }
    }

    private void OnAuthorsWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            var dx = (e.Delta.Y != 0 ? -e.Delta.Y : -e.Delta.X) * 40;
            var extentX = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
            var newX = Math.Clamp(sv.Offset.X + dx, 0, extentX);
            sv.Offset = new Vector(newX, sv.Offset.Y);
            e.Handled = true;
        }
    }

    private static List<ForgeClient.ModVersion> OrderVersionsForRow(IEnumerable<ForgeClient.ModVersion> versions)
    {
        var list = versions?.ToList() ?? new List<ForgeClient.ModVersion>();
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

    private static int CompareSemverDesc(string? a, string? b)
    {
        var sa = a ?? "";
        var sb = b ?? "";
        var okA = SemverUtil.TryParseStrict(sa, out var sva);
        var okB = SemverUtil.TryParseStrict(sb, out var svb);
        if (okA && okB) return svb.CompareSortOrderTo(sva);
        return string.Compare(sb, sa, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToABFromConstraint(string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint)) return "";
        var norm = SemverUtil.NormalizeToThreeParts(constraint) ?? constraint;
        var p = norm.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 2) return $"{p[0]}.{p[1]}";
        var m = Regex.Match(constraint, @"(\d+)\.(\d+)");
        return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : "";
    }

    private static bool IsBetter(CacheDb.ModRow a, CacheDb.ModRow b)
    {
        var au = a.UpdatedAt ?? DateTimeOffset.MinValue;
        var bu = b.UpdatedAt ?? DateTimeOffset.MinValue;
        if (au != bu) return au > bu;
        if (a.Downloads != b.Downloads) return a.Downloads > b.Downloads;
        var ap = a.PublishedAt ?? DateTimeOffset.MinValue;
        var bp = b.PublishedAt ?? DateTimeOffset.MinValue;
        if (ap != bp) return ap > bp;
        var aslug = string.IsNullOrWhiteSpace(a.Slug) ? 0 : 1;
        var bslug = string.IsNullOrWhiteSpace(b.Slug) ? 0 : 1;
        return aslug > bslug;
    }
}