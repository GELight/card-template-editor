using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;

namespace CardTemplateEditor.Tests;

public class WarpPreviewServiceTests
{
    [AvaloniaFact]
    public void RenderPreview_PlainFieldWithText_AlsoProducesBitmap()
    {
        // Editor-↔-Export-Konsistenz-Invariante: AUCH ein plain Field
        // (kein Warp/Stretch/etc.) muss eine Preview-Bitmap liefern, damit
        // der Editor dieselbe DrawTextField-Pipeline durchläuft wie der
        // Export. Nur so ist garantiert, dass kein Sub-Pixel-Versatz oder
        // TextBox-Layout-Eigenheit den gespeicherten PNG-Output von der
        // Live-Vorschau abkoppelt. Vor diesem Fix gab WarpPreviewService
        // null zurück, sobald keine Render-Property aktiv war.
        var f = new TextField
        {
            Width = 200, Height = 60,
            FontFamily = "Arial", FontSize = 18,
            Color = "#000000", CurrentText = "Hallo",
        };
        var layout = WarpPreviewService.RenderPreview(f);
        Assert.NotNull(layout);
    }

    [Fact]
    public void RenderPreview_ReturnsNull_WhenTextEmpty_EvenWithEffects()
    {
        // Leerer Text → nichts zu rendern. TextBox bleibt sichtbar als
        // Edit-Surface, damit der User tippen kann.
        var f = new TextField
        {
            Width = 100, Height = 40,
            CurrentText = "",
            StretchX = 2.0, // Effekt da, aber Text leer
        };
        Assert.Null(WarpPreviewService.RenderPreview(f));
    }

    [AvaloniaFact]
    public void RenderPreview_NonWarpedFieldWithStretchX_ProducesBitmap()
    {
        // BUG-Repro: Wenn das Feld nicht gewarpt ist, aber StretchX != 1,
        // muss der Editor trotzdem einen Preview rendern — sonst zeigt die
        // TextBox un-gestreckten Text, das exportierte PNG aber gestreckten,
        // und Editor ≠ Output. Vor dem Fix lieferte RenderPreview hier null.
        var f = new TextField
        {
            Width = 200, Height = 60,
            FontFamily = "Arial", FontSize = 18, FontWeight = "Bold",
            Color = "#000000", CurrentText = "I",
            HorizontalTextAlignment = "Left",
            VerticalTextAlignment = "Top",
            StretchX = 4.0,
        };
        var layout = WarpPreviewService.RenderPreview(f);
        Assert.NotNull(layout);
    }

    [AvaloniaFact]
    public void RenderPreview_NonWarpedFieldWithAutoFit_ProducesBitmap()
    {
        var f = new TextField
        {
            Width = 200, Height = 60,
            FontFamily = "Arial", FontSize = 12,
            Color = "#000000", CurrentText = "Auto",
            AutoFit = true,
        };
        Assert.NotNull(WarpPreviewService.RenderPreview(f));
    }

    [AvaloniaFact]
    public void RenderPreview_NonWarpedFieldWithLetterSpacing_ProducesBitmap()
    {
        var f = new TextField
        {
            Width = 200, Height = 40,
            FontFamily = "Arial", FontSize = 16,
            Color = "#000000", CurrentText = "Wide",
            LetterSpacing = 5,
        };
        Assert.NotNull(WarpPreviewService.RenderPreview(f));
    }

    [AvaloniaFact]
    public void RenderPreview_NonWarpedFieldWithLineHeight_ProducesBitmap()
    {
        var f = new TextField
        {
            Width = 200, Height = 80,
            FontFamily = "Arial", FontSize = 16,
            Color = "#000000", CurrentText = "Eins\nZwei",
            LineHeight = 30,
        };
        Assert.NotNull(WarpPreviewService.RenderPreview(f));
    }

    [Fact]
    public void HasRenderEffect_FlipsForEachRelevantProperty()
    {
        // Plain field → kein Effekt.
        Assert.False(WarpPreviewService.HasRenderEffect(new TextField()));

        // Warp-Offset → Effekt.
        Assert.True(WarpPreviewService.HasRenderEffect(new TextField { CornerNEdx = 5 }));

        // Stretch != 1 → Effekt.
        Assert.True(WarpPreviewService.HasRenderEffect(new TextField { StretchX = 2 }));
        Assert.True(WarpPreviewService.HasRenderEffect(new TextField { StretchY = 0.5 }));

        // AutoFit → Effekt.
        Assert.True(WarpPreviewService.HasRenderEffect(new TextField { AutoFit = true }));

        // LineHeight gesetzt (kein NaN) → Effekt.
        Assert.True(WarpPreviewService.HasRenderEffect(new TextField { LineHeight = 24 }));

        // LetterSpacing != 0 → Effekt.
        Assert.True(WarpPreviewService.HasRenderEffect(new TextField { LetterSpacing = 3 }));

        // Rotation alleine zählt NICHT — UserControl rotiert TextBox UND
        // Bitmap einheitlich, da gibt's keine Editor-vs-Export-Differenz.
        Assert.False(WarpPreviewService.HasRenderEffect(new TextField { Rotation = 30 }));
    }

