using System;
using System.Collections.Generic;
using DragonDen.ModManager.Services;

namespace DragonDen.ModManager.ViewModels;

public sealed class InstalledModRow
{
    public List<string> ModIds { get; set; } = new();
    public string Name { get; set; } = "";
    public string Guid { get; set; } = "";
    public string InstalledVersion { get; set; } = "Custom Install";
    public int FileCount { get; set; }
    public bool HasEditableConfigs { get; set; }
    public string Thumbnail { get; set; } = "";
    public string DetailUrl { get; set; } = "";
    public bool HasPage { get; set; }
    public bool IsCustom { get; set; }
    public bool IsOutdated { get; set; }
    public bool CanUpdate { get; set; }
    public string LatestVersionText { get; set; } = "";
    public ForgeClient.ModVersion? Latest { get; set; }
    public string Category { get; set; } = "";
    public string CategoryColorClass { get; set; } = "";
    public DateTimeOffset? InstalledAt { get; set; }
    public string InstalledAtText { get; set; } = "";
    public string LatestPublishedText { get; set; } = "";
    public List<string> Authors { get; set; } = new();
    public List<SourceButton> SourceButtons { get; set; } = new();
    public bool HasAuthors => Authors is { Count: > 0 };
    public bool HasSources => SourceButtons is { Count: > 0 };
    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);
    public string? ThumbnailOrPlaceholder => string.IsNullOrWhiteSpace(Thumbnail) ? null : Thumbnail;
    public List<ForgeClient.ModVersion> Versions { get; set; } = new();
    public bool IsDisabled { get; set; }
    public string DisabledAtText { get; set; } = "";
    public bool ShowDisable => !IsDisabled;
    public bool ShowEnable  =>  IsDisabled;

    public sealed class SourceButton
    {
        public string Url { get; set; } = "";
        public string Label { get; set; } = "Source";
    }
}