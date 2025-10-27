using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DragonDen.ModManager.Services;

public sealed class Config
{
    public PathsSection Paths { get; set; } = new();
    public ForgeSection Forge { get; set; } = new();
    public UISection UI { get; set; } = new();

    [JsonIgnore]
    public static JsonSerializerOptions JsonOptions => AppJsonContext.Default.Options;

    public static Config CreateDefault()
    {
        return new Config
        {
            Paths = new PathsSection
            {
                SptRoot = "",
                DataFolder = "",
                ClientModsRelative = "BepInEx/plugins",
                ServerModsRelative = "SPT/user/mods"
            },
            Forge = new ForgeSection
            {
                BaseUrl = "https://forge.sp-tarkov.com",
                Token = ""
            },
            UI = new UISection
            {
                SearchSort = "recent",
                SearchPageSize = 18
            }
        };
    }

    public static Config Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);
                var cfg = JsonSerializer.Deserialize(json, AppJsonContext.Default.Config) ?? CreateDefault();
                cfg.ApplyDefaultsIfMissing();
                return cfg;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("[Config] Loading config failed: " + ex.Message);
        }

        var created = CreateDefault();
        EnsureDirectory(filePath);
        SaveTo(filePath, created);
        return created;
    }

    public static void Save(string filePath)
    {
        SaveTo(filePath, App.Config);
    }

    private static void SaveTo(string filePath, Config config)
    {
        try
        {
            EnsureDirectory(filePath);
            var json = JsonSerializer.Serialize(config, AppJsonContext.Default.Config);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Notifications.Current.ShowError("Save Failed", "Couldn't save config file.");
            Logger.Error("[Config] Save failed: " + ex);
        }
    }

    public void ApplyDefaultsIfMissing()
    {
        Paths ??= new PathsSection();
        Forge ??= new ForgeSection();
        UI ??= new UISection();

        if (string.IsNullOrWhiteSpace(Paths.ClientModsRelative))
            Paths.ClientModsRelative = "BepInEx/plugins";

        if (string.IsNullOrWhiteSpace(Paths.ServerModsRelative))
            Paths.ServerModsRelative = "SPT/user/mods";

        if (string.IsNullOrWhiteSpace(Forge.BaseUrl))
            Forge.BaseUrl = "https://forge.sp-tarkov.com";

        Forge.Token ??= "";

        if (string.IsNullOrWhiteSpace(UI.SearchSort))
            UI.SearchSort = "recent";

        if (UI.SearchPageSize <= 0)
            UI.SearchPageSize = 12;
    }

    public static string GetDefaultConfigPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, "DragonDen.ModManager");
        return Path.Combine(dir, "config.json");
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}

public sealed class PathsSection
{
    public string? SptRoot { get; set; }
    public string? DataFolder { get; set; }
    public string ClientModsRelative { get; set; } = "BepInEx/plugins";
    public string ServerModsRelative { get; set; } = "SPT/user/mods";
}

public sealed class ForgeSection
{
    public string BaseUrl { get; set; } = "https://forge.sp-tarkov.com";
    public string Token { get; set; } = "";
}

public sealed class UISection
{
    public string SearchSort { get; set; } = "recent";
    public int SearchPageSize { get; set; } = 12;
}