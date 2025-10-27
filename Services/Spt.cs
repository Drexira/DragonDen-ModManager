using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DragonDen.ModManager.Services;

public static class Spt
{
    public static string? Root => App.Config.Paths.SptRoot;
    public static string ClientModsPath => string.IsNullOrWhiteSpace(Root) ? "" : Path.Combine(Root!, Normalize(App.Config.Paths.ClientModsRelative));
    public static string ServerModsPath => string.IsNullOrWhiteSpace(Root) ? "" : Path.Combine(Root!, Normalize(App.Config.Paths.ServerModsRelative));

    private static string Normalize(string p)
    {
        return p.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    public static bool TryFindAnyServerExe(out string exePath)
    {
        exePath = "";
        try
        {
            var root = Root ?? "";
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return false;

            var candidates = new[]
            {
                Path.Combine(root, "SPT.Server.exe"),
                Path.Combine(root, "SPT", "SPT.Server.exe"),
                Path.Combine(root, "Aki.Server.exe"),
                Path.Combine(root, "Aki.Server", "Aki.Server.exe"),
                Path.Combine(root, "Server", "Server.exe"),
                Path.Combine(root, "SPT", "Server.exe")
            };

            foreach (var p in candidates)
                if (File.Exists(p))
                {
                    exePath = p;
                    return true;
                }

            var extra = new List<string>();
            try
            {
                extra.AddRange(Directory.EnumerateFiles(root, "*Server*.exe", SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Spt] Error enumerating files in root: {ex}");
            }

            var sptDir = Path.Combine(root, "SPT");
            if (Directory.Exists(sptDir))
                try
                {
                    extra.AddRange(Directory.EnumerateFiles(sptDir, "*Server*.exe", SearchOption.TopDirectoryOnly));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Spt] Error enumerating files in SPT dir: {ex}");
                }

            var hit = extra.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(hit))
            {
                exePath = hit;
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Spt] Error finding server exe: {ex}");
        }

        return false;
    }

    public static bool TryGetServerVersionThree(out string threePart, out string majorTwo)
    {
        threePart = "";
        majorTwo = "";

        try
        {
            if (!TryFindAnyServerExe(out var exe)) return false;

            var vi = FileVersionInfo.GetVersionInfo(exe);
            var raw = (vi.FileVersion ?? "").Trim();
            var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;

            var a = Safe(parts, 0);
            var b = Safe(parts, 1);
            var c = parts.Length >= 3 ? Safe(parts, 2) : "0";

            threePart = $"{a}.{b}.{c}";
            majorTwo = $"{a}.{b}";
            return true;
        }
        catch
        {
            return false;
        }

        static string Safe(string[] a, int i)
        {
            return i >= 0 && i < a.Length && int.TryParse(a[i], out var n) ? n.ToString() : "0";
        }
    }
}