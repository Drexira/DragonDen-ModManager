using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private volatile bool _isBuilding;
    private int _statusEpoch;

    public InstalledModsPage()
    {
        InitializeComponent();

        RefreshBtn.Click += async (_, __) => await ScanDiskAsync();
        CheckUpdatesBtn.Click += async (_, __) => await CheckUpdates();
        UpdateAllBtn.Click += async (_, __) => await UpdateAll();
        OpenClientBtn.Click += (_, __) => OpenFolder(Spt.ClientModsPath);
        OpenServerBtn.Click += (_, __) => OpenFolder(Spt.ServerModsPath);
        UninstallAllBtn.Click += async (_, __) => await UninstallAllAsync();

        EnableAllBtn.Click += async (_, __) => await EnableAllAsync();
        DisableAllBtn.Click += async (_, __) => await DisableAllAsync();

        SortBox.SelectionChanged += async (_, __) => await RefreshRows();
        UpdatesFirstChk.IsCheckedChanged += async (_, __) => await RefreshRows();
        HideDisabledChk.IsCheckedChanged += async (_, __) => await RefreshRows();
        
        _searchDebounce.Stop();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(1000);
        _searchDebounce.Tick -= OnSearchDebounceTick;
        _searchDebounce.Tick += OnSearchDebounceTick;

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
                _searchDebounce.Stop();
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
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else
            {
                Notifications.Current.ShowError("Missing Folder", $"The folder '{path}' could not be found.");
                Console.WriteLine($"[InstalledModsPage] OpenFolder: target folder missing → {path}");
            }
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Open Folder Failed", $"Could not open '{path}'. Check if the folder exists or is accessible.");
            Console.WriteLine($"[InstalledModsPage] OpenFolder exception: {ex.Message}");
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

        if (!_isBuilding)
            SetStatus(updatable == 0 ? "All up to date" : $"{updatable} mod(s) have updates");
        await RefreshRows();
    }
    
    private async void OnSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounce.Stop();
        _searchText = (SearchBox.Text ?? "").Trim();
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
                    var lv = latestForAB.Version ?? "Custom Install";
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

        if (!_isBuilding)
            SetStatus(queued == 0 ? "No updates queued" : $"Queued {queued} update(s)");
        await RefreshRows();
    }

    private async Task DisableAllAsync()
    {
        var rows = (ModsList.ItemsSource as IEnumerable<InstalledModRow>)?.ToList() ?? new();
        var targets = rows.Where(r => !r.IsDisabled && r.Versions?.Count > 0).ToList();
        if (targets.Count == 0)
        {
            Notifications.Current.ShowWarning("Nothing To Disable", "All visible mods are already disabled or custom installs.");
            Console.WriteLine($"[InstalledModsPage] DisableAllAsync: no eligible mods to disable.");
            return;
        }
        var queued = 0;
        foreach (var r in targets)
        {
            if (r.ModIds is { Count: > 0 })
            {
                App.Queue.EnqueueDisable(r.Name, r.ModIds);
                queued++;
            }
            await Task.Delay(40);
        }
        Notifications.Current.ShowSuccess("Queued Disables", $"Queued {queued} disable job(s).");
        Console.WriteLine($"[InstalledModsPage] DisableAllAsync: queued {queued} jobs.");
    }

    private async Task EnableAllAsync()
    {
        var rows = (ModsList.ItemsSource as IEnumerable<InstalledModRow>)?.ToList() ?? new();
        var targets = rows.Where(r => r.IsDisabled).ToList();

        if (targets.Count == 0)
        {
            Notifications.Current.ShowWarning("Nothing To Enable", "No disabled mods in the current view.");
            Console.WriteLine("[InstalledModsPage] EnableAllAsync: no eligible mods to enable.");
            return;
        }
        var queued = 0;
        foreach (var r in targets)
        {
            if (r.ModIds is { Count: > 0 })
            {
                App.Queue.EnqueueEnable(r.Name, r.ModIds);
                queued++;
            }
            await Task.Delay(40);
        }
        Notifications.Current.ShowSuccess("Queued Enables", $"Queued {queued} enable job(s).");
        Console.WriteLine($"[InstalledModsPage] EnableAllAsync: queued {queued} jobs.");
    }

    private async void OnAnyButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button b) return;
        if (b.DataContext is InstalledModRow dc && dc.IsDisabled)
        {
            if (b.Classes?.Contains("btn-trash") == true && b.Tag is InstalledModRow rowDisabledTrash)
            {
                var owner = (Window?)TopLevel.GetTopLevel(this);
                var dlg = new ConfirmUninstallDialog(rowDisabledTrash.Name);
                var doIt = owner != null ? await dlg.ShowDialog<bool>(owner) : false;
                if (!doIt) return;
                if (rowDisabledTrash.ModIds is { Count: > 0 })
                {
                    App.Queue.EnqueueUninstall(rowDisabledTrash.Name, rowDisabledTrash.ModIds);
                    Notifications.Current.ShowSuccess("Uninstall Queued", $"'{rowDisabledTrash.Name}' will be uninstalled.");
                    Console.WriteLine($"[InstalledModsPage] Queued uninstall for disabled mod → {rowDisabledTrash.Name}");
                    await RefreshRows();
                }

                return;
            }

            return;
        }

        if (b.Classes?.Contains("btn-trash") == true && b.Tag is InstalledModRow rowTrash)
        {
            var owner = (Window?)TopLevel.GetTopLevel(this);
            var dlg = new ConfirmUninstallDialog(rowTrash.Name);
            var doIt = owner != null ? await dlg.ShowDialog<bool>(owner) : false;
            if (!doIt) return;
            if (rowTrash.ModIds is { Count: > 0 })
            {
                App.Queue.EnqueueUninstall(rowTrash.Name, rowTrash.ModIds);
                Notifications.Current.ShowSuccess("Uninstall Queued", $"'{rowTrash.Name}' will be uninstalled.");
                Console.WriteLine($"[InstalledModsPage] Queued uninstall → {rowTrash.Name}");
                await RefreshRows();
            }

            return;
        }

        var content = b.Content?.ToString() ?? "";
        if (content.Equals("List Files", StringComparison.OrdinalIgnoreCase))
        {
            if (b.Tag is InstalledModRow row4)
            {
                if (row4.IsDisabled)
                {
                    Notifications.Current.ShowWarning("Mod Disabled", "Enable this mod to list its files.");
                    Console.WriteLine($"[InstalledModsPage] List Files click blocked: mod disabled → {row4.Name}");
                    return;
                }
                await ShowFilesDialog(row4);
            }
            return;
        }

        if (content.Equals("Edit Configs", StringComparison.OrdinalIgnoreCase))
        {
            if (b.Tag is InstalledModRow row5)
            {
                if (row5.IsDisabled)
                {
                    Notifications.Current.ShowWarning("Mod Disabled", "Enable this mod to edit its configs.");
                    Console.WriteLine($"[InstalledModsPage] Edit Configs click blocked: mod disabled → {row5.Name}");
                    return;
                }
                await ShowConfigDialog(row5);
            }
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
            }
        var files = merged
            .Select(t =>
            {
                var rel = (t.path ?? "").Replace('\\', '/');
                var tgt = (t.target ?? "client").ToLowerInvariant();
                var root = tgt.Equals("server", StringComparison.OrdinalIgnoreCase) ? Spt.ServerModsPath : Spt.ClientModsPath;
                var full = NormalizeFullPath(root, rel);
                return (path: full, target: tgt);
            })
            .OrderBy(t => t.target, StringComparer.Ordinal)
            .ThenBy(t => t.path, StringComparer.OrdinalIgnoreCase)
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
            try
            {
                var part = App.Db.ListFilesForModId(id);
                if (part is { Count: > 0 }) merged.AddRange(part);
            }
            catch
            {
            }
        var items = new List<ConfigDialog.ConfigItem>();
        foreach (var (rel0, target0) in merged)
        {
            var rel = (rel0 ?? "").Replace('\\', '/');
            if (!IsEditablePath(rel)) continue;
            var tgt = (target0 ?? "client").ToLowerInvariant();
            var root = tgt.Equals("server", StringComparison.OrdinalIgnoreCase) ? Spt.ServerModsPath : Spt.ClientModsPath;
            var full = NormalizeFullPath(root, rel);
            items.Add(new ConfigDialog.ConfigItem
            {
                DisplayPath = $"{tgt} • {rel}",
                FullPath = full
            });
        }
        if (items.Count == 0)
        {
            Notifications.Current.ShowError("No Configs Found", $"No editable config files exist for '{row.Name}'.");
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
            catch (Exception ex)
            {
                Notifications.Current.ShowError("Source Link Failed", $"Could not open source link for '{b.Content}'.");
                Console.WriteLine($"[InstalledModsPage] OpenSource link error: {ex.Message}");
            }
    }

    private async Task ScanDiskAsync()
    {
        if (!_isBuilding) SetStatus("Scanning mods...");
        var stats = await Task.Run(() => InstalledScanner.ImportFromDisk());

        var pruned = App.Db.PruneRemovedMods();

        var msg = $"Refreshed: imported {stats.imported}, updated {stats.updated}, skipped {stats.skipped}, removed {pruned} missing.";
        if (!_isBuilding) SetStatus(msg);
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
        var rows = (ModsList.ItemsSource as IEnumerable<InstalledModRow>)?.ToList() ?? new();
        var targets = rows.Where(r => r.ModIds is { Count: > 0 }).ToList();
        if (targets.Count == 0)
        {
            Notifications.Current.ShowWarning("No Mods Found", "There are no installed mods to uninstall.");
            Console.WriteLine("[InstalledModsPage] UninstallAllAsync: no mods to uninstall.");
            return;
        }
        var owner = (Window?)TopLevel.GetTopLevel(this);
        var ok = owner != null ? await new ConfirmUninstallDialog("All selected mods").ShowDialog<bool>(owner) : false;
        if (!ok) return;
        var queued = 0;
        foreach (var r in targets)
        {
            App.Queue.EnqueueUninstall(r.Name, r.ModIds);
            queued++;
            await Task.Delay(25);
        }
        Notifications.Current.ShowSuccess("Uninstalls Queued", $"Queued {queued} uninstall job(s).");
        Console.WriteLine($"[InstalledModsPage] UninstallAllAsync: queued {queued} jobs.");
    }

    private void OnDisableMod(object? s, RoutedEventArgs e)
    {
        if ((s as Control)?.Tag is not InstalledModRow row) return;
        if (row.ModIds is null || row.ModIds.Count == 0) return;
        App.Queue.EnqueueDisable(row.Name, row.ModIds);
        Notifications.Current.ShowSuccess("Disable Queued", $"{row.Name} will be moved to Disabled Mods.");
        Console.WriteLine($"[InstalledModsPage] Queued disable → {row.Name}");
    }

    private void OnEnableMod(object? s, RoutedEventArgs e)
    {
        if ((s as Control)?.Tag is not InstalledModRow row) return;
        if (row.ModIds is null || row.ModIds.Count == 0) return;
        App.Queue.EnqueueEnable(row.Name, row.ModIds);
        Notifications.Current.ShowSuccess("Enable Queued", $"{row.Name} will be restored.");
        Console.WriteLine($"[InstalledModsPage] Queued enable → {row.Name}");
    }

    public async Task RefreshRows()
    {
        var sortTag = ((SortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "alpha").ToLowerInvariant();
        var updatesFirst = UpdatesFirstChk?.IsChecked ?? false;
        var hideDisabled = HideDisabledChk?.IsChecked ?? false;

        var visible = new ObservableCollection<InstalledModRow>();
        ModsList.ItemsSource = visible;

        var comparer = BuildComparer(sortTag, updatesFirst);

        var totalCount = 0;
        var visibleCount = 0;
        var loading = true;
        const string suffix = " (updating in progress)";

        _isBuilding = true;
        var myEpoch = Interlocked.Increment(ref _statusEpoch);

        bool PassesFilters(InstalledModRow r)
        {
            var single = new[] { r };
            var matched = ApplySearchFilter(single, _searchText).Any();
            if (hideDisabled) matched = matched && !r.IsDisabled;
            return matched;
        }

        void UpdateStatusLive()
        {
            var baseText = totalCount == 0
                ? "Loading installed mods..."
                : (visibleCount == totalCount
                    ? $"{visibleCount} installed mods"
                    : $"Showing {visibleCount} of {totalCount} installed");

            SetStatus(loading ? (baseText + suffix) : baseText, myEpoch);
        }

        var allRows = new ConcurrentBag<InstalledModRow>();

        var result = await BuildRowsStreamingAsync(
            sortTag,
            updatesFirst,
            row =>
            {
                allRows.Add(row);
                Interlocked.Increment(ref totalCount);

                if (PassesFilters(row))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        InsertSorted(visible, row, comparer);
                        visibleCount++;
                        UpdateStatusLive();
                    });
                }
                else
                {
                    Dispatcher.UIThread.Post(UpdateStatusLive);
                }
            });

        loading = false;

        var finalList = allRows.ToList();
        var finalFiltered = ApplySearchFilter(finalList, _searchText).ToList();
        if (hideDisabled)
            finalFiltered = finalFiltered.Where(r => !r.IsDisabled).ToList();

        var finalSorted = finalFiltered.OrderBy(r => r, comparer).ToList();

        Dispatcher.UIThread.Post(() =>
        {
            visible.Clear();
            foreach (var r in finalSorted)
                visible.Add(r);
        });

        var finalStatus = finalFiltered.Count == finalList.Count
            ? result.statusText
            : $"Showing {finalFiltered.Count} of {finalList.Count} installed";

        _isBuilding = false;
        SetStatus(finalStatus, myEpoch, force: true);

        var detectedAB = App.GetDetectedSptAB();
        _ = WarmAndApplyVersionsAsync(result.coldMap, detectedAB, comparer, visible, myEpoch);
    }

    private static IComparer<InstalledModRow> BuildComparer(string sortTag, bool updatesFirst)
    {
        int UpdatesKey(InstalledModRow r) => updatesFirst ? (r.IsOutdated ? 0 : 1) : 0;

        return Comparer<InstalledModRow>.Create((a, b) =>
        {
            var u = UpdatesKey(a).CompareTo(UpdatesKey(b));
            if (u != 0) return u;

            switch (sortTag)
            {
                case "installed_desc":
                {
                    var c = (b.InstalledAt ?? DateTimeOffset.MinValue).CompareTo(a.InstalledAt ?? DateTimeOffset.MinValue);
                    if (c != 0) return c;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                }
                case "installed_asc":
                {
                    var c = (a.InstalledAt ?? DateTimeOffset.MaxValue).CompareTo(b.InstalledAt ?? DateTimeOffset.MaxValue);
                    if (c != 0) return c;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                }
                case "enabled_first":
                {
                    var c = (a.IsDisabled ? 1 : 0).CompareTo(b.IsDisabled ? 1 : 0);
                    if (c != 0) return c;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                }
                case "enabled_last":
                {
                    var c = (a.IsDisabled ? 0 : 1).CompareTo(b.IsDisabled ? 0 : 1);
                    if (c != 0) return c;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                }
                default:
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            }
        });
    }
    
    private static void InsertSorted(ObservableCollection<InstalledModRow> col, InstalledModRow item, IComparer<InstalledModRow> cmp)
    {
        var lo = 0;
        var hi = col.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (cmp.Compare(item, col[mid]) > 0) lo = mid + 1; else hi = mid;
        }
        col.Insert(lo, item);
    }

    private void SetStatus(string text, int? epoch = null, bool force = false)
    {
        if (!force && _isBuilding && epoch.HasValue && epoch.Value != _statusEpoch)
            return;

        StatusText.Text = text;
    }

    private async Task<(List<InstalledModRow> final, string statusText, Dictionary<int, InstalledModRow> coldMap)> BuildRowsStreamingAsync(
        string sortTag, bool updatesFirst, Action<InstalledModRow>? onProgress)
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

        var coldMap = new ConcurrentDictionary<int, InstalledModRow>();

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
            return $"https://placehold.co/120x120/31343C/EEE.png?text={safeName}&font=source-sans-pro";
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
        var throttler = new SemaphoreSlim(Math.Max(16, Environment.ProcessorCount * 2));

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
                        versionsForRow = await Storage.CacheDb.GetVersionsAsync(cacheRow.Id).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    if (versionsForRow.Count == 0)
                        coldMap[cacheRow.Id] = null;
                }

                var sorted = OrderVersionsForRow(versionsForRow);
                var filtered = string.IsNullOrWhiteSpace(detectedAB)
                    ? sorted
                    : sorted.Where(v => string.Equals(ToABFromConstraint(v.SptVersionConstraint), detectedAB, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                var latestForAB = filtered.FirstOrDefault();
                var latestVerText = latestForAB?.Version ?? "";
                var canUpdate = latestForAB != null && !string.IsNullOrWhiteSpace(latestForAB.Version) && IsUpdate(installedVersion, latestForAB.Version!);

                var modIds = g.Select(x => x.mod_id).Distinct().ToList();

                var isDisabled = false;
                try
                {
                    foreach (var mid in modIds)
                    {
                        if (await ModDisabler.IsDisabledAsync(mid))
                        {
                            isDisabled = true;
                            break;
                        }
                    }
                }
                catch
                {
                }

                var hasEditableConfigs = false;
                try
                {
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
                }

                var row = new InstalledModRow
                {
                    ModIds = modIds,
                    Name = name,
                    Guid = guid,
                    InstalledVersion = installedVersion,
                    FileCount = fileCount,
                    Thumbnail = thumbUrl,
                    DetailUrl = detail,
                    HasPage = hasPage,
                    IsCustom = custom,
                    IsOutdated = !isDisabled && canUpdate,
                    CanUpdate = !isDisabled && canUpdate,
                    LatestVersionText = latestVerText,
                    Versions = filtered,
                    Latest = latestForAB,
                    Authors = authors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    SourceButtons = sources,
                    Category = string.IsNullOrWhiteSpace(category) ? "(Uncategorized)" : category,
                    InstalledAt = installedAt,
                    InstalledAtText = installedAt.HasValue ? installedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "",
                    LatestPublishedText = latestForAB?.PublishedAt.HasValue == true ? latestForAB!.PublishedAt!.Value.LocalDateTime.ToString("yyyy-MM-dd") : "",
                    HasEditableConfigs = hasEditableConfigs,
                    IsDisabled = isDisabled
                };

                if (cacheRow != null && coldMap.ContainsKey(cacheRow.Id))
                    coldMap[cacheRow.Id] = row;

                rows.Add(row);
                onProgress?.Invoke(row);
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        IEnumerable<InstalledModRow> seq = rows;
        if (updatesFirst)
            seq = seq.OrderBy(r => r.IsOutdated ? 0 : 1);

        Func<InstalledModRow, int> updatesKey = r => updatesFirst ? (r.IsOutdated ? 0 : 1) : 0;

        IOrderedEnumerable<InstalledModRow>? ordered;
        switch (sortTag)
        {
            case "installed_desc":
                ordered = rows.OrderBy(updatesKey).ThenByDescending(r => r.InstalledAt ?? DateTimeOffset.MinValue).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
                break;
            case "installed_asc":
                ordered = rows.OrderBy(updatesKey).ThenBy(r => r.InstalledAt ?? DateTimeOffset.MaxValue).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
                break;
            case "enabled_first":
                ordered = rows.OrderBy(updatesKey).ThenBy(r => r.IsDisabled ? 1 : 0).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
                break;
            case "enabled_last":
                ordered = rows.OrderBy(updatesKey).ThenBy(r => r.IsDisabled ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
                break;
            default:
                ordered = rows.OrderBy(updatesKey).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
                break;
        }

        var final = ordered.ToList();
        var statusText = final.Count == 0 ? "No installed mods." : $"{final.Count} installed mods";
        return (final, statusText, coldMap.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!));
    }

    private async Task WarmAndApplyVersionsAsync(Dictionary<int, InstalledModRow> coldMap, string detectedAB, IComparer<InstalledModRow> comparer,
        ObservableCollection<InstalledModRow> visible, int epoch)
    {
        if (coldMap == null || coldMap.Count == 0) return;

        var ids = coldMap.Keys.ToList();
        var throttler = new SemaphoreSlim(8);

        var tasks = ids.Select(async id =>
        {
            await throttler.WaitAsync();
            try
            {
                await App.Cache.EnsureVersionsCachedAsync(id).ConfigureAwait(false);
                var versions = await Storage.CacheDb.GetVersionsAsync(id).ConfigureAwait(false);

                var sorted = OrderVersionsForRow(versions);
                var filtered = string.IsNullOrWhiteSpace(detectedAB)
                    ? sorted
                    : sorted.Where(v => string.Equals(ToABFromConstraint(v.SptVersionConstraint), detectedAB, StringComparison.OrdinalIgnoreCase)).ToList();

                var latest = filtered.FirstOrDefault();
                var row = coldMap[id];

                var installedVersion = row.InstalledVersion ?? "0.0.0";
                var canUpdate = latest != null && !string.IsNullOrWhiteSpace(latest.Version) && IsUpdate(installedVersion, latest.Version!);

                Dispatcher.UIThread.Post(() =>
                {
                    var idx = visible.IndexOf(row);
                    if (idx >= 0) visible.RemoveAt(idx);

                    row.Versions = filtered;
                    row.Latest = latest;
                    row.LatestVersionText = latest?.Version ?? "";
                    row.IsOutdated = !row.IsDisabled && canUpdate;
                    row.CanUpdate = !row.IsDisabled && canUpdate;
                    row.LatestPublishedText = latest?.PublishedAt.HasValue == true ? latest!.PublishedAt!.Value.LocalDateTime.ToString("yyyy-MM-dd") : "";

                    InsertSorted(visible, row, comparer);
                });
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        SetStatus(StatusText.Text.Replace(" (updating in progress)", ""), epoch, force: true);
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
                (!string.IsNullOrWhiteSpace(r.Name) && r.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(r.Guid) && r.Guid.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(r.Category) && r.Category.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrWhiteSpace(r.DetailUrl) && r.DetailUrl.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                r.Authors?.Any(a => a?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) == true;
        });
    }

    private void OnToggleEnabled(object? s, RoutedEventArgs e)
    {
        if (s is not CheckBox { Tag: InstalledModRow row } cb) return;

        var shouldEnable = cb.IsChecked == true;

        if (shouldEnable)
            OnEnableMod(cb, e);
        else
            OnDisableMod(cb, e);
    }

    private void OnUpdateFromBadge(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not InstalledModRow row) return;

        if (row.IsDisabled)
        {
            Notifications.Current.ShowWarning("Mod Disabled", "Enable this mod before updating.");
            Console.WriteLine($"[InstalledModsPage] Update badge click blocked: mod disabled → {row.Name}");
            return;
        }

        if (row.Latest?.Link is string url && !string.IsNullOrWhiteSpace(url))
        {
            b.IsEnabled = false;
            App.Queue.EnqueueRemote(row.Name, url, row.Latest?.Version ?? "Custom Install", row.Guid ?? "");
            Notifications.Current.ShowSuccess("Update Queued", $"'{row.Name}' has been added to the update queue.");
            Console.WriteLine($"[InstalledModsPage] Update badge clicked: queued update for {row.Name}");
        }
        else
        {
            Notifications.Current.ShowError("No Update Link", $"The mod '{row.Name}' has no downloadable update link.");
            Console.WriteLine($"[InstalledModsPage] Update badge click failed: no update link for {row.Name}");
        }
    }

    private async void OnOpenVersionModal(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not InstalledModRow row) return;
        if (row.IsDisabled)
        {
            Notifications.Current.ShowWarning("Mod Disabled", "Enable this mod before changing its version.");
            Console.WriteLine($"[InstalledModsPage] Open version modal blocked: mod disabled → {row.Name}");
            return;
        }

        var owner = (Window?)TopLevel.GetTopLevel(this);
        var dlg = new ChangeInstalledVersion(row);
        if (owner is not null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
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
            Notifications.Current.ShowError("Missing Page URL", "No valid mod page URL is available for this mod.");
            Console.WriteLine("[InstalledModsPage] OnOpenPageFromTitle: missing URL.");
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Page Open Failed", $"Could not open mod page: {url}");
            Console.WriteLine($"[InstalledModsPage] OnOpenPageFromTitle error: {ex.Message}");
        }
    }

    private void OnOpenPageFromContext(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var url = mi.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            Notifications.Current.ShowError("Missing Page URL", "No valid mod page URL is available for this mod.");
            Console.WriteLine("[InstalledModsPage] OnOpenPageFromContext: missing URL.");
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Page Open Failed", $"Could not open mod page: {url}");
            Console.WriteLine($"[InstalledModsPage] OnOpenPageFromContext error: {ex.Message}");
        }
    }

    private async void OnCopyLink(object? sender, RoutedEventArgs e)
    {
        var url = (sender as Control)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            Notifications.Current.ShowWarning("No Link Found", "There is no link to copy for this mod.");
            Console.WriteLine($"[InstalledModsPage] OnCopyLink: no URL to copy.");
            return;
        }

        try
        {
            var tl = TopLevel.GetTopLevel(this);
            if (tl?.Clipboard is not null)
            {
                await tl.Clipboard.SetTextAsync(url);
                Notifications.Current.ShowSuccess("Link Copied", "The link has been copied to your clipboard.");
                Console.WriteLine($"[InstalledModsPage] OnCopyLink: copied URL to clipboard.");
            }
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Copy Failed", "Unable to copy the link to clipboard.");
            Console.WriteLine($"[InstalledModsPage] CopyLink exception: {ex.Message}");
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
                Notifications.Current.ShowSuccess("GUID Copied", "The GUID has been copied to your clipboard.");
                Console.WriteLine($"[InstalledModsPage] OnCopyGuid: copied GUID to clipboard.");
            }
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Copy Failed", "Unable to copy the GUID to clipboard.");
            Console.WriteLine($"[InstalledModsPage] CopyGuid exception: {ex.Message}");
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
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Image Open Failed", "Could not open thumbnail image in browser.");
            Console.WriteLine($"[InstalledModsPage] OnOpenThumb error: {ex.Message}");
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
    
    private static string NormalizeFullPath(string baseRoot, string relOrFull)
    {
        if (string.IsNullOrWhiteSpace(relOrFull)) return baseRoot;
        var raw = relOrFull.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(raw)) return raw.Replace('/', Path.DirectorySeparatorChar);
        var baseNorm = baseRoot.Replace('\\', '/').TrimEnd('/');
        var relSegs = raw.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var baseSegs = baseNorm.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (var len = Math.Min(6, baseSegs.Length); len >= 2; len--)
        {
            var match = true;
            for (int i = 0; i < len && i < relSegs.Length; i++)
            {
                var a = baseSegs[baseSegs.Length - len + i];
                var b = relSegs[i];
                if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) { match = false; break; }
            }
            if (match)
            {
                relSegs = relSegs.Skip(len).ToArray();
                break;
            }
        }
        var trimmed = string.Join(Path.DirectorySeparatorChar.ToString(), relSegs);
        var combined = Path.Combine(baseRoot, trimmed);
        return Path.GetFullPath(combined);
    }
}