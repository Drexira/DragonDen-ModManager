using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DragonDen.ModManager.Services;

public sealed class VersionLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ForgeClient.ModVersion v) return "";
        var ver = string.IsNullOrWhiteSpace(v.Version) ? "n/a" : v.Version!.Trim();
        var spt = SemverUtil.NormalizeToThreeParts(v.SptVersionConstraint);
        return string.IsNullOrWhiteSpace(spt) ? $"v{ver}" : $"v{ver} - SPT {spt}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}