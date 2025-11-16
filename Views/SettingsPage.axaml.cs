using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.Storage;

namespace DragonDen.ModManager.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();

        App.ConfigChanged += RefreshFromConfig;

        RefreshFromConfig();

        BrowseSPTBtn.Click += OnBrowseSptSptFolder;
        BrowseDataBtn.Click += OnBrowseDataFolder;
        ResetDataBtn.Click += OnResetDataFolder;
        SaveBtn.Click += OnSave;

        SptRootBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) UpdateComputed();
        };
        DataBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) UpdateComputed();
        };
        ClientRelBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) UpdateComputed();
        };
        ServerRelBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) UpdateComputed();
        };

        ShowTokenToggle.Checked += (_, __) => ToggleTokenVisibility(true);
        ShowTokenToggle.Unchecked += (_, __) => ToggleTokenVisibility(false);

        ClearCacheBtn.Click += OnClearCache;
        ClearTempFilesBtn.Click += OnClearTemp;
        ClearLogFilesBtn.Click += (_,__) => Logger.CleanAllLogs();
    }

    private void ToggleTokenVisibility(bool show)
    {
        ForgeTokenBox.PasswordChar = show ? '\0' : '•';
        ShowTokenToggle.Content = show ? "Hide" : "Show";
    }

    public void RefreshFromConfig()
    {
        SptRootBox.Text = App.Config.Paths.SptRoot ?? "";
        DataBox.Text = App.Config.Paths.DataFolder ?? "";
        ClientRelBox.Text = App.Config.Paths.ClientModsRelative;
        ServerRelBox.Text = App.Config.Paths.ServerModsRelative;
        ForgeTokenBox.Text = App.Config.Forge.Token ?? "";
        ShowTokenToggle.IsChecked = false;
        ToggleTokenVisibility(false);
        
        ExpertModeToggle.IsChecked = App.Config.UI.ExpertMode;

        UpdateComputed();
        UpdateSptDetectionStatus();
    }

    private void OnResetDataFolder(object? s, RoutedEventArgs e)
    {
        App.Config.Paths.DataFolder = "";
        DataBox.Text = Paths.DataDir;
        App.SaveConfig();
        Notifications.Current.ShowSuccess("Data Folder Reset", "Data folder has been reset to default location.");
        Logger.Info("[SettingsPage] Data folder reset to default.");
    }

    private async void OnBrowseDataFolder(object? s, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner?.StorageProvider is null) return;

        var pick = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Pick a folder to be the new Data Folder."
        });

        if (pick.Count == 0) return;

        var chosen = pick[0].Path.LocalPath;
        if (!Directory.Exists(chosen))
        {
            Notifications.Current.ShowError("Invalid Folder", "Selected folder doesn't exist.");
            Logger.Error("[SettingsPage] Selected data folder doesn't exist: " + chosen);
            return;
        }

        App.Config.Paths.DataFolder = chosen;
        App.SaveConfig();
        Notifications.Current.ShowSuccess("Data Folder Changed", "Data folder updated successfully.");
        Logger.Info("[SettingsPage] Data folder changed to: " + chosen);
    }

    private async void OnBrowseSptSptFolder(object? s, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner?.StorageProvider is null) return;

        var pick = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Choose an SPT folder."
        });

        if (pick.Count == 0) return;

        var chosen = pick[0].Path.LocalPath;
        if (!Directory.Exists(chosen))
        {
            Notifications.Current.ShowError("Invalid Folder", "Selected SPT folder doesn't exist.");
            Logger.Error("[SettingsPage] Selected SPT folder doesn't exist: " + chosen);
            return;
        }

        var eftExe = Path.Combine(chosen, "EscapeFromTarkov.exe");
        var bepin = Path.Combine(chosen, "BepInEx");
        if (!File.Exists(eftExe) || !Directory.Exists(bepin))
        {
            Notifications.Current.ShowError(
                "Invalid SPT Folder",
                "EscapeFromTarkov.exe and BepInEx folder must be in the selected folder."
            );
            Logger.Error("[SettingsPage] Invalid SPT folder: missing EscapeFromTarkov.exe or BepInEx in " + chosen);
            return;
        }

        if (!TryFindSptExe(chosen, out var exePath))
        {
            Notifications.Current.ShowError("Invalid SPT Folder", "Could not locate SPT.Server.exe in selected directory.");
            Logger.Error("[SettingsPage] Invalid SPT folder: missing SPT.Server.exe in " + chosen);
            return;
        }

        var major = 0;
        var friendly = "";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var fv = info?.FileVersion ?? "";
            var parts = fv.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) int.TryParse(parts[0], out major);
            friendly = parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : fv;
        }
        catch (Exception ex)
        {
            Logger.Error("[SettingsPage] Failed to read SPT version: " + ex);
        }

        var clientRel = "BepInEx/plugins";
        if (Directory.Exists(Path.Combine(chosen, "BepInEx", "plugins")))
            clientRel = "BepInEx/plugins";

        string serverRel;
        if (Directory.Exists(Path.Combine(chosen, "SPT", "user", "mods")))
            serverRel = "SPT/user/mods";
        else if (Directory.Exists(Path.Combine(chosen, "user", "mods")))
            serverRel = "user/mods";
        else
            serverRel = major >= 4 ? "SPT/user/mods" : "user/mods";

        var oldRoot = App.Config.Paths.SptRoot ?? "";
        App.Config.Paths.SptRoot = chosen;
        App.Config.Paths.ClientModsRelative = clientRel;
        App.Config.Paths.ServerModsRelative = serverRel;
        App.SaveConfig();

        if (!string.Equals(oldRoot, chosen, StringComparison.OrdinalIgnoreCase))
            try
            {
                var newDbPath = Paths.ModsDbPath;
                App.Db = new Db(newDbPath);
                App.Db.Init();
                Notifications.Current.ShowSuccess("Database Switched", $"Switched mods DB → {Path.GetFileName(newDbPath)}");
                Logger.Info("[SettingsPage] Mods DB switched to: " + newDbPath);
            }
            catch (Exception ex)
            {
                Notifications.Current.ShowError("Database Error", "Failed to switch mods database.");
                Logger.Error("[SettingsPage] Failed to switch mods database: " + ex);
            }

        SptRootBox.Text = chosen;
        ClientRelBox.Text = clientRel;
        ServerRelBox.Text = serverRel;
        UpdateComputed();
        UpdateSptDetectionStatus();

        Notifications.Current.ShowSuccess("SPT Folder Saved", string.IsNullOrWhiteSpace(friendly)
            ? "SPT folder saved successfully."
            : $"SPT {friendly} detected and saved.");
        Logger.Info("[SettingsPage] SPT folder saved and config updated.");

        var main = (MainWindow?)TopLevel.GetTopLevel(this);
        var tabs = main.FindDescendantOfType<TabControl>();
        if (tabs is not null) tabs.SelectedIndex = 1;
        var installed = main?.FindDescendantOfType<InstalledModsPage>();
        _ = installed?.RefreshFromSettingsAsync();
    }

    private static bool TryFindSptExe(string root, out string exePath)
    {
        var p1 = Path.Combine(root, "SPT.Server.exe");
        var p2 = Path.Combine(root, "SPT", "SPT.Server.exe");
        if (File.Exists(p1))
        {
            exePath = p1;
            return true;
        }

        if (File.Exists(p2))
        {
            exePath = p2;
            return true;
        }

        exePath = "";
        return false;
    }

    private void OnSave(object? s, RoutedEventArgs e)
    {
        var oldRoot = App.Config.Paths.SptRoot ?? "";

        App.Config.Paths.SptRoot = (SptRootBox.Text ?? "").Trim();
        App.Config.Paths.DataFolder = (DataBox.Text ?? "data").Trim();
        App.Config.Paths.ClientModsRelative = ClientRelBox.Text ?? "BepInEx/plugins";
        App.Config.Paths.ServerModsRelative = ServerRelBox.Text ?? "SPT/user/mods";
        App.Config.Forge.Token = (ForgeTokenBox.Text ?? "").Trim();
        App.Config.UI.ExpertMode = ExpertModeToggle.IsChecked == true;

        App.SaveConfig();
        App.RaiseConfigChanged();
        var newRoot = App.Config.Paths.SptRoot ?? "";
        if (!string.Equals(oldRoot, newRoot, StringComparison.OrdinalIgnoreCase))
            try
            {
                var newDbPath = Paths.ModsDbPath;
                App.Db = new Db(newDbPath);
                App.Db.Init();
                Notifications.Current.ShowSuccess("Database Switched", $"Switched mods DB → {Path.GetFileName(newDbPath)}");
                Logger.Info("[SettingsPage] Mods DB switched to: " + newDbPath);
            }
            catch (Exception ex)
            {
                Notifications.Current.ShowError("Database Error", "Failed to switch mods database.");
                Logger.Error("[SettingsPage] Failed to switch mods database: " + ex);
            }

        RefreshFromConfig();
        Notifications.Current.ShowSuccess("Settings Saved", "All settings have been saved successfully.");
        Logger.Info("[SettingsPage] Settings saved and configuration updated.");

        var main = (MainWindow?)TopLevel.GetTopLevel(this);
        var installed = main?.FindDescendantOfType<InstalledModsPage>();
        _ = installed?.RefreshFromSettingsAsync();
        installed?.GetType().GetMethod("RefreshRows", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(installed, null);
    }

    private void UpdateComputed()
    {
        var root = SptRootBox.Text ?? "";
        var client = (ClientRelBox.Text ?? "").Replace('/', Path.DirectorySeparatorChar);
        var server = (ServerRelBox.Text ?? "").Replace('/', Path.DirectorySeparatorChar);

        ClientFullText.Text = string.IsNullOrWhiteSpace(root) ? "" : Path.Combine(root, client);
        ServerFullText.Text = string.IsNullOrWhiteSpace(root) ? "" : Path.Combine(root, server);
    }

    private void UpdateSptDetectionStatus()
    {
        var root = SptRootBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            SptStatusText.Text = "No folder selected.";
            return;
        }

        if (TryFindSptExe(root, out var exePath))
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                var fv = info?.FileVersion ?? "";
                var parts = fv.Split('.', StringSplitOptions.RemoveEmptyEntries);
                var friendly = parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : fv;
                SptStatusText.Text = string.IsNullOrWhiteSpace(friendly)
                    ? "Detected SPT (version unknown)"
                    : $"Detected SPT {friendly}";
            }
            catch
            {
                SptStatusText.Text = "Detected SPT (version unknown)";
            }
        else
            SptStatusText.Text = "Could not find SPT.Server.exe in this folder.";
    }

    private async void OnClearCache(object? sender, RoutedEventArgs e)
    {
        try
        {
            try
            {
                App.Cache?.Close();
            }
            catch (Exception ex)
            {
                Notifications.Current.ShowError("Cache Error", "Failed to clear cache database connection.");
                Logger.Error("[SettingsPage] Failed to close cache: " + ex);
            }

            var dbPath = Paths.CacheDbPath;
            var targets = new[] { dbPath, dbPath + "-shm", dbPath + "-wal" };
            foreach (var p in targets)
                try
                {
                    if (File.Exists(p)) File.Delete(p);
                }
                catch (Exception ex)
                {
                    Notifications.Current.ShowError("Cache Error", "Failed to delete cache files.");
                    Logger.Error("[SettingsPage] Failed to delete cache file: " + ex);
                }

            try
            {
                App.Cache?.Init();
            }
            catch (Exception ex)
            {
                Notifications.Current.ShowError("Cache Error", "Failed to reinitialize cache database.");
                Logger.Error("[SettingsPage] Failed to initialize cache: " + ex);
            }

            Notifications.Current.ShowSuccess("Cache Cleared", "All cached data has been removed successfully.");
            Logger.Info("[SettingsPage] Cache cleared successfully.");
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Cache Error", "Unexpected error while clearing cache.");
            Logger.Error("[SettingsPage] Unexpected cache clear error: " + ex);
        }

        var main = (MainWindow?)TopLevel.GetTopLevel(this);
        if (main is not null)
        {
            var tabs = main.FindDescendantOfType<TabControl>();
            if (tabs is not null) tabs.SelectedIndex = 0;
            var browse = main.FindDescendantOfType<BrowseModsPage>();
            if (browse is not null)
                await browse.TriggerRefresh();
        }
    }

    private void OnClearTemp(object? sender, RoutedEventArgs e)
    {
        var baseDir = string.IsNullOrWhiteSpace(App.Config.Paths.DataFolder) ? Paths.DataDir : App.Config.Paths.DataFolder;
        var downloads = Path.Combine(baseDir, "downloads");
        var thumbs = Path.Combine(baseDir, "thumbs");
        var stage = Path.Combine(baseDir, "stage");

        TryDeleteDir(downloads);
        TryDeleteDir(thumbs);
        TryDeleteDir(stage);

        Notifications.Current.ShowSuccess("Temp Files Cleared", "All temporary files have been removed.");
        Logger.Info("[SettingsPage] Temporary files cleared successfully.");
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                }
                catch (Exception ex)
                {
                    Logger.Error("[SettingsPage] Failed to reset file attributes: " + ex);
                }

            Directory.Delete(dir, true);
            Logger.Info("[SettingsPage] Deleted directory: " + dir);
        }
        catch (Exception ex)
        {
            Logger.Error("[SettingsPage] Failed to delete directory: " + dir + " | " + ex);
        }
    }
}