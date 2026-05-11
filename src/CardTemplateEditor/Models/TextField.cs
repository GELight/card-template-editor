namespace CardTemplateEditor.Models;

public class TextField
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImageSlotId { get; set; }
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 200;
    public double Height { get; set; } = 30;
    public string FontFamily { get; set; } = "Arial";
    public double FontSize { get; set; } = 18;
    public string FontWeight { get; set; } = "Normal";
    public string Color { get; set; } = "#000000";
    public string CurrentText { get; set; } = "";

    /// <summary>
    /// Horizontale Textausrichtung innerhalb der Textfeld-Box.
    /// Erlaubte Werte: "Left", "Center", "Right".
    /// </summary>
    public string HorizontalTextAlignment { get; set; } = "Left";

    /// <summary>
    /// Vertikale Textausrichtung innerhalb der Textfeld-Box.
    /// Erlaubte Werte: "Top", "Center", "Bottom".
    /// </summary>
    public string VerticalTextAlignment { get; set; } = "Top";

    /// <summary>
    /// Drehung im Uhrzeigersinn in Grad. 0 = unrotiert.
    /// Drehzentrum ist der Origin-Punkt — siehe <see cref="RotationOriginRelX"/>
    /// und <see cref="RotationOriginRelY"/>. Default = Frame-Mittelpunkt.
    /// </summary>
    public double Rotation { get; set; } = 0;

    /// <summary>
    /// Drehpunkt-Position relativ zur Frame-Breite (0 = links, 1 = rechts).
    /// Default 0.5 = horizontale Mitte. User kann den Drehpunkt per Drag am
    /// Cross-Marker im Editor verschieben, damit das Feld um eine Ecke oder
    /// einen frei wählbaren Punkt rotiert.
    /// </summary>
    public double RotationOriginRelX { get; set; } = 0.5;

    /// <summary>
    /// Drehpunkt-Position relativ zur Frame-Höhe (0 = oben, 1 = unten).
    /// Default 0.5 = vertikale Mitte.
    /// </summary>
    public double RotationOriginRelY { get; set; } = 0.5;

    /// <summary>
    /// Offsets der vier Ecken (in Bildpixeln) gegenüber den Rechteck-Ecken,
    /// in Reihenfolge NW, NE, SE, SW. Werden im Editor per Eckpunkt-Drag
    /// gesetzt und vom ExportService projektiv (Perspektive) gerendert.
    /// Alle Werte 0 ⇒ achsenparalleles Rechteck (klassischer Modus).
    /// Die Rotation aus <see cref="Rotation"/> wird VOR der Eckpunkt-Verzerrung
    /// angewandt, damit Rotation und Perspektive zusammenspielen.
    /// </summary>
    public double CornerNWdx { get; set; } = 0;
    public double CornerNWdy { get; set; } = 0;
    public double CornerNEdx { get; set; } = 0;
    public double CornerNEdy { get; set; } = 0;
    public double CornerSEdx { get; set; } = 0;
    public double CornerSEdy { get; set; } = 0;
    public double CornerSWdx { get; set; } = 0;
    public double CornerSWdy { get; set; } = 0;

    /// <summary>
    /// Horizontaler/Vertikaler Skalierungsfaktor der Glyphen. 1.0 = unverändert.
    /// Wirkt rein auf das Text-Rendering (kein Einfluss auf Box-Geometrie),
    /// damit der User Schrift unabhängig von <see cref="FontSize"/> breiter/höher
    /// ziehen kann.
    /// </summary>
    public double StretchX { get; set; } = 1.0;
    public double StretchY { get; set; } = 1.0;

    /// <summary>
    /// Wenn true, wird der Text automatisch so skaliert, dass er die innere
    /// Box (Width × Height abzüglich Inset) genau ausfüllt. <see cref="FontSize"/>
    /// dient dann nur als Basis-Referenz; <see cref="StretchX"/>/<see cref="StretchY"/>
    /// werden vom Auto-Fit überschrieben.
    /// </summary>
    public bool AutoFit { get; set; } = false;

    /// <summary>
    /// Zeilenhöhe in Pixeln. NaN ⇒ Standard (Avalonia leitet aus Schrift-Metrik ab).
    /// </summary>
    public double LineHeight { get; set; } = double.NaN;

    /// <summary>
    /// Zusätzlicher Abstand zwischen aufeinanderfolgenden Buchstaben (in Pixeln).
    /// 0 = unverändertes FormattedText-Rendering. Werte ≠ 0 erzwingen ein
    /// Glyph-für-Glyph-Rendering, das automatischen Wortumbruch nicht unterstützt
    /// (manuelle "\n"-Zeilenumbrüche bleiben respektiert).
    /// </summary>
    public double LetterSpacing { get; set; } = 0.0;

    /// <summary>
    /// Halb-offene Bereiche im Text, deren Glyphen fett gerendert werden — per
    /// Strg+B-Toggle auf der Selektion in der TextBox gepflegt. Glyph-für-Glyph-
    /// Rendering im Export wechselt entsprechend zwischen <see cref="FontWeight"/>
    /// und Bold pro Index. Bereiche ≠ leer ⇒ Render-Effekt aktiv.
    /// </summary>
    public List<BoldRange> BoldRanges { get; set; } = new();
}
