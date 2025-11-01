using System;
using System.Text.RegularExpressions;
using Semver;

namespace DragonDen.ModManager.Services;

public static class SemverUtil
{
    public static string NormalizeToThreeParts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var m = Regex.Match(raw, @"\d+(?:[.\*xX]\d+|[.\*xX]){0,2}");
        if (!m.Success) return "";

        var parts = Regex.Replace(m.Value, @"[xX\*]", "0").Split('.');
        int a = Part(parts, 0), b = Part(parts, 1), c = Part(parts, 2);

        try { _ = new SemVersion(a, b, c); return $"{a}.{b}.{c}"; }
        catch { return ""; }

        static int Part(string[] p, int i) =>
            i < p.Length && int.TryParse(p[i], out var n) && n >= 0 ? n : 0;
    }

    public static bool TryParseStrict(string? raw, out SemVersion v)
    {
        v = default;
        var norm = NormalizeToThreeParts(raw);
        if (string.IsNullOrWhiteSpace(norm)) return false;
        try
        {
            v = SemVersion.Parse(norm, SemVersionStyles.Strict);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string MajorMinor(SemVersion v)
    {
        return $"{v.Major}.{v.Minor}";
    }

    public static int CompareTagsDesc(string a, string b)
    {
        var okA = TryParseStrict(a, out var va);
        var okB = TryParseStrict(b, out var vb);
        if (okA && okB) return vb.CompareSortOrderTo(va);
        if (okA) return -1;
        if (okB) return 1;
        return string.Compare(b ?? "", a ?? "", StringComparison.OrdinalIgnoreCase);
    }
}