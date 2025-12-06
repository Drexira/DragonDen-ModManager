using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DragonDen.ModManager.Services;

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