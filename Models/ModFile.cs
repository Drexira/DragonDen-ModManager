using System;
using DragonDen.ModManager.Services;

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
    
    public string FullPath
    {
        get
        {
            var unix = (Path ?? "").Replace('\\', '/');

            if (string.Equals(Target, "server", StringComparison.OrdinalIgnoreCase))
            {
                var rel =
                    TrimPrefix(unix, "SPT/user/mods/") ??
                    TrimPrefix(unix, "user/mods/") ??
                    unix;

                return System.IO.Path.Combine(Spt.ServerModsPath, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            }
            else
            {
                var rel =
                    TrimPrefix(unix, "BepInEx/plugins/") ??
                    TrimPrefix(unix, "plugins/") ??
                    unix;

                return System.IO.Path.Combine(Spt.ClientModsPath, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            }
        }
    }

    public string GetFileLocation(string target, string file)
    {
        if (string.Equals(target, "client", StringComparison.OrdinalIgnoreCase))
            return App.Config.Paths.ClientModsRelative + "/" + file;
        else 
            return App.Config.Paths.ServerModsRelative + "/" + file;
    }
    
    private static string? TrimPrefix(string text, string prefix)
    {
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? text[prefix.Length..] : null;
    }
}