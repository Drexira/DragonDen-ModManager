using System;
using System.IO;

namespace DragonDen.ModManager.Services;

public static class Paths
{
    public static readonly string AppBase = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    public static readonly string CacheDir = Path.Combine(LocalAppData, "DragonDen.ModManager");
    public static readonly string ModsDir = Path.Combine(LocalAppData, "DragonDen.ModManager", "ModsDB");
    public static readonly string ToolsDir = Path.Combine(AppBase, "tools");
    public static readonly string SevenZipPath = Path.Combine(ToolsDir, "7zip", "win", "7za.exe");
    public static readonly string DataDir = Path.Combine(AppBase, "data");
    public static readonly string DbPath = Path.Combine(ModsDir, "mods.db");
    public static readonly string AppSettingsPath = Path.Combine(LocalAppData, "DragonDen.ModManager", "appsettings.json");
    public static readonly string CacheDbPath = Path.Combine(CacheDir, "cache.db");
    public static string ModsRegistryPath => Path.Combine(CacheDir, "mods_registry.json");
    public static string ModsDbPath => ModsDbRegistry.GetDbPathFor(App.Config?.Paths?.SptRoot ?? "");

    public static string LocalAppData
    {
        get
        {
            var p = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(p)) p = Path.Combine(AppBase, "LocalAppData");
            return p;
        }
    }
}