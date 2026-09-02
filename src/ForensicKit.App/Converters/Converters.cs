using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ForensicKit.Core.Models;

namespace ForensicKit.App.Converters;

internal static class StatusColors
{
    public static Color Of(object? value) => value switch
    {
        ToolStatus.Downloaded => Color.FromRgb(0x00, 0xE6, 0x76),        // IMPERO green
        ToolStatus.UpdateAvailable => Color.FromRgb(0xFF, 0x8C, 0x00),   // IMPERO orange
        _ => Color.FromRgb(0x8A, 0x8A, 0x99)                             // muted
    };
}

/// <summary>Soft (translucent) pill background for a <see cref="ToolStatus"/>.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var c = StatusColors.Of(value);
        return new SolidColorBrush(Color.FromArgb(0x28, c.R, c.G, c.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Vivid foreground (dot + text) for a <see cref="ToolStatus"/> pill.</summary>
public sealed class StatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new SolidColorBrush(StatusColors.Of(value));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps a <see cref="ToolStatus"/> to a human-readable label.</summary>
public sealed class StatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            ToolStatus.Downloaded => "Scaricato",
            ToolStatus.UpdateAvailable => "Aggiornamento disponibile",
            _ => "Non scaricato"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Boolean to Visibility. Pass "invert" as parameter to reverse.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Maps a favorite flag to a gold (true) or muted gray (false) brush.</summary>
public sealed class FavoriteBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Gold = new(Color.FromRgb(0xF5, 0xC5, 0x18));
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0x77, 0x77, 0x77));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Gold : Muted;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal static class SeverityColors
{
    public static Color Of(object? value)
    {
        var s = (value as string)?.ToLowerInvariant() ?? "";
        if (s.StartsWith("alt")) return Color.FromRgb(0xFF, 0x2E, 0x88);       // alto -> pink/red
        if (s.StartsWith("sosp")) return Color.FromRgb(0xFF, 0x8C, 0x00);      // sospetto -> orange
        return Color.FromRgb(0x00, 0xD4, 0xFF);                                // info -> cyan
    }
}

/// <summary>Soft translucent background for a severity string (timeline row/pill).</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var c = SeverityColors.Of(value);
        return new SolidColorBrush(Color.FromArgb(0x2A, c.R, c.G, c.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Vivid foreground for a severity string (dot + text).</summary>
public sealed class SeverityToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new SolidColorBrush(SeverityColors.Of(value));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverts a boolean (e.g. IsBusy -> IsEnabled).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
