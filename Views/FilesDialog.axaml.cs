using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    private void OnFilesListDoubleTapped(object? s, TappedEventArgs e)
    {
        if (FilesList?.SelectedItem is ModFile mf)
        {
            var path = (App.Config.Paths.SptRoot + mf.GetFileLocation(mf.Target, mf.Path) ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var process = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                Process.Start(process);
            }
            catch (Exception ex)
            {
                Notifications.Current.ShowError("Open Failed", $"Couldn't open the file '{Path.GetFileName(path)}'.");
                Console.WriteLine($"[FilesDialog] Failed to open file: {path} ({ex.Message})");
            }
        }
    }
}