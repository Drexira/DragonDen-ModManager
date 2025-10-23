using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

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
            App.Toasts?.Show("Folder doesn't exist.");
            return;
        }

        var exe = Path.Combine(chosen, "SPT.Server.exe");
        if (!File.Exists(exe))
            exe = Path.Combine(chosen, "SPT", "SPT.Server.exe");

        if (!File.Exists(exe))
        {
            VersionText.Text = "Invalid folder. SPT.Server.exe not found.";
            App.Toasts?.Show("Invalid SPT folder. Please pick the game root (contains SPT.Server.exe).");
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
        catch
        {
            VersionText.Text = "Could not read SPT version from executable.";
            App.Toasts?.Show("Could not read version. Try another folder.");
            return;
        }

        App.Config.Paths.SptRoot = chosen;
        try
        {
            var major = int.TryParse(majorTwo.Split('.')[0], out var mj) ? mj : 0;
            App.Config.Paths.ServerModsRelative = major >= 4 ? "SPT/user/mods" : "user/mods";
        }
        catch
        {
            // good girl action
        }

        App.SaveConfig();
        App.RaiseConfigChanged();

        VersionText.Text = $"Detected SPT {threePart} — filter set to {majorTwo}";
        HintText.Text = $"{exe}";

        App.Toasts?.Show("SPT folder set.");
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