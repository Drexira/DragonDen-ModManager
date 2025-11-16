using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DragonDen.ModManager.Services;

namespace DragonDen.ModManager.Views;

public partial class FirstRunDialog : Window
{
    public enum Result
    {
        None,
        Select,
        CloseApp
    }

    public FirstRunDialog()
    {
        InitializeComponent();
        SelectBtn.Click += OnSelectAsync;
        CloseBtn.Click += OnClose;
    }

    private async void OnSelectAsync(object? s, RoutedEventArgs e)
    {
        var storage = StorageProvider;
        if (storage is null)
        {
            Close(Result.CloseApp);
            return;
        }

        var pick = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false
        });
        if (pick.Count == 0) return;

        var chosen = pick[0].Path.LocalPath;
        if (!Directory.Exists(chosen))
        {
            Notifications.Current.ShowError("Folder Missing", "The selected folder doesn't exist.");
            Logger.Warn("[FirstRunDialog] Folder does not exist: " + chosen);
            return;
        }

        var eftExe = Path.Combine(chosen, "EscapeFromTarkov.exe");
        var bepin = Path.Combine(chosen, "BepInEx");
        if (!File.Exists(eftExe) || !Directory.Exists(bepin))
        {
            VersionText.Text = "Invalid folder. EscapeFromTarkov.exe or BepInEx folder not found.";
            Notifications.Current.ShowError(
                "Invalid Folder",
                "EscapeFromTarkov.exe and BepInEx folder must be in the selected folder."
            );
            Logger.Warn("[FirstRunDialog] Invalid folder selected (missing EFT exe or BepInEx): " + chosen);
            return;
        }

        var exe = Path.Combine(chosen, "SPT.Server.exe");
        if (!File.Exists(exe))
            exe = Path.Combine(chosen, "SPT", "SPT.Server.exe");

        if (!File.Exists(exe))
        {
            VersionText.Text = "Invalid folder. SPT.Server.exe not found.";
            Notifications.Current.ShowError("Invalid Folder", "SPT.Server.exe not found. Please select the SPT root folder.");
            Logger.Warn("[FirstRunDialog] Invalid folder selected: " + chosen);
            return;
        }

        string threePart, majorTwo;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exe);
            var raw = (vi.FileVersion ?? "").Trim();
            var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) throw new InvalidOperationException("Unexpected file version.");
            threePart = $"{Safe(parts, 0)}.{Safe(parts, 1)}.{Safe(parts, 2)}";
            majorTwo = $"{Safe(parts, 0)}.{Safe(parts, 1)}";
        }
        catch (Exception ex)
        {
            VersionText.Text = "Could not read SPT version from executable.";
            Notifications.Current.ShowError("Read Failed", "Could not read SPT version. Try selecting another folder.");
            Logger.Error("[FirstRunDialog] Could not read version info: " + ex);
            return;
        }

        App.Config.Paths.SptRoot = chosen;
        try
        {
            var major = int.TryParse(majorTwo.Split('.')[0], out var mj) ? mj : 0;
            App.Config.Paths.ServerModsRelative = major >= 4 ? "SPT/user/mods" : "user/mods";
        }
        catch (Exception ex)
        {
            Logger.Error("[FirstRunDialog] Failed to set ServerModsRelative: " + ex);
        }

        App.SaveConfig();
        App.RaiseConfigChanged();

        VersionText.Text = $"Detected SPT {threePart} — filter set to {majorTwo}";
        HintText.Text = $"{exe}";

        Notifications.Current.ShowSuccess("SPT Folder Set", $"SPT {threePart} detected and saved.");
        Logger.Info("[FirstRunDialog] SPT folder set successfully: " + chosen);
        Close(Result.Select);

        static string Safe(string[] a, int i)
        {
            return i >= 0 && i < a.Length && int.TryParse(a[i], out var n) ? n.ToString() : "0";
        }
    }

    private void OnClose(object? s, RoutedEventArgs e)
    {
        Close(Result.CloseApp);
    }
}