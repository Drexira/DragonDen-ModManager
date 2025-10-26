using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DragonDen.ModManager.Services;

namespace DragonDen.ModManager.ViewModels;

public sealed class SearchResultRow : INotifyPropertyChanged
{
    private bool _isInstalled;
    private bool _isQueued;

    public int ModId { get; set; }
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Teaser { get; set; } = "";
    public string Category { get; set; } = "";
    public string CategoryColorClass { get; set; } = "";
    public List<string> OwnerNames { get; set; } = new();
    public string Thumbnail { get; set; } = "";
    public string Slug { get; set; } = "";
    public string ModPageUrl { get; set; } = "";
    public long Downloads { get; set; }
    public List<ForgeClient.ModVersion> Versions { get; set; } = new();
    public List<VersionDisplay> VersionsDisplay { get; set; } = new();
    public string LatestVersionText { get; set; } = "";
    public string SptConstraintText { get; set; } = "";
    public List<SourceButton> SourceButtons { get; set; } = new();
    public string? ThumbnailOrPlaceholder => string.IsNullOrWhiteSpace(Thumbnail) ? null : Thumbnail;
    public bool HasAuthors => OwnerNames is { Count: > 0 };
    public bool IsLatestVersion { get; set; } = false;
    public bool HasSources => SourceButtons is { Count: > 0 };
    public string DownloadsText => Downloads.ToString("N0", CultureInfo.InvariantCulture);

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value) return;
            _isInstalled = value;
            OnPropsChanged(nameof(IsInstalled), nameof(ShowUninstall), nameof(ShowInstall), nameof(ShowVersionPicker));
        }
    }

    public bool IsQueued
    {
        get => _isQueued;
        set
        {
            if (_isQueued == value) return;
            _isQueued = value;
            OnPropsChanged(nameof(IsQueued), nameof(IsInstalled), nameof(ShowQueued), nameof(ShowInstall));
        }
    }

    public bool ShowUninstall => IsInstalled;
    public bool ShowInstall => !IsInstalled && !IsQueued;
    public bool ShowVersionPicker => !IsInstalled;
    public bool ShowQueued => IsQueued && !IsInstalled;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void OnPropsChanged(params string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
    }

    public sealed class VersionDisplay
    {
        public ForgeClient.ModVersion Model { get; set; } = new();
        public string Label { get; set; } = "";
        public string SptNormalized { get; set; } = "";
    }

    public sealed class SourceButton
    {
        public string Url { get; set; } = "";
        public string Label { get; set; } = "Source";
    }
}