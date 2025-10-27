using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DragonDen.ModManager.Services;

public static class DllGuidExtractor
{
    public static string? TryExtractModGuidFromDll(string dllPath)
    {
        try
        {
            var bep = DllIntrospector.TryGetBepInExInfo(dllPath);
            if (bep is not null && !string.IsNullOrWhiteSpace(bep.Guid))
                return bep.Guid;
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllGuidExtractor] Failed to extract mod guid from {dllPath}: {ex.Message}");
        }

        return FallbackScanBinary(dllPath);
    }

    public static string? TryExtractServerModGuidFromDll(string dllPath)
    {
        try
        {
            var g = DllIntrospector.TryGetServerModGuid(dllPath);
            if (!string.IsNullOrWhiteSpace(g)) return g;
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllGuidExtractor] Failed to extract server mod guid from {dllPath}: {ex.Message}");
        }

        return FallbackScanBinary(dllPath);
    }

    public static string? TryExtractModGuidFromFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;

        try
        {
            foreach (var dll in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories))
            {
                var g1 = TryExtractModGuidFromDll(dll);
                if (!string.IsNullOrWhiteSpace(g1)) return g1;

                var g2 = TryExtractServerModGuidFromDll(dll);
                if (!string.IsNullOrWhiteSpace(g2)) return g2;
            }

            var serverFromCs = TryExtractServerGuidFromCs(folder);
            if (!string.IsNullOrWhiteSpace(serverFromCs)) return serverFromCs;

            var (clientGuid, nameCandidates) = TryExtractClientGuidOrNamesFromCs(folder);
            if (!string.IsNullOrWhiteSpace(clientGuid)) return clientGuid;

            var dllStems = Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories)
                .Select(p => Path.GetFileNameWithoutExtension(p) ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var folderName = new[] { new DirectoryInfo(folder).Name };

            var allNames = nameCandidates
                .Concat(dllStems)
                .Concat(folderName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var fromForge = ResolveGuidFromForgeByNames(allNames);
            if (!string.IsNullOrWhiteSpace(fromForge)) return fromForge;

            foreach (var dll in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories))
            {
                var g = FallbackScanBinary(dll);
                if (!string.IsNullOrWhiteSpace(g)) return g;
            }

            foreach (var cs in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                var g = FallbackScanTextFile(cs);
                if (!string.IsNullOrWhiteSpace(g)) return g;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllGuidExtractor] Failed to extract mod guid from {folder}: {ex.Message}");
        }

        return null;
    }

    public static string? TryExtractServerModGuid(string folder)
    {
        try
        {
            foreach (var dll in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories))
            {
                var g = TryExtractServerModGuidFromDll(dll);
                if (!string.IsNullOrWhiteSpace(g)) return g;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllGuidExtractor] Failed to extract server mod guid from {folder}: {ex.Message}");
        }

        return null;
    }

    private static string? TryExtractServerGuidFromCs(string folder)
    {
        try
        {
            foreach (var cs in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string text;
                try
                {
                    text = ReadSmallText(cs);
                }
                catch
                {
                    continue;
                }

                if (text.IndexOf("AbstractModMetadata", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var m1 = Regex.Match(
                    text,
                    @"public\s+override\s+string\s+ModGuid\s*=>\s*""([^""]+)""",
                    RegexOptions.IgnoreCase);

                if (m1.Success) return m1.Groups[1].Value;

                var m2 = Regex.Match(
                    text,
                    @"public\s+override\s+string\s+ModGuid\s*{[^}]*?get\s*{[^}]*?return\s*""([^""]+)""\s*;?[^}]*}\s*}",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);

                if (m2.Success) return m2.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllGuidExtractor] Failed to extract server mod guid from {folder}: {ex.Message}");
        }

        return null;
    }

    private static (string? guid, List<string> names) TryExtractClientGuidOrNamesFromCs(string folder)
    {
        var resultNames = new List<string>();
        try
        {
            var constGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var constName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var cs in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string text;
                try
                {
                    text = ReadSmallText(cs);
                }
                catch
                {
                    continue;
                }

                foreach (Match mc in Regex.Matches(
                             text,
                             @"\b(public|internal)\s+const\s+string\s+(?<name>(PLUGIN_GUID|GUID|MOD_GUID|MODID|ID))\s*=\s*""(?<val>[^""]+)""",
                             RegexOptions.IgnoreCase))
                {
                    var n = mc.Groups["name"].Value;
                    var v = mc.Groups["val"].Value;
                    if (!constGuid.ContainsKey(n)) constGuid[n] = v;
                }

                foreach (Match mc in Regex.Matches(
                             text,
                             @"\b(public|internal)\s+const\s+string\s+(?<name>(PLUGIN_NAME|NAME|MOD_NAME))\s*=\s*""(?<val>[^""]+)""",
                             RegexOptions.IgnoreCase))
                {
                    var n = mc.Groups["name"].Value;
                    var v = mc.Groups["val"].Value;
                    if (!constName.ContainsKey(n)) constName[n] = v;
                    if (!string.IsNullOrWhiteSpace(v) && !resultNames.Contains(v, StringComparer.OrdinalIgnoreCase))
                        resultNames.Add(v);
                }
            }

            foreach (var cs in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string text;
                try
                {
                    text = ReadSmallText(cs);
                }
                catch
                {
                    continue;
                }

                var mAttrLit = Regex.Match(
                    text,
                    @"\[BepInPlugin\s*\(\s*""(?<id>[^""]+)""\s*,\s*""(?<name>[^""]*)""\s*,\s*""[^""]*""\s*\)\]",
                    RegexOptions.IgnoreCase);
                if (mAttrLit.Success)
                {
                    var id = mAttrLit.Groups["id"].Value;
                    var nm = mAttrLit.Groups["name"].Value;
                    if (!string.IsNullOrWhiteSpace(id)) return (id, resultNames);
                    if (!string.IsNullOrWhiteSpace(nm) && !resultNames.Contains(nm, StringComparer.OrdinalIgnoreCase))
                        resultNames.Add(nm);
                }

                var mAttrSym = Regex.Match(
                    text,
                    @"\[BepInPlugin\s*\(\s*(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*,",
                    RegexOptions.IgnoreCase);
                if (mAttrSym.Success)
                {
                    var idSym = mAttrSym.Groups["id"].Value;
                    var nmSym = mAttrSym.Groups["name"].Value;

                    if (constGuid.TryGetValue(idSym, out var idVal) && !string.IsNullOrWhiteSpace(idVal))
                        return (idVal, resultNames);

                    if (constName.TryGetValue(nmSym, out var nmVal) && !string.IsNullOrWhiteSpace(nmVal)
                                                                    && !resultNames.Contains(nmVal, StringComparer.OrdinalIgnoreCase))
                        resultNames.Add(nmVal);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllGuidExtractor] Failed to extract server mod guid from {folder}: {ex.Message}");
        }

        return (null, resultNames);
    }

    private static string? ResolveGuidFromForgeByNames(IEnumerable<string> names)
    {
        try
        {
            var all = App.Cache.GetAllModsBasic();
            if (all is null || all.Count == 0) return null;

            string Norm(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                var filtered = s.Where(char.IsLetterOrDigit).ToArray();
                return new string(filtered).ToLowerInvariant();
            }

            foreach (var raw in names.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var candidate = raw.Trim();
                var candNorm = Norm(candidate);

                var exact = all.FirstOrDefault(x => string.Equals(x.Name, candidate, StringComparison.OrdinalIgnoreCase));
                if (exact is not null && !string.IsNullOrWhiteSpace(exact.Guid))
                    return exact.Guid;

                var byNorm = all.FirstOrDefault(x => Norm(x.Name) == candNorm);
                if (byNorm is not null && !string.IsNullOrWhiteSpace(byNorm.Guid))
                    return byNorm.Guid;

                var contains = all.FirstOrDefault(x =>
                {
                    var nx = Norm(x.Name);
                    return nx.Contains(candNorm, StringComparison.Ordinal) || candNorm.Contains(nx, StringComparison.Ordinal);
                });
                if (contains is not null && !string.IsNullOrWhiteSpace(contains.Guid))
                    return contains.Guid;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllGuidExtractor] Failed ");
        }

        return null;
    }

    private static string? FallbackScanBinary(string dllPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(dllPath);
            var text = Encoding.UTF8.GetString(bytes);

            var m = Regex.Match(
                text,
                @"(?<q>[""'])(?<id>([a-zA-Z0-9_\-]+\.){2,}[a-zA-Z0-9_\-]+)\k<q>",
                RegexOptions.CultureInvariant);

            if (m.Success) return m.Groups["id"].Value;
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllGuidExtractor] Fallback scan failed for {dllPath}: {ex.Message}");
        }

        return null;
    }

    private static string? FallbackScanTextFile(string path)
    {
        try
        {
            var text = ReadSmallText(path);
            var m = Regex.Match(
                text,
                @"(?<q>[""'])(?<id>([a-zA-Z0-9_\-]+\.){2,}[a-zA-Z0-9_\-]+)\k<q>",
                RegexOptions.CultureInvariant);
            if (m.Success) return m.Groups["id"].Value;
        }
        catch (Exception ex)
        {
            Logger.Error($"[DllGuidExtractor] Fallback scan failed for {path}: {ex.Message}");
        }

        return null;
    }

    private static string ReadSmallText(string path, int maxBytes = 1_500_000)
    {
        using var fs = File.OpenRead(path);
        var cap = (int)Math.Min(fs.Length, maxBytes);
        using var ms = new MemoryStream(cap);
        fs.CopyTo(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}