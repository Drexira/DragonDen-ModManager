using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace DragonDen.ModManager.Services;

public sealed class NullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class BoolNegateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : value is null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class StringHasTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrWhiteSpace(s);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class VersionChangeEnabledConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var installed = values.Count > 0 ? values[0]?.ToString()?.Trim() ?? "" : "";
        var selectedObj = values.Count > 1 ? values[1] : null;

        var selected = selectedObj switch
        {
            ForgeClient.ModVersion mv
                => mv.Version?.Trim() ?? "",
            string s => s.Trim(),
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(selected))
            return false;

        var okI = SemverUtil.TryParseStrict(installed, out var vi);
        var okS = SemverUtil.TryParseStrict(selected, out var vs);
        if (okI && okS) return vs != vi;

        return !string.Equals(installed, selected, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}