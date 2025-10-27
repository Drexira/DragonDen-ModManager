using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DragonDen.ModManager.Models;
using DragonDen.ModManager.Services;

namespace DragonDen.ModManager.Views;

public partial class FilesDialog : Window
{
    public FilesDialog()
    {
        InitializeComponent();

        PointerPressed += (_, e) =>
        {
            if (e.Source is Button) return;
            var pos = e.GetPosition(this);
            if (pos.Y <= 36 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                try
                {
                    BeginMoveDrag(e);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FilesDialog] BeginMoveDrag failed: {ex.Message}");
                }
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        }, RoutingStrategies.Tunnel);
    }

    public FilesDialog(string modName, IEnumerable<(string path, string target)> files) : this()
    {
        SetData(modName, files);
    }

    public void SetData(string modName, IEnumerable<(string path, string target)> files)
    {
        TitleText.Text = $"Files • {modName}";
        var items = files
            .Select(t => new ModFile
            {
                Path = t.path?.Replace('\\', '/') ?? "",
                Target = (t.target ?? "client").ToLowerInvariant()
            })
            .OrderBy(t => t.Target, StringComparer.Ordinal)
            .ThenBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        FilesList.ItemsSource = items;
        FilesCountText.Text = items.Count.ToString();
    }

    private void OnCloseClicked(object? s, RoutedEventArgs e)
    {
        Close();
    }

    private static string NormalizeWeirdPath(string? sptRoot, string raw)
    {
        var root = (sptRoot ?? "").Replace('/', '\\').TrimEnd('\\');
        var p = (raw ?? "").Replace('/', '\\').Trim();

        if (string.IsNullOrWhiteSpace(p)) return "";

        var m = Regex.Match(p, @"[A-Za-z]:\\");
        if (m.Success)
        {
            var idx = m.Index;
            var sliced = p.Substring(idx);
            try { return Path.GetFullPath(sliced); } catch { return sliced; }
        }

        if (Path.IsPathRooted(p))
        {
            try { return Path.GetFullPath(p); } catch { return p; }
        }

        if (!string.IsNullOrWhiteSpace(root))
        {
            var withSep = root + "\\";
            if (p.StartsWith(withSep, StringComparison.OrdinalIgnoreCase))
            {
                try { return Path.GetFullPath(p); } catch { return p; }
            }

            var pos = p.IndexOf(withSep, StringComparison.OrdinalIgnoreCase);
            if (pos > 0)
            {
                var sliced = p.Substring(pos);
                try { return Path.GetFullPath(sliced); } catch { return sliced; }
            }

            var combined = Path.Combine(root, p.TrimStart('\\'));
            try { return Path.GetFullPath(combined); } catch { return combined; }
        }

        try { return Path.GetFullPath(p); } catch { return p; }
    }

    private void OnFilesListDoubleTapped(object? s, TappedEventArgs e)
    {
        if (FilesList?.SelectedItem is not ModFile mf) return;

        try
        {
            var loc = mf.GetFileLocation(mf.Target, mf.Path) ?? "";
            var path = NormalizeWeirdPath(App.Config.Paths.SptRoot, loc);

            if (string.IsNullOrWhiteSpace(path))
            {
                Notifications.Current.ShowWarning("Open Failed", "Invalid file path.");
                return;
            }

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Notifications.Current.ShowWarning("Open Failed", "File not found on disk.");
                Console.WriteLine($"[FilesDialog] Path does not exist: {path}");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                };
                Process.Start(psi);
            }
            catch (Exception first)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var psiReveal = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = "/select,\"" + path + "\"",
                            UseShellExecute = true
                        };
                        Process.Start(psiReveal);
                    }
                    else
                    {
                        var psiFolder = new ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true
                        };
                        Process.Start(psiFolder);
                    }
                }
                catch (Exception second)
                {
                    Notifications.Current.ShowError("Open Failed", $"Couldn't open the file '{Path.GetFileName(path)}'.");
                    Console.WriteLine($"[FilesDialog] Failed to open file: {path} ({first.Message}; fallback: {second.Message})");
                }
            }
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Open Failed", "Unexpected error while opening file.");
            Console.WriteLine($"[FilesDialog] Unexpected error: {ex}");
        }
    }
}