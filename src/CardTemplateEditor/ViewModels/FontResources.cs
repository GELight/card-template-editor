using System.Linq;
using Avalonia.Media;

namespace CardTemplateEditor.ViewModels;

/// <summary>
/// Ein dropdown-fähiges Pärchen aus Anzeige-Label und persistiertem Wert.
/// Wird für die Alignment-ComboBoxen verwendet, damit das Label deutsch ("Links")
/// und der gespeicherte Wert technisch ("Left") sein kann.
/// </summary>
public sealed record AlignmentOption(string Label, string Value);

public static class FontResources
{
    /// <summary>
    /// Standardliste der Fonts für den Dropdown, falls FontManager nicht verfügbar ist
    /// (z. B. in pure Unit-Tests ohne initialisiertes Avalonia).
    /// </summary>
    private static readonly IReadOnlyList<string> DefaultFamilies = new[]
    {
        "Arial", "Calibri", "Consolas", "Courier New",
        "Georgia", "Tahoma", "Times New Roman", "Verdana",
    };

    private static IReadOnlyList<string>? _families;

    public static IReadOnlyList<string> AvailableFontFamilies
    {
        get
        {
            if (_families is not null) return _families;
            try
            {
                var fonts = FontManager.Current.SystemFonts
                    .Select(f => f.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _families = fonts.Count > 0 ? fonts : DefaultFamilies;
            }
            catch
            {
                _families = DefaultFamilies;
            }
            return _families;
        }
    }

    public static IReadOnlyList<string> AvailableFontWeights { get; } = new[]
    {
        "Thin", "Light", "Normal", "Medium", "SemiBold",
        "Bold", "ExtraBold", "Black",
    };

    public static IReadOnlyList<AlignmentOption> HorizontalAlignmentOptions { get; } = new[]
    {
        new AlignmentOption("Links", "Left"),
        new AlignmentOption("Zentriert", "Center"),
        new AlignmentOption("Rechts", "Right"),
    };

    public static IReadOnlyList<AlignmentOption> VerticalAlignmentOptions { get; } = new[]
    {
        new AlignmentOption("Oben", "Top"),
        new AlignmentOption("Mittig", "Center"),
        new AlignmentOption("Unten", "Bottom"),
    };
}
