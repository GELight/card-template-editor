namespace CardTemplateEditor.ViewModels;

/// <summary>
/// Aktueller Bearbeitungs-Modus für die 8 Resize-Handles eines Textfelds.
/// Wird in <see cref="MainWindowViewModel.EditMode"/> aus den live
/// gehaltenen Modifier-Tasten abgeleitet (Iteration 14): kein Modifier =
/// Scale, Strg = Distort, Alt = ScaleUniform (Aspekt-Lock), Shift+Alt =
/// Skew. Rotate wird nicht mehr per Modifier ausgelöst, sondern bleibt
/// dem dedizierten Rotation-Handle vorbehalten — das Enum hat den Wert
/// trotzdem, falls die Status-Anzeige beim Drag am Rotation-Handle den
/// Mode darstellen soll.
///
/// Farb-Mapping (Toolbar-Indikator + Hover-Farbe der Handles):
/// Scale=DodgerBlue, Distort=Gold, Skew=MediumSeaGreen, Rotate=Orange.
/// </summary>
public enum TextFieldEditMode
{
    /// <summary>Klassisches Resize: Box-Dimensionen ändern, Eckpunkt-Offsets bleiben 0.</summary>
    Scale,

    /// <summary>Resize mit Aspekt-Lock (Alt-Modifier): W/H-Verhältnis bleibt konstant.</summary>
    ScaleUniform,

    /// <summary>Perspektive (Strg-Modifier): nur das gezogene Eckpunkt-Offset wandert.</summary>
    Distort,

    /// <summary>Skew/Schräg-Stellen (Shift+Alt): zwei benachbarte Eckpunkte parallel.</summary>
    Skew,

    /// <summary>Rotation um den Drehpunkt (orange Rotation-Handle).</summary>
    Rotate,
}
