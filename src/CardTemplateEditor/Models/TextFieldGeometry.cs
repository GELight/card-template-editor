namespace CardTemplateEditor.Models;

/// <summary>
/// Geometrie-Konstanten, die Editor und Export-Renderer teilen müssen, damit
/// das exportierte PNG pixel-identisch zum Live-Editor aussieht.
///
/// Hintergrund: Im Editor sitzt das Textinhaltsfeld INNERHALB des TextFieldFrame.
/// Der äußere Drag-Border hat Padding = <see cref="DragBorderPadding"/>, die
/// innere TextBox hat zusätzlich Padding = <see cref="InnerTextBoxPadding"/>.
/// Der Text rendert also <see cref="TextInset"/> Pixel innerhalb der X,Y,Width,
/// Height-Bounds des Modells. ExportService muss denselben Inset zeichnen,
/// sonst springt der Text beim Export auf eine andere Position.
/// </summary>
public static class TextFieldGeometry
{
    public const double DragBorderPadding = 8.0;
    public const double InnerTextBoxPadding = 2.0;

    /// <summary>
    /// Gesamter Pixel-Inset zwischen Modell-Frame (X, Y, Width, Height) und
    /// dem tatsächlich sichtbaren Textbereich.
    /// </summary>
    public const double TextInset = DragBorderPadding + InnerTextBoxPadding;

    /// <summary>
    /// Zusätzlicher Pixel-Rand rund um das eigentliche Textfeld im Editor-
    /// Visual: schafft Hit-Test-Fläche für Rotation- und Eckpunkt-Verzerrungs-
    /// Handles, die sonst (mit negativen Margins ausserhalb der Frame-Bounds)
    /// nicht hit-testbar wären (siehe Iteration 4 Lessons Learned).
    /// Die TextFieldFrame-UserControl ist deshalb breiter/höher als das
    /// gespeicherte Modell-Rechteck; der innere Frame sitzt mit Margin
    /// = OuterPadding zentriert darin.
    /// Reine Editor-Geometrie — wird nicht persistiert und nicht im Export
    /// reflektiert.
    /// </summary>
    public const double OuterPadding = 32.0;
}
