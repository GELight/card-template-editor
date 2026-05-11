using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Models;
using SkiaSharp;

namespace CardTemplateEditor.Services;

/// <summary>
/// Rendert eine ImageSlot-Komposition (Originalbild + überlagerte TextFields) als PNG.
/// Koordinaten der TextFields sind in Bildpixeln; das Quellbild bleibt erhalten —
/// wir zeichnen lediglich die Texte darüber.
/// </summary>
/// <remarks>
/// Render-Pfade:
/// 1. **Achsenparallel oder rein rotiert**: Avalonia DrawingContext direkt. Schnell,
///    bleibt mit dem Editor pixel-konsistent.
/// 2. **Mit 4-Punkt-Eckpunkt-Verzerrung (IsWarped)**: Avalonia rendert Hintergrund
///    + nicht-warped TextFields, dann konvertieren wir die Bitmap nach SkiaSharp,
///    rendern jedes warped TextField in eine Zwischen-RTB (rechteckig), laden sie
///    als SKBitmap und überlagern sie via projektiver Homographie. Avalonias
///    DrawingContext kann nur affin transformieren — Perspektive geht nur über
///    den Skia-Direktpfad.
/// </remarks>
public class ExportService
{
    public virtual void ExportSlot(
        Template template,
        ImageSlot slot,
        string sourceImagePath,
        string destPath)
    {
        if (string.IsNullOrWhiteSpace(destPath))
            throw new ArgumentException("destPath darf nicht leer sein.", nameof(destPath));
        if (!File.Exists(sourceImagePath))
            throw new FileNotFoundException($"Quellbild nicht gefunden: {sourceImagePath}", sourceImagePath);

        var destDir = Path.GetDirectoryName(destPath);
        if (string.IsNullOrEmpty(destDir))
            throw new ArgumentException(
                $"Zielpfad ohne Verzeichnis ist nicht erlaubt: {destPath}", nameof(destPath));

        try
        {
            Directory.CreateDirectory(destDir);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Zielverzeichnis konnte nicht angelegt werden: {destDir}", ex);
        }

        var slotFields = template.TextFields.Where(f => f.ImageSlotId == slot.Id).ToList();
        var warpedFields = slotFields.Where(IsWarped).ToList();
        var nonWarpedFields = slotFields.Where(f => !IsWarped(f)).ToList();

        using var source = new Bitmap(sourceImagePath);
        var pixelSize = source.PixelSize;
        using var rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96));

        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.DrawImage(source, new Rect(0, 0, pixelSize.Width, pixelSize.Height));

            foreach (var f in nonWarpedFields)
            {
                if (string.IsNullOrEmpty(f.CurrentText)) continue;
                DrawTextField(ctx, f);
            }
        }

        if (warpedFields.Count == 0)
        {
            // Kein Warp → Avalonia-RTB direkt schreiben (verlustfrei).
            rtb.Save(destPath);
            return;
        }

        // Warp-Pfad: nach Skia überleiten. Wir kodieren die Avalonia-RTB als PNG
        // in einen MemoryStream (verlustfrei) und dekodieren in SkiaSharp.
        using var mainMs = new MemoryStream();
        rtb.Save(mainMs);
        mainMs.Position = 0;
        using var mainSk = SKBitmap.Decode(mainMs);
        using var mainCanvas = new SKCanvas(mainSk);

        foreach (var f in warpedFields)
        {
            if (string.IsNullOrEmpty(f.CurrentText)) continue;
            ApplyWarpedField(mainCanvas, f);
        }

        // SKBitmap → PNG-Datei.
        using var image = SKImage.FromBitmap(mainSk);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fileStream = File.Create(destPath);
        data.SaveTo(fileStream);
    }

    /// <summary>
    /// Liefert true, sobald ein TextField eine 4-Punkt-Verzerrung (≠ 0/0)
    /// hat — dann muss der SkiaSharp-Pfad genutzt werden.
    /// </summary>
    public static bool IsWarped(TextField f) =>
        f.CornerNWdx != 0 || f.CornerNWdy != 0 ||
        f.CornerNEdx != 0 || f.CornerNEdy != 0 ||
        f.CornerSEdx != 0 || f.CornerSEdy != 0 ||
        f.CornerSWdx != 0 || f.CornerSWdy != 0;

    /// <summary>
    /// Achsenparalleler oder rein rotierter Pfad: zeichnet via Avalonia DrawingContext.
    /// internal static, damit der Editor (WarpPreviewService) denselben Text-Rasterer
    /// für die Live-Vorschau wiederverwenden kann.
    ///
    /// Berücksichtigt zusätzlich: StretchX/Y (non-uniform Glyph-Skalierung),
    /// AutoFit (Skalierung passend zur Box), LineHeight und LetterSpacing.
    /// LetterSpacing != 0 erzwingt ein Glyph-für-Glyph-Rendering, weil
    /// Avalonias FormattedText keinen Letter-Spacing-Wert kennt.
    /// </summary>
    internal static void DrawTextField(DrawingContext ctx, TextField f)
    {
        var typeface = new Typeface(
            SafeFontFamily(f.FontFamily),
            FontStyle.Normal,
            ParseWeight(f.FontWeight));
        var brush = new SolidColorBrush(SafeParseColor(f.Color));

        var inset = TextFieldGeometry.TextInset;
        var innerWidth = Math.Max(1.0, f.Width - 2 * inset);
        var innerHeight = Math.Max(1.0, f.Height - 2 * inset);

        // Stretch/AutoFit ermitteln. Bei AutoFit messen wir den Text einmal
        // unstretched in seiner natürlichen Breite (MaxTextWidth = ∞) und
        // berechnen daraus den Skalierungsfaktor, der das Inhalts-Rect
        // passend füllt.
        var (stretchX, stretchY) = ComputeStretch(f, typeface, brush, innerWidth, innerHeight);

        // MaxTextWidth bezieht sich auf den GERENDERTEN Bildraum. Wir wollen,
        // dass der Text nach der Stretchung in innerWidth passt, also vor der
        // Stretchung in (innerWidth / stretchX).
        var wrapWidth = stretchX > 0.001 ? innerWidth / stretchX : innerWidth;

        var ft = new FormattedText(
            f.CurrentText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            f.FontSize,
            brush)
        {
            MaxTextWidth = wrapWidth,
            TextAlignment = ParseTextAlignment(f.HorizontalTextAlignment),
        };
        if (!double.IsNaN(f.LineHeight) && f.LineHeight > 0)
            ft.LineHeight = f.LineHeight;

        // Vertikale Ausrichtung wird im (vor-stretch) Layout-Raum gerechnet
        // und mit stretchY skaliert auf den gerenderten Y-Versatz übertragen.
        var verticalOffset = ParseVerticalOffset(
            f.VerticalTextAlignment, innerHeight / Math.Max(0.001, stretchY), ft.Height);
        var textTopLeft = new Point(f.X + inset, f.Y + inset + verticalOffset * stretchY);

        // Transform-Stack: rund um textTopLeft skalieren, dann ggf. rotieren
        // um den Box-Mittelpunkt. Reihenfolge ist wichtig, damit die Stretchung
        // entlang der Frame-Achsen wirkt (nicht der Welt-Achsen).
        var hasStretch = stretchX != 1.0 || stretchY != 1.0;
        var hasRotation = f.Rotation != 0;
        var transform = Matrix.Identity;
        if (hasStretch)
        {
            transform =
                Matrix.CreateTranslation(-textTopLeft.X, -textTopLeft.Y) *
                Matrix.CreateScale(stretchX, stretchY) *
                Matrix.CreateTranslation(textTopLeft.X, textTopLeft.Y);
        }
        if (hasRotation)
        {
            // Drehung um den Origin-Punkt — Default ist die Frame-Mitte
            // (RelX=RelY=0.5), aber der User kann den Drehpunkt im Editor
            // verschieben. ExportService muss denselben Punkt verwenden,
            // damit Editor und exportiertes PNG geometrisch übereinstimmen.
            var cx = f.X + f.Width * f.RotationOriginRelX;
            var cy = f.Y + f.Height * f.RotationOriginRelY;
            var rad = f.Rotation * Math.PI / 180.0;
            transform = transform *
                Matrix.CreateTranslation(-cx, -cy) *
                Matrix.CreateRotation(rad) *
                Matrix.CreateTranslation(cx, cy);
        }

        if (transform != Matrix.Identity)
        {
            using (ctx.PushTransform(transform))
            {
                DrawTextOrSpacedGlyphs(ctx, f, typeface, brush, textTopLeft, wrapWidth, ft);
            }
        }
        else
        {
            DrawTextOrSpacedGlyphs(ctx, f, typeface, brush, textTopLeft, wrapWidth, ft);
        }
    }

    /// <summary>
    /// Liefert effektive Skalierungsfaktoren (X, Y). Bei <see cref="TextField.AutoFit"/>
    /// werden sie aus der Größe einer ungebundenen FormattedText-Messung abgeleitet.
    /// </summary>
    private static (double X, double Y) ComputeStretch(
        TextField f, Typeface typeface, IBrush brush,
        double innerWidth, double innerHeight)
    {
        if (!f.AutoFit)
            return (Math.Max(0.001, f.StretchX), Math.Max(0.001, f.StretchY));

        if (string.IsNullOrEmpty(f.CurrentText)) return (1.0, 1.0);

        var probe = new FormattedText(
            f.CurrentText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            f.FontSize,
            brush);
        if (!double.IsNaN(f.LineHeight) && f.LineHeight > 0)
            probe.LineHeight = f.LineHeight;
        var w = Math.Max(1.0, probe.WidthIncludingTrailingWhitespace);
        var h = Math.Max(1.0, probe.Height);
        return (innerWidth / w, innerHeight / h);
    }

    /// <summary>
    /// Wenn <see cref="TextField.LetterSpacing"/> ≠ 0 ODER <see cref="TextField.BoldRanges"/>
    /// nicht leer ist: rendert manuell glyph-by-glyph (mehrzeilig nur bei expliziten
    /// "\n"-Linebreaks, kein Auto-Wordwrap; Bold pro Zeichen via BoldRanges).
    /// Sonst: einmal als FormattedText.
    /// </summary>
    private static void DrawTextOrSpacedGlyphs(
        DrawingContext ctx,
        TextField f,
        Typeface typeface,
        IBrush brush,
        Point textTopLeft,
        double wrapWidth,
        FormattedText defaultFt)
    {
        var hasMixedBold = f.BoldRanges is { Count: > 0 };
        if (f.LetterSpacing == 0 && !hasMixedBold)
        {
            ctx.DrawText(defaultFt, textTopLeft);
            return;
        }

        var defaultWeight = ParseWeight(f.FontWeight);
        var boldTypeface = new Typeface(typeface.FontFamily, typeface.Style, FontWeight.Bold);
        var defaultTypeface = new Typeface(typeface.FontFamily, typeface.Style, defaultWeight);

        Typeface PerCharTypeface(int absoluteIndex)
        {
            return hasMixedBold && Models.BoldRangeMath.IsBoldAt(f.BoldRanges, absoluteIndex)
                ? boldTypeface
                : defaultTypeface;
        }

        var lines = (f.CurrentText ?? "").Split('\n');
        var lineHeight = !double.IsNaN(f.LineHeight) && f.LineHeight > 0
            ? f.LineHeight
            : defaultFt.Height / Math.Max(1, lines.Length);
        var hAlign = ParseTextAlignment(f.HorizontalTextAlignment);

        var y = textTopLeft.Y;
        var absoluteIndex = 0;
        foreach (var line in lines)
        {
            var glyphWidths = new double[line.Length];
            var lineWidth = 0.0;
            for (var i = 0; i < line.Length; i++)
            {
                var ft = new FormattedText(
                    line[i].ToString(),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    PerCharTypeface(absoluteIndex + i),
                    f.FontSize,
                    brush);
                glyphWidths[i] = ft.WidthIncludingTrailingWhitespace;
                lineWidth += glyphWidths[i];
            }
            if (line.Length > 1) lineWidth += f.LetterSpacing * (line.Length - 1);

            var startX = textTopLeft.X;
            startX += hAlign switch
            {
                TextAlignment.Center => Math.Max(0, (wrapWidth - lineWidth) / 2.0),
                TextAlignment.Right => Math.Max(0, wrapWidth - lineWidth),
                _ => 0,
            };

            var x = startX;
            for (var i = 0; i < line.Length; i++)
            {
                var ft = new FormattedText(
                    line[i].ToString(),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    PerCharTypeface(absoluteIndex + i),
                    f.FontSize,
                    brush);
                ctx.DrawText(ft, new Point(x, y));
                x += glyphWidths[i] + f.LetterSpacing;
            }
            absoluteIndex += line.Length + 1; // +1 for the '\n'
            y += lineHeight;
        }
    }

    /// <summary>
    /// 4-Punkt-Verzerrung über SkiaSharp:
    /// 1. Text wird rectangular in eine Zwischen-RTB der Field-Größe gerendert
    ///    (ohne Rotation, ohne Warp — exakt wie der Editor in Edit-Mode).
    /// 2. Diese RTB wird als SKBitmap gelesen.
    /// 3. Eine 3×3-Homographie mappt (0..W, 0..H) → 4 Ziel-Eckpunkte
    ///    (Modell-Rect + ggf. Rotation um Mittelpunkt + Eckpunkt-Offsets).
    /// 4. Skia-Canvas.Concat mit dieser Matrix → DrawBitmap zeichnet das
    ///    rectangular Text-Bitmap perspektivisch verzerrt auf das Hauptbild.
    /// </summary>
    private static void ApplyWarpedField(SKCanvas mainCanvas, TextField f)
    {
        var w = (int)Math.Ceiling(f.Width);
        var h = (int)Math.Ceiling(f.Height);
        if (w < 1 || h < 1) return;

        // Schritt 1: rectangular Text-RTB (Field-lokal: Origin = (0,0)).
        // ALLE Render-relevanten Felder durchreichen — sonst wenden Editor und
        // Export unterschiedliche Effekte an (Stretch, AutoFit, LineHeight,
        // LetterSpacing) und das exportierte PNG weicht von der Live-Vorschau ab.
        using var textRtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var c = textRtb.CreateDrawingContext())
        {
            var local = new TextField
            {
                Id = f.Id, ImageSlotId = f.ImageSlotId, Name = f.Name,
                X = 0, Y = 0,
                Width = f.Width, Height = f.Height,
                FontFamily = f.FontFamily, FontSize = f.FontSize,
                FontWeight = f.FontWeight, Color = f.Color,
                CurrentText = f.CurrentText,
                HorizontalTextAlignment = f.HorizontalTextAlignment,
                VerticalTextAlignment = f.VerticalTextAlignment,
                Rotation = 0, // Rotation wird in der Homographie unten berücksichtigt
                StretchX = f.StretchX, StretchY = f.StretchY,
                AutoFit = f.AutoFit,
                LineHeight = f.LineHeight,
                LetterSpacing = f.LetterSpacing,
                BoldRanges = f.BoldRanges,
            };
            DrawTextField(c, local);
        }

        // Schritt 2: RTB → SKBitmap (über PNG-Roundtrip; Avalonia 12 hat keinen
        // direkten RGBA-Buffer-Export auf RenderTargetBitmap, der mit allen
        // Backends portabel funktioniert).
        using var textMs = new MemoryStream();
        textRtb.Save(textMs);
        textMs.Position = 0;
        using var textSk = SKBitmap.Decode(textMs);

        // Schritt 3: 4 Ziel-Eckpunkte berechnen — Rotation um Mittelpunkt, dann
        // Eckpunkt-Offsets hinzu. Reihenfolge muss zu PerspectiveMath passen:
        // NW, NE, SE, SW.
        var corners = ComputeDestinationCorners(f);

        var src = new[]
        {
            new PerspectiveMath.Pt(0, 0),
            new PerspectiveMath.Pt(w, 0),
            new PerspectiveMath.Pt(w, h),
            new PerspectiveMath.Pt(0, h),
        };
        var matrix = PerspectiveMath.Compute(src, corners);
        var skMatrix = ToSKMatrix(matrix);

        // Schritt 4: Apply matrix and draw text bitmap onto main canvas.
        mainCanvas.Save();
        mainCanvas.Concat(in skMatrix);
        // SkiaSharp 3.x: SKBitmap → SKImage und DrawImage mit SKSamplingOptions.
        // (DrawBitmap mit Sampling existiert in 3.x nicht mehr in dieser Form.)
        using var textImage = SKImage.FromBitmap(textSk);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        mainCanvas.DrawImage(textImage, 0, 0, sampling);
        mainCanvas.Restore();
    }

    /// <summary>
    /// Berechnet die 4 Ziel-Eckpunkte (NW, NE, SE, SW) im Bildpixel-Raum:
    /// Rotation um den Mittelpunkt der unrotierten Box, dann Eckpunkt-Offsets
    /// (CornerXxxdx/dy) auf die jeweils rotierte Eck-Position addiert.
    /// Reihenfolge muss zu PerspectiveMath.UnitSquareToQuad passen.
    /// internal für Editor-↔-Export-Konsistenztests.
    /// </summary>
    internal static PerspectiveMath.Pt[] ComputeDestinationCorners(TextField f)
    {
        // Drehung um den Origin-Punkt (frei wählbar, default Frame-Mitte).
        var cx = f.X + f.Width * f.RotationOriginRelX;
        var cy = f.Y + f.Height * f.RotationOriginRelY;
        var rad = f.Rotation * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        PerspectiveMath.Pt Rotate(double x, double y)
        {
            var dx = x - cx;
            var dy = y - cy;
            return new PerspectiveMath.Pt(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
        }

        // Eckpunkt-Offsets sind im LOKALEN (unrotierten) Frame-Raum gespeichert
        // — das ist die Konvention, die der Editor beim Drag nutzt
        // (TextFieldFrame.OnPointerMoved → InverseRotate → WriteCornerOffset).
        // Vor dem Addieren zur rotierten Eck-Position muss der Offset-VEKTOR
        // mitrotiert werden, damit "Drag eines Eckpunkts in lokalem X" auch im
        // exportierten PNG entlang der Frame-X-Achse sitzt — und nicht entlang
        // der Welt-X-Achse. Sonst weicht das gespeicherte Bild von der Editor-
        // Vorschau ab, sobald gleichzeitig Rotation ≠ 0 und ein Warp-Offset
        // gesetzt sind.
        PerspectiveMath.Pt RotateVector(double dx, double dy)
            => new(dx * cos - dy * sin, dx * sin + dy * cos);

        var nw = Rotate(f.X, f.Y);
        var ne = Rotate(f.X + f.Width, f.Y);
        var se = Rotate(f.X + f.Width, f.Y + f.Height);
        var sw = Rotate(f.X, f.Y + f.Height);

        var nwOff = RotateVector(f.CornerNWdx, f.CornerNWdy);
        var neOff = RotateVector(f.CornerNEdx, f.CornerNEdy);
        var seOff = RotateVector(f.CornerSEdx, f.CornerSEdy);
        var swOff = RotateVector(f.CornerSWdx, f.CornerSWdy);

        return new[]
        {
            new PerspectiveMath.Pt(nw.X + nwOff.X, nw.Y + nwOff.Y),
            new PerspectiveMath.Pt(ne.X + neOff.X, ne.Y + neOff.Y),
            new PerspectiveMath.Pt(se.X + seOff.X, se.Y + seOff.Y),
            new PerspectiveMath.Pt(sw.X + swOff.X, sw.Y + swOff.Y),
        };
    }

    /// <summary>
    /// Konvertiert unsere 9-Element-Matrix (Row-Major) in SkiaSharps SKMatrix.
    /// Konvention SKMatrix: ScaleX, SkewX, TransX, SkewY, ScaleY, TransY,
    /// Persp0, Persp1, Persp2 (Row-Major auf den Indizes 0..8).
    /// Unsere Konvention deckt sich (m11=ScaleX, m12=SkewX, m13=TransX, …).
    /// </summary>
    private static SKMatrix ToSKMatrix(double[] m)
    {
        return new SKMatrix(
            (float)m[0], (float)m[1], (float)m[2],
            (float)m[3], (float)m[4], (float)m[5],
            (float)m[6], (float)m[7], (float)m[8]);
    }

    private static FontWeight ParseWeight(string s) =>
        Enum.TryParse<FontWeight>(s, ignoreCase: true, out var w) ? w : FontWeight.Normal;

    /// <summary>
    /// Robust gegen leere/unbekannte Schriftartnamen — sonst würde der
    /// FontFamily-Konstruktor werfen und der gesamte Export-Lauf abbrechen.
    /// Auf Linux fehlt z. B. häufig "Arial"; wir fallen dann auf den System-Default zurück.
    /// </summary>
    private static FontFamily SafeFontFamily(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return FontFamily.Default;
        try { return new FontFamily(name); }
        catch { return FontFamily.Default; }
    }

    private static Color SafeParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Colors.Black;
        return Color.TryParse(hex, out var c) ? c : Colors.Black;
    }

    private static TextAlignment ParseTextAlignment(string s) => s switch
    {
        "Center" => TextAlignment.Center,
        "Right" => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    private static double ParseVerticalOffset(string mode, double boxHeight, double textHeight)
    {
        var space = Math.Max(0, boxHeight - textHeight);
        return mode switch
        {
            "Center" => space / 2.0,
            "Bottom" => space,
            _ => 0,
        };
    }
}
