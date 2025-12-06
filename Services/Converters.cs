using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

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

public sealed class SptVersionBadgeConverter : IValueConverter
{
    public IBrush GreenBrush { get; set; } = new SolidColorBrush(Color.Parse("#1faa33"));
    public IBrush RedBrush { get; set; } = new SolidColorBrush(Color.Parse("#fc2626"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text) || text == "-")
            return RedBrush;

        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return RedBrush;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major))
            return RedBrush;

        return major >= 4 ? GreenBrush : RedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class SptInstallTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var constraintRaw = value as string ?? "";
        var constraint = constraintRaw.Trim();

        var detectedAb = App.GetDetectedSptAB();
        if (string.IsNullOrWhiteSpace(detectedAb))
            return "Your current SPT install could not be detected.";

        var abParts = detectedAb.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (abParts.Length == 0 || !int.TryParse(abParts[0], out var currentMajor))
            return "Your current SPT version could not be parsed.";

        if (currentMajor < 4)
            return "Your current SPT version does not support installing mods here.";

        if (string.IsNullOrWhiteSpace(constraint))
            return "This version is not marked as compatible with SPT 4.x.";

        var modParts = constraint.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (modParts.Length == 0 || !int.TryParse(modParts[0], out var modMajor))
            return "This mod does not have a valid SPT target version.";

        if (modMajor == 4)
            return null;

        return "This mod does not target your current SPT version.";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class SptInstallEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var constraintRaw = value as string ?? "";
        var constraint = constraintRaw.Trim();

        var detectedAb = App.GetDetectedSptAB();
        if (string.IsNullOrWhiteSpace(detectedAb)) return false;

        var abParts = detectedAb.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (abParts.Length == 0) return false;
        if (!int.TryParse(abParts[0], out var currentMajor)) return false;

        if (currentMajor < 4) return false;

        if (string.IsNullOrWhiteSpace(constraint)) return false;

        var modParts = constraint.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (modParts.Length == 0) return false;
        if (!int.TryParse(modParts[0], out var modMajor)) return false;

        return modMajor == 4;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}