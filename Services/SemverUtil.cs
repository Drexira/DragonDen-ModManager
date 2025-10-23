using System;
using System.Text.RegularExpressions;
using Semver;

namespace DragonDen.ModManager.Services;

public static class SemverUtil
{
    public static string NormalizeToThreeParts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var s = raw.Trim();
        s = s.TrimStart('v', 'V', '~', '^');
        s = Regex.Replace(s, @"(?i)\bx\b", "0");
        var cut = s.Split(new[] { '-', '+' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];

        var parts = cut.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int a = Part(parts, 0), b = Part(parts, 1), c = Part(parts, 2);

        try
        {
            _ = new SemVersion(a, b, c);
            return $"{a}.{b}.{c}";
        }
        catch
        {
            return "";
        }

        static int Part(string[] p, int i)
        {
            return i < p.Length && int.TryParse(p[i], out var n) && n >= 0 ? n : 0;
        }
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