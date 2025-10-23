using System;

namespace DragonDen.ModManager.Models;

public sealed class ModFile
{
    public string Path { get; set; } = "";

    public string Target { get; set; } = "client";

    public string TargetLabel => string.Equals(Target, "server", StringComparison.OrdinalIgnoreCase) ? "Server" : "Client";

    public string TargetColor => string.Equals(Target, "server", StringComparison.OrdinalIgnoreCase) ? "#25422E" : "#28354B";

    public string DisplayPath
    {
        get
        {
            var p = (Path ?? "").Trim().Replace('\\', '/');

            if (string.Equals(Target, "server", StringComparison.OrdinalIgnoreCase)) return $"SPT/user/mods/{p}";

            if (p.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))
                return p;

            return $"BepInEx/plugins/{p}";
        }
    }

    public string GetFileLocation(string target, string file)
    {
        if (string.Equals(target, "client", StringComparison.OrdinalIgnoreCase))
            return App.Config.Paths.ClientModsRelative + "/" + file;
        else 
            return App.Config.Paths.ServerModsRelative + "/" + file;
    }
}