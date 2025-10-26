using System;
using System.Collections;
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

public sealed class BoolNotConverter : IValueConverter
{
    public static readonly BoolNotConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : value;
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

public sealed class StringEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var a = value?.ToString()?.Trim() ?? "";
        var b = parameter?.ToString()?.Trim() ?? "";
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StringNotEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var a = value?.ToString()?.Trim() ?? "";
        var b = parameter?.ToString()?.Trim() ?? "";
        return !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CollectionHasItemsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is IEnumerable e && e.GetEnumerator().MoveNext();
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ShowCustomInstallConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var installed = values.Count > 0 ? values[0]?.ToString()?.Trim() ?? "" : "";
        var versions  = values.Count > 1 ? values[1] as IEnumerable : null;

        bool hasVersions = false;
        if (versions != null)
        {
            var en = versions.GetEnumerator();
            hasVersions = en.MoveNext();
        }

        bool isCustom = installed.Equals("Custom Install", StringComparison.OrdinalIgnoreCase)
                        || installed.Equals("0.0.0", StringComparison.OrdinalIgnoreCase)
                        || !hasVersions;

        var invert = string.Equals(parameter?.ToString(), "invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !isCustom : isCustom;
    }

    public object? ConvertBack(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}

public sealed class BoolAndConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var v in values)
        {
            if (v is bool b)
            {
                if (!b) return false;
            }
            else return false;
        }
        return true;
    }
    public object? ConvertBack(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => AvaloniaProperty.UnsetValue;
}