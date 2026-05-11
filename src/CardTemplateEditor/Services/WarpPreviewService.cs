using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Models;
using SkiaSharp;

namespace CardTemplateEditor.Services;

/// <summary>
/// Live-Perspektiv-Vorschau für TextFieldFrame: rastert den Text rectangulär in
/// eine Avalonia-RTB, transformiert das Bitmap projektiv (4-Punkt-Homographie)
/// und liefert ein <see cref="Layout"/>-Tupel aus Bitmap + Versatz zurück. Der
/// Versatz erlaubt es, die Vorschau auch dann komplett anzuzeigen, wenn
/// Eckpunkte weit über den OuterPadding-Bereich des Frames hinausgezogen
/// werden — die Bitmap wächst dynamisch mit der Bounding-Box des verzerrten
/// Quads (plus Sicherheitsrand).
///
/// Hintergrund: Avalonias DrawingContext kann nur affin transformieren —
/// für echte 4-Punkt-Verzerrung im Editor (analog zum Export-Output) muss der
/// Bitmap-Pfad über SkiaSharp laufen, identisch zu <see cref="ExportService.ApplyWarpedField"/>.
/// Der einzige Unterschied: hier KEIN Rotations-Anteil, weil das umschließende
/// TextFieldFrame.UserControl bereits über RenderTransform rotiert wird.
/// </summary>
public static class WarpPreviewService
{
    /// <summary>
    /// Bitmap-Vorschau plus Top-Left-Offset relativ zur OuterRoot-Top-Left
    /// (kann negativ sein, wenn Eckpunkte das UserControl-Rect verlassen).
    /// </summary>
    public sealed record Layout(Bitmap Bitmap, double OffsetX, double OffsetY);

    /// <summary>
    /// Zusatzrand (Pixel), der um die Bounding-Box der vier verzerrten
    /// Eckpunkte gelegt wird. Verhindert, dass Anti-Aliasing-Pixel direkt am
    /// Bitmap-Rand abgeschnitten werden.
    /// </summary>
    private const int SafetyMargin = 4;

    /// <summary>
    /// Liefert das Vorschau-Layout für ein verzerrtes Textfeld oder null, wenn
    /// keine Verzerrung anliegt, der Text leer ist oder die Geometrie entartet ist.
    /// </summary>
    public static Layout? RenderPreview(TextField field)
    {
        if (field is null) return null;
        if (string.IsNullOrEmpty(field.CurrentText)) return null;
        // Bewusst KEIN Effect-Gate mehr: Editor und Export müssen IMMER durch
        // dieselbe DrawTextField-Pipeline laufen, sonst kommen Sub-Pixel-
        // Differenzen zwischen Avalonias TextBox-Layout und FormattedText
        // durch — und der gespeicherte PNG-Output weicht von der Live-
        // Vorschau ab. Siehe CLAUDE.md "Editor ↔ Export-Konsistenz".

        var w = (int)Math.Ceiling(field.Width);
        var h = (int)Math.Ceiling(field.Height);
        if (w < 1 || h < 1) return null;

        // 1. Rectangulärer Text in eine field-lokale RTB (X=Y=0).
        var localField = new TextField
        {
            X = 0, Y = 0,
            Width = field.Width, Height = field.Height,
            FontFamily = field.FontFamily, FontSize = field.FontSize,
            FontWeight = field.FontWeight, Color = field.Color,
            CurrentText = field.CurrentText,
            HorizontalTextAlignment = field.HorizontalTextAlignment,
            VerticalTextAlignment = field.VerticalTextAlignment,
            Rotation = 0,
            StretchX = field.StretchX, StretchY = field.StretchY,
            AutoFit = field.AutoFit,
            LineHeight = field.LineHeight,
            LetterSpacing = field.LetterSpacing,
            BoldRanges = field.BoldRanges,
        };
        // WICHTIG: Avalonias RenderTargetBitmap zeichnet Text auf TRANSPARENTEM
        // Hintergrund nur mit BINÄREM Alpha (keine anti-aliased Glyph-Kanten),
        // weil der Compositor nichts hat, gegen das er blenden könnte. Der
        // Export-Pfad zeichnet hingegen auf das (geladene) Quellbild und
        // bekommt deshalb saubere AA-Kanten. Damit Editor-Vorschau und PNG-
        // Output PIXEL-ÄHNLICH werden, rendern wir hier den Text auf weißen
        // Hintergrund und extrahieren danach das Alpha per Color-Keying:
        // pixel = white * (1 - cov) + textColor * cov  ⇒  cov herausrechnen.
        using var textRtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = textRtb.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            ExportService.DrawTextField(ctx, localField);
        }

        // RTB → SKBitmap (PNG-Roundtrip), dann Alpha-Key direkt auf den
        // SKBitmap-Pixelbuffer. SKBitmap statt RTB-Mutation, weil RTB kein
        // direktes Pixel-Write-Back unterstützt.
        using var textMs = new MemoryStream();
        textRtb.Save(textMs);
        textMs.Position = 0;
        var textSk = SKBitmap.Decode(textMs);
        try
        {
            ApplyAlphaKeyOnWhiteBackground(textSk, localField.Color);

            // 2a. Fast-Path für nicht-gewarpte Felder: das alpha-gekeyte
            //     SKBitmap direkt als PNG → Avalonia.Bitmap zurück. Pixel-
            //     äquivalent zur Export-Pipeline (gleiches DrawTextField, AA-
            //     Kanten korrekt) plus transparenter Außenbereich.
            var pad = TextFieldGeometry.OuterPadding;
            if (!IsWarped(field))
            {
                using var skImg = SKImage.FromBitmap(textSk);
                using var pngData = skImg.Encode(SKEncodedImageFormat.Png, 100);
                using var pngStream = pngData.AsStream();
                return new Layout(new Bitmap(pngStream), pad, pad);
            }

            // 2b. Warped Pfad: textSk wird gleich für die Homographie genutzt.
            return RenderWarpedPath(field, w, h, textSk, pad);
        }
        finally
        {
            textSk.Dispose();
        }
    }

    private static Layout? RenderWarpedPath(TextField field, int w, int h, SKBitmap textSk, double pad)
    {

        // 3. Ziel-Eckpunkte im OuterRoot-Raum. Bounding-Box berechnen, damit
        //    die Vorschau auch bei extrem ausgezogenen Ecken nicht abgeschnitten
        //    wird. Bitmap wird auf bbox(quad) + SafetyMargin dimensioniert und
        //    per Offset-Verschiebung im Editor positioniert.
        var quad = new[]
        {
            (X: pad + field.CornerNWdx,                 Y: pad + field.CornerNWdy),
            (X: pad + field.Width + field.CornerNEdx,   Y: pad + field.CornerNEdy),
            (X: pad + field.Width + field.CornerSEdx,   Y: pad + field.Height + field.CornerSEdy),
            (X: pad + field.CornerSWdx,                 Y: pad + field.Height + field.CornerSWdy),
        };
        var minX = quad.Min(p => p.X) - SafetyMargin;
        var minY = quad.Min(p => p.Y) - SafetyMargin;
        var maxX = quad.Max(p => p.X) + SafetyMargin;
        var maxY = quad.Max(p => p.Y) + SafetyMargin;

        var bw = (int)Math.Ceiling(maxX - minX);
        var bh = (int)Math.Ceiling(maxY - minY);
        if (bw < 1 || bh < 1) return null;

        var info = new SKImageInfo(bw, bh, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvasBmp = new SKBitmap(info);
        using var canvas = new SKCanvas(canvasBmp);
        canvas.Clear(SKColors.Transparent);

        var src = new[]
        {
            new PerspectiveMath.Pt(0, 0),
            new PerspectiveMath.Pt(w, 0),
            new PerspectiveMath.Pt(w, h),
            new PerspectiveMath.Pt(0, h),
        };
        // Eckpunkte in das LOKALE Bitmap-Koordinatensystem übersetzen
        // (= OuterRoot-Coords − (minX, minY)).
        var dst = new[]
        {
            new PerspectiveMath.Pt(quad[0].X - minX, quad[0].Y - minY),
            new PerspectiveMath.Pt(quad[1].X - minX, quad[1].Y - minY),
            new PerspectiveMath.Pt(quad[2].X - minX, quad[2].Y - minY),
            new PerspectiveMath.Pt(quad[3].X - minX, quad[3].Y - minY),
        };
        double[] m;
        try
        {
            m = PerspectiveMath.Compute(src, dst);
        }
        catch
        {
            // Entartetes Quad (drei Punkte kollinear) → keine sinnvolle Vorschau,
            // der Wireframe-Fallback im XAML bleibt sichtbar.
            return null;
        }

        var skMatrix = new SKMatrix(
            (float)m[0], (float)m[1], (float)m[2],
            (float)m[3], (float)m[4], (float)m[5],
            (float)m[6], (float)m[7], (float)m[8]);

        canvas.Save();
        canvas.Concat(in skMatrix);
        using var textImage = SKImage.FromBitmap(textSk);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        canvas.DrawImage(textImage, 0, 0, sampling);
        canvas.Restore();

        // 5. SKBitmap → Avalonia.Bitmap (PNG-Roundtrip; SKImage.Encode liefert
        //    SKData, das wir als Stream in den Bitmap-Konstruktor geben).
        using var image = SKImage.FromBitmap(canvasBmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new Layout(new Bitmap(stream), minX, minY);
    }

    private static bool IsWarped(TextField f) =>
        f.CornerNWdx != 0 || f.CornerNWdy != 0 ||
        f.CornerNEdx != 0 || f.CornerNEdy != 0 ||
        f.CornerSEdx != 0 || f.CornerSEdy != 0 ||
        f.CornerSWdx != 0 || f.CornerSWdy != 0;

    /// <summary>
    /// Mutiert das übergebene <see cref="SKBitmap"/> in-place: Pixel werden
    /// aus dem RGB-Wert (Text auf weißem Hintergrund) zurück in Premul-Bgra
    /// mit korrektem Alpha-Kanal überführt:
    ///   pixel_rendered = white * (1 − cov) + textColor * cov
    ///   ⇒ cov = (255 − pixel) / (255 − textColor)  pro Kanal
    /// Wir nehmen das Maximum über die drei Kanäle, weil dort der Signal-
    /// Hub am stärksten ist (z. B. bei rotem Text dominiert der Rot-Kanal,
    /// die anderen Kanäle würden falsch "0" liefern wenn Text-Color-Kanal=255).
    /// </summary>
    private static void ApplyAlphaKeyOnWhiteBackground(SKBitmap sk, string textColorHex)
    {
        var color = Color.TryParse(string.IsNullOrEmpty(textColorHex) ? "#000000" : textColorHex, out var c)
            ? c : Colors.Black;

        var w = sk.Width;
        var h = sk.Height;
        // SKBitmap.GetPixel/SetPixel ist langsam — direkter Pointer-Zugriff
        // über GetPixels() ist deutlich schneller. SKBitmap.Decode liefert
        // RGBA8888 (oder BGRA8888 je nach Plattform); wir lesen den ColorType
        // und differenzieren.
        var info = sk.Info;
        var pixelsPtr = sk.GetPixels();
        unsafe
        {
            var basePtr = (byte*)pixelsPtr.ToPointer();
            for (var y = 0; y < h; y++)
            {
                var row = basePtr + y * info.RowBytes;
                for (var x = 0; x < w; x++)
                {
                    var px = row + x * 4;
                    byte r, g, b;
                    if (info.ColorType == SKColorType.Bgra8888)
                    { b = px[0]; g = px[1]; r = px[2]; }
                    else
                    { r = px[0]; g = px[1]; b = px[2]; }

                    double covR = color.R == 255 ? 0 : (255.0 - r) / (255.0 - color.R);
                    double covG = color.G == 255 ? 0 : (255.0 - g) / (255.0 - color.G);
                    double covB = color.B == 255 ? 0 : (255.0 - b) / (255.0 - color.B);
                    var cov = Math.Max(0, Math.Min(1, Math.Max(covR, Math.Max(covG, covB))));
                    var alpha = (byte)Math.Round(cov * 255);

                    var pmR = (byte)(color.R * alpha / 255);
                    var pmG = (byte)(color.G * alpha / 255);
                    var pmB = (byte)(color.B * alpha / 255);
                    if (info.ColorType == SKColorType.Bgra8888)
                    { px[0] = pmB; px[1] = pmG; px[2] = pmR; }
                    else
                    { px[0] = pmR; px[1] = pmG; px[2] = pmB; }
                    px[3] = alpha;
                }
            }
        }
        sk.NotifyPixelsChanged();
    }

    /// <summary>
    /// True, sobald irgendeine Eigenschaft das gerenderte Resultat vom
    /// "einfachen TextBox-Rendering" abweichen lässt: Warp-Offset, Stretch,
    /// AutoFit, explizite LineHeight oder LetterSpacing. Rotation alleine
    /// zählt nicht — die wendet die UserControl ohnehin korrekt per
    /// RenderTransform auf TextBox UND Bitmap an.
    /// </summary>
    public static bool HasRenderEffect(TextField f) =>
        IsWarped(f) ||
        f.StretchX != 1.0 || f.StretchY != 1.0 ||
        f.AutoFit ||
        !double.IsNaN(f.LineHeight) ||
        f.LetterSpacing != 0.0;
}
