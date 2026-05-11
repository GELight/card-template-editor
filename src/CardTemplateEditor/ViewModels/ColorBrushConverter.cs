using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CardTemplateEditor.ViewModels;

/// <summary>
/// Wandelt ein Avalonia.Media.Color in einen SolidColorBrush für TextBox.Foreground
/// oder beliebige Brush-Bindings. ColorPicker und VM speichern Color, das UI braucht
/// einen Brush — daher dieser einseitige Converter.
/// </summary>
public sealed class ColorBrushConverter : IValueConverter
{
    public static readonly ColorBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color c) return new SolidColorBrush(c);
        if (value is string hex && Color.TryParse(hex, out var parsed))
            return new SolidColorBrush(parsed);
        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