    /// <summary>
    /// Editor ↔ Export-Konsistenz-Invariante: Wenn StretchX > 1 ist, MUSS der
    /// Editor-Preview den Text spürbar nach rechts ausdehnen — exakt da, wo
    /// auch der Export-Pfad ihn rendert. Der Test misst die rechteste dunkle
    /// Pixel-Spalte im Preview-Bitmap und verlangt, dass sie deutlich nach
    /// rechts wandert, sobald StretchX hochgesetzt wird. Bug-Repro: Vor dem
    /// Fix gab WarpPreviewService null zurück, dann lieferte HasRenderEffect=
    /// false und der Editor blieb bei der ungestreckten TextBox → Editor-
    /// Output und PNG-Output liefen auseinander.
    /// </summary>
    [AvaloniaFact]
    public void RenderPreview_HonorsBoldRanges_ProducesHeavierInkInBoldPortion()
    {
        // Editor-↔-Export-Konsistenz für BoldRanges: ein Feld mit "AAA"
        // (Range [0..3) bold) und "AAA" (alles non-bold) muss im Preview-
        // Bitmap auf der bold-Seite mehr "Tinte" zeigen als auf der non-bold-
        // Seite. Wir vergleichen pixel-counts und nehmen ≥10% Mehr-Tinte als
        // Untergrenze für den Bold-Effekt.
        var withBold = new TextField
        {
            Width = 200, Height = 40,
            FontFamily = "Arial", FontSize = 24, FontWeight = "Normal",
            Color = "#000000", CurrentText = "AAA",
            HorizontalTextAlignment = "Left",
            VerticalTextAlignment = "Top",
            BoldRanges = new List<BoldRange> { new() { Start = 0, Length = 3 } },
        };
        var withoutBold = new TextField
        {
            Width = 200, Height = 40,
            FontFamily = "Arial", FontSize = 24, FontWeight = "Normal",
            Color = "#000000", CurrentText = "AAA",
            HorizontalTextAlignment = "Left",
            VerticalTextAlignment = "Top",
        };
        var inkBold = CountDarkPixels(WarpPreviewService.RenderPreview(withBold)!.Bitmap);
        var inkNormal = CountDarkPixels(WarpPreviewService.RenderPreview(withoutBold)!.Bitmap);
        Assert.True(inkBold > inkNormal * 1.1,
            $"Bold-Variante muss spürbar mehr Tinte zeigen — bold={inkBold}, normal={inkNormal}.");
    }

    private static int CountDarkPixels(Bitmap bmp)
    {
        var size = bmp.PixelSize;
        var stride = size.Width * 4;
        var bufferLen = stride * size.Height;
        var buffer = new byte[bufferLen];
        unsafe
        {
            fixed (byte* p = buffer)
            {
                bmp.CopyPixels(
                    new PixelRect(0, 0, size.Width, size.Height),
                    (IntPtr)p,
                    bufferLen,
                    stride);
            }
        }
        var count = 0;
        for (var i = 0; i < bufferLen; i += 4)
        {
            var b = buffer[i + 0];
            var g = buffer[i + 1];
            var r = buffer[i + 2];
            var a = buffer[i + 3];
            if (a > 80 && r < 100 && g < 100 && b < 100) count++;
        }
        return count;
    }

    [AvaloniaFact]
    public void RenderPreview_StretchX_ShiftsTextRightEdge_RightwardInPreview()
    {
        var common = new
        {
            Width = 200.0, Height = 60.0,
            FontFamily = "Arial", FontSize = 18.0, FontWeight = "Bold",
            Color = "#000000", CurrentText = "I",
            HAlign = "Left", VAlign = "Top",
        };

        // Stretch=1 ist plain → kein Preview erzeugt. Wir nehmen einen
        // Mini-Marker (LetterSpacing=0, aber AutoFit=false), der den
        // Preview-Pfad nicht triggert. Stattdessen rendern wir Stretch=1
        // direkt über DrawTextField in eine eigene RTB als Referenz.
        var refField = new TextField
        {
            Width = common.Width, Height = common.Height,
            FontFamily = common.FontFamily, FontSize = common.FontSize, FontWeight = common.FontWeight,
            Color = common.Color, CurrentText = common.CurrentText,
            HorizontalTextAlignment = common.HAlign, VerticalTextAlignment = common.VAlign,
            StretchX = 1.0,
        };
        var rightmostRef = RenderRectangularAndFindRightmostDarkColumn(refField);

        var stretchedField = new TextField
        {
            Width = common.Width, Height = common.Height,
            FontFamily = common.FontFamily, FontSize = common.FontSize, FontWeight = common.FontWeight,
            Color = common.Color, CurrentText = common.CurrentText,
            HorizontalTextAlignment = common.HAlign, VerticalTextAlignment = common.VAlign,
            StretchX = 4.0,
        };
        var layout = WarpPreviewService.RenderPreview(stretchedField);
        Assert.NotNull(layout);
        var rightmostStretched = FindRightmostDarkColumn(layout!.Bitmap);

        Assert.True(rightmostStretched > rightmostRef + 10,
            $"Editor-Preview muss bei StretchX=4 deutlich weiter nach rechts reichen. " +
            $"Reference={rightmostRef}, Stretched={rightmostStretched} — wenn die beiden " +
            $"gleich auf liegen, wendet die Vorschau Stretch nicht an und der Editor zeigt " +
            $"etwas anderes als das exportierte PNG.");
    }

    private static int RenderRectangularAndFindRightmostDarkColumn(TextField field)
    {
        // Spiegel-Helper: rendert das Field genauso wie es Export & Preview
        // beide tun (rechtwinklig in W×H), und sucht die rechteste Spalte
        // mit einem dunklen Pixel.
        var w = (int)Math.Ceiling(field.Width);
        var h = (int)Math.Ceiling(field.Height);
        using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            CardTemplateEditor.Services.ExportService.DrawTextField(ctx, field);
        }
        return FindRightmostDarkColumn(rtb);
    }

    private static int FindRightmostDarkColumn(Bitmap bmp)
    {
        var size = bmp.PixelSize;
        var stride = size.Width * 4;
        var bufferLen = stride * size.Height;
        var buffer = new byte[bufferLen];
        unsafe
        {
            fixed (byte* p = buffer)
            {
                bmp.CopyPixels(
                    new PixelRect(0, 0, size.Width, size.Height),
                    (IntPtr)p,
                    bufferLen,
                    stride);
            }
        }
        var rightmost = -1;
        for (var x = 0; x < size.Width; x++)
        for (var y = 0; y < size.Height; y++)
        {
            var idx = y * stride + x * 4;
            var b = buffer[idx + 0];
            var g = buffer[idx + 1];
            var r = buffer[idx + 2];
            var a = buffer[idx + 3];
            if (a > 80 && r < 100 && g < 100 && b < 100) rightmost = Math.Max(rightmost, x);
        }
        return rightmost;
    }

    [Fact]
    public void RenderPreview_ReturnsNull_WhenTextEmpty()
    {
        var f = new TextField
        {
            Width = 100, Height = 40,
            CornerNWdx = 5, CornerNWdy = 5,
            CurrentText = "",
        };
        Assert.Null(WarpPreviewService.RenderPreview(f));
    }

    [AvaloniaFact]
    public void RenderPreview_ProducesBitmap_SizedToWarpedQuadBoundingBox()
    {
        // Eckpunkt-Offsets bleiben innerhalb des OuterPadding-Bereichs ⇒
        // die Bounding-Box der Quad-Eckpunkte liegt vollständig im
        // OuterRoot-Rechteck. Wir prüfen nur, dass das Bitmap mindestens
        // die Modell-Box-Größe abdeckt — die exakte Größe hängt von
        // SafetyMargin und Quad-BoundingBox ab.
        var f = new TextField
        {
            X = 50, Y = 60, Width = 120, Height = 40,
            FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
            Color = "#000000", CurrentText = "ABC",
            HorizontalTextAlignment = "Center",
            VerticalTextAlignment = "Center",
            CornerNWdx = 10, CornerNWdy = 5,
            CornerSEdx = -10, CornerSEdy = -5,
        };

        var layout = WarpPreviewService.RenderPreview(f);
        Assert.NotNull(layout);
        Assert.True(layout!.Bitmap.PixelSize.Width >= (int)f.Width,
            $"Bitmap zu schmal: {layout.Bitmap.PixelSize.Width} < {f.Width}.");
        Assert.True(layout.Bitmap.PixelSize.Height >= (int)f.Height,
            $"Bitmap zu niedrig: {layout.Bitmap.PixelSize.Height} < {f.Height}.");
    }

    [AvaloniaFact]
    public void RenderPreview_GrowsBitmap_AndShiftsOffset_WhenCornerLeavesOuterRoot()
    {
        // NW-Eckpunkt 100px nach links/oben gezogen ⇒ Quad ragt deutlich
        // über die OuterRoot-Top-Left-Kante hinaus. Die Bitmap muss
        // entsprechend vergrößert und der Offset negativ sein, damit der
        // Editor den verzerrten Text nicht abschneidet.
        var f = new TextField
        {
            Width = 100, Height = 40,
            FontFamily = "Arial", FontSize = 18,
            Color = "#000000", CurrentText = "Test",
            CornerNWdx = -100,
            CornerNWdy = -100,
        };

        var layout = WarpPreviewService.RenderPreview(f);
        Assert.NotNull(layout);
        Assert.True(layout!.OffsetX < 0,
            $"OffsetX muss negativ sein, war {layout.OffsetX}.");
        Assert.True(layout.OffsetY < 0,
            $"OffsetY muss negativ sein, war {layout.OffsetY}.");
    }

    [AvaloniaFact]
    public void RenderPreview_ContainsTextPixels_InWarpedRegion()
    {
        // Sanity-Check: nach dem Warp muss IRGENDWO im Bitmap mindestens ein
        // dunkles (Text-)Pixel erkennbar sein. Wir prüfen das Alpha != 0 +
        // dunkler RGB-Wert. Genauere Pixel-Position wäre vom Font-Layout
        // abhängig und damit fragil.
        var f = new TextField
        {
            Width = 200, Height = 80,
            FontFamily = "Arial", FontSize = 48, FontWeight = "Bold",
            Color = "#000000", CurrentText = "WWWW",
            HorizontalTextAlignment = "Center",
            VerticalTextAlignment = "Center",
            CornerNWdx = 30,
            CornerSEdy = 20,
        };
        var layout = WarpPreviewService.RenderPreview(f);
        Assert.NotNull(layout);
        var bmp = layout!.Bitmap;

        var size = bmp.PixelSize;
        var stride = size.Width * 4;
        var bufferLen = stride * size.Height;
        var buffer = new byte[bufferLen];
        unsafe
        {
            fixed (byte* p = buffer)
            {
                bmp.CopyPixels(
                    new PixelRect(0, 0, size.Width, size.Height),
                    (IntPtr)p,
                    bufferLen,
                    stride);
            }
        }

        var foundDark = false;
        for (var y = 0; y < size.Height && !foundDark; y++)
        for (var x = 0; x < size.Width && !foundDark; x++)
        {
            var idx = y * stride + x * 4;
            // BGRA-Layout: B G R A
            var b = buffer[idx + 0];
            var g = buffer[idx + 1];
            var r = buffer[idx + 2];
            var a = buffer[idx + 3];
            if (a > 80 && r < 80 && g < 80 && b < 80) foundDark = true;
        }
        Assert.True(foundDark,
            "Warp-Vorschau-Bitmap muss mindestens ein dunkles Text-Pixel enthalten.");
    }

    [AvaloniaFact]
    public void RenderPreview_TransparentOutsideWarpedQuad()
    {
        // Ecke (0,0) des Bitmaps liegt im SafetyMargin-Streifen vor der
        // Quad-Bounding-Box. Pixel dort muss vollständig transparent sein —
        // Quad zeichnet erst innerhalb der Bounding-Box.
        var f = new TextField
        {
            Width = 100, Height = 40,
            FontFamily = "Arial", FontSize = 18,
            Color = "#000000", CurrentText = "Hallo",
            CornerNEdx = 10,
        };
        var layout = WarpPreviewService.RenderPreview(f);
        Assert.NotNull(layout);
        var bmp = layout!.Bitmap;

        var stride = bmp.PixelSize.Width * 4;
        var bufferLen = stride * bmp.PixelSize.Height;
        var buffer = new byte[bufferLen];
        unsafe
        {
            fixed (byte* p = buffer)
            {
                bmp.CopyPixels(
                    new PixelRect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height),
                    (IntPtr)p,
                    bufferLen,
                    stride);
            }
        }
        // Pixel (0,0) ganz oben links:
        Assert.Equal(0, buffer[3]);
    }
}
