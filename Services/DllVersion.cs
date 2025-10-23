using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DragonDen.ModManager.Services;

public static class DllVersion
{
    private static readonly Regex digits = new(@"\b(\d+)(\.\d+){0,3}\b", RegexOptions.Compiled);

    public static string DetectFromFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return "";
        var dlls = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories)
                .Where(p => Path.GetDirectoryName(p) != folder))
            .Take(64).ToList();

        foreach (var dll in dlls)
        {
            var ver = FromDll(dll);
            if (!string.IsNullOrWhiteSpace(ver)) return ver;
        }

        return "";
    }

    public static string DetectFromFile(string path)
    {
        return FromDll(path);
    }

    public static string FromDll(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var raw = (info.ProductVersion ?? info.FileVersion ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var m = digits.Match(raw);
            if (!m.Success) return "";
            var norm = SemverUtil.NormalizeToThreeParts(m.Value);
            return norm;
        }
        catch
        {
            return "";
        }
    }
}