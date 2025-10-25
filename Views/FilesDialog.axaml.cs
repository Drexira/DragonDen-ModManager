using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DragonDen.ModManager.Models;

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
                catch
                {
                    // good girl action
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
            var path = (App.Config.Paths.SptRoot + mf.GetFileLocation(mf.Target, mf.Path) ?? string.Empty).Replace('/', System.IO.Path.DirectorySeparatorChar);
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
            catch
            {
                Console.WriteLine("Failed to open file: " + path);
                // good girl action
            }
        }
    }
}