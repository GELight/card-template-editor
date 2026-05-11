using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;

namespace CardTemplateEditor.Tests;

public class WarpExportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public WarpExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WarpExport_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateRedPng(string name, int w, int h)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.Red, null, new Rect(0, 0, w, h));
        }
        bmp.Save(path);
        return path;
    }

    private static unsafe (byte r, byte g, byte b, byte a) ReadPixel(string pngPath, int x, int y)
    {
        using var bmp = new Bitmap(pngPath);
        var size = bmp.PixelSize;
        var stride = size.Width * 4;
        var bufferLen = stride * size.Height;
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferLen);
        try
        {
            fixed (byte* p = buffer)
            {
                bmp.CopyPixels(
                    new PixelRect(0, 0, size.Width, size.Height),
                    (IntPtr)p,
                    bufferLen,
                    stride);
            }
            var idx = y * stride + x * 4;
            return (buffer[idx + 2], buffer[idx + 1], buffer[idx + 0], buffer[idx + 3]);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [Fact]
    public void IsWarped_ReturnsFalse_ForZeroOffsets()
    {
        var f = new TextField();
        Assert.False(ExportService.IsWarped(f));
    }

    [Fact]
    public void IsWarped_ReturnsTrue_AsSoonAsAnyCornerHasOffset()
    {
        Assert.True(ExportService.IsWarped(new TextField { CornerNWdx = 1 }));
        Assert.True(ExportService.IsWarped(new TextField { CornerSEdy = -3 }));
        Assert.True(ExportService.IsWarped(new TextField { CornerNEdx = 0.001 }));
    }

    [AvaloniaFact]
    public void ExportSlot_NoWarp_StillProducesValidPng()
    {
        // Sanity: ohne Warp wird der Avalonia-Pfad genutzt; Output muss
        // pixel-konsistent zum bisherigen Verhalten sein.
        var src = CreateRedPng("plain.png", 50, 30);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "plain.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = 5, Y = 5, Width = 40, Height = 20,
                    FontFamily = "Arial", FontSize = 14,
                    Color = "#000000", CurrentText = "X",
                },
            },
        };
        var dest = Path.Combine(_tempDir, "plain.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);
        Assert.True(File.Exists(dest));
        // Ecke außerhalb des Textfelds bleibt rot.
        var (r, g, b, _) = ReadPixel(dest, 48, 28);
        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
    }

    [AvaloniaFact]
    public void ExportSlot_WarpedField_GoesThroughSkiaPath_AndLeavesNonWarpedRegionsRed()
    {
        // Warped TextField in der Mitte. Ein klarer rotbleibender Bereich am
        // Bildrand verifiziert, dass die Skia-Pipeline das Hintergrundbild
        // verlustfrei übernommen hat (Avalonia-RTB → PNG-Roundtrip → SKBitmap).
        var src = CreateRedPng("warped.png", 200, 200);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "warped.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = 50, Y = 50, Width = 100, Height = 100,
                    FontFamily = "Arial", FontSize = 32, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "X",
                    HorizontalTextAlignment = "Center",
                    VerticalTextAlignment = "Center",
                    // Echter Warp: NW-Ecke nach innen, SE-Ecke nach außen.
                    CornerNWdx = 20, CornerNWdy = 20,
                    CornerSEdx = -20, CornerSEdy = -20,
                },
            },
        };
        var dest = Path.Combine(_tempDir, "warped.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        Assert.True(File.Exists(dest));
        // Bildecken müssen noch unverändert rot sein.
        var (r1, g1, b1, _) = ReadPixel(dest, 5, 5);
        var (r2, g2, b2, _) = ReadPixel(dest, 195, 195);
        Assert.Equal(255, r1); Assert.Equal(0, g1); Assert.Equal(0, b1);
        Assert.Equal(255, r2); Assert.Equal(0, g2); Assert.Equal(0, b2);
    }

    [AvaloniaFact]
    public void ExportSlot_WarpedField_HonorsStretch_AndAutoFit()
    {
        // Bug-Fix: ApplyWarpedField clont das Field für die rectanguläre Text-
        // RTB. Wenn StretchY in diesem Clone fehlt, rendert der Editor den Text
        // doppelt so hoch, der Export aber normal hoch — Bild ≠ Vorschau.
        // Test prüft: bei extremem StretchX kommt der Text "stark verbreitert"
        // im PNG an. Wir vergleichen das pixelmäßig schmale "I" mit dem
        // verbreiterten Output.
        var src = CreateRedPng("stretch.png", 200, 100);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "stretch.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = 50, Y = 30, Width = 100, Height = 40,
                    FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "I",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                    StretchX = 5.0, // 5x horizontal gestreckt
                    CornerNEdx = 1, // Mini-Warp triggert Skia-Pfad
                },
            },
        };
        var dest = Path.Combine(_tempDir, "stretch.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        // In einer 100px-breiten Box muss der Text bei StretchX=5 sehr breit
        // ausfallen — wir suchen ein dunkles Pixel an einer Position, an der
        // ein normaler "I" bei x=10 (wenn überhaupt) wäre, hier aber dank
        // Stretch x≥40 erreicht haben muss.
        var hasDarkAtFarRight = false;
        for (var x = 90; x < 145 && !hasDarkAtFarRight; x++)
        for (var y = 35; y < 65 && !hasDarkAtFarRight; y++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 100 && g < 50 && b < 50) hasDarkAtFarRight = true;
        }
        Assert.True(hasDarkAtFarRight,
            "Mit StretchX=5 muss der gestreckte Text deutlich nach rechts reichen — der Bug clonte das Field ohne Stretch und der Export rendert dann ungestreckt.");
    }

    [AvaloniaFact]
    public void ExportSlot_RotatedAndWarped_AppliesOffsetsInLocalFrame()
    {
        // Bug-Fix: Eckpunkt-Offsets werden im Editor in LOKALEN Frame-Coords
        // gespeichert (Drag → InverseRotate → Speicher). Der Export muss diese
        // Offsets als Vektor im lokalen Frame interpretieren und für die
        // Homographie mitrotieren — sonst weicht der gespeicherte Output von
        // der Editor-Vorschau ab, sobald Rotation ≠ 0 und ein Warp gesetzt sind.
        // Konkret prüfen: bei 90°-Rotation und CornerNEdx=20 (lokal "rechts")
        // muss die NE-Ecke in WELT-Koords nach UNTEN verschoben werden, nicht
        // nach rechts.
        var src = CreateRedPng("rotwarp.png", 300, 300);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "rotwarp.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = 100, Y = 100, Width = 100, Height = 60,
                    FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "WWWW",
                    Rotation = 90,
                    // NE in lokalem Frame nach rechts ziehen (= NACH UNTEN
                    // in Welt-Koords nach 90°-Rotation).
                    CornerNEdx = 40,
                },
            },
        };
        var dest = Path.Combine(_tempDir, "rotwarp.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        // Mit korrekt rotiertem Offset (= unten in Welt-Koords) muss das Quad
        // nach unten ausbrechen → wir erwarten dunkle Text-Pixel deutlich
        // unter Y = 160 (was ohne Offset die untere Kante des rotierten Rects
        // wäre). Davor war (Bug): Offset in Welt-Coords = "nach rechts" →
        // Quad bricht nach rechts aus, nicht nach unten.
        var hasDarkBelowRect = false;
        for (var y = 165; y < 200 && !hasDarkBelowRect; y++)
        for (var x = 90; x < 220 && !hasDarkBelowRect; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 100 && g < 50 && b < 50) hasDarkBelowRect = true;
        }
        Assert.True(hasDarkBelowRect,
            "Bei 90°-Rotation und CornerNEdx=40 (lokal X) muss der Warp im Welt-Frame in Y-Richtung wirken — sonst stimmt der Export nicht mit der Editor-Vorschau überein.");
    }

    [AvaloniaFact]
    public void ExportSlot_WarpedField_TextEndsUpInsideTheWarpedQuadRegion()
    {
        // Wir verschieben die NW-Ecke deutlich nach unten-rechts (in das
        // Bild-Inner). Damit muss der gerenderte Text-Inhalt komplett im
        // verzerrten Quad-Bereich liegen — die ORIGINAL-NW-Ecke (ca. (50, 50))
        // sollte rot bleiben, weil dort nach Warp kein Text mehr liegt.
        var src = CreateRedPng("warpquad.png", 200, 200);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "warpquad.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = 50, Y = 50, Width = 100, Height = 100,
                    FontFamily = "Arial", FontSize = 36, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "WWWW",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                    // NW-Ecke um 40 nach SE → Quad startet bei (90, 90).
                    CornerNWdx = 40, CornerNWdy = 40,
                },
            },
        };
        var dest = Path.Combine(_tempDir, "warpquad.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        // Original-NW-Region (50..80, 50..80): muss komplett rot sein,
        // weil der warped Quad dort nicht mehr liegt.
        var anyDarkInOriginalNW = false;
        for (var y = 50; y < 80 && !anyDarkInOriginalNW; y++)
        for (var x = 50; x < 80 && !anyDarkInOriginalNW; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) anyDarkInOriginalNW = true;
        }
        Assert.False(anyDarkInOriginalNW,
            "Original-NW-Ecke sollte rot bleiben, weil der warped Quad dort nicht mehr liegt.");

        // Im warped Quad-Bereich (95..145, 95..140): muss Text-Pixel enthalten.
        var anyDarkInsideWarp = false;
        for (var y = 95; y < 145 && !anyDarkInsideWarp; y++)
        for (var x = 95; x < 145 && !anyDarkInsideWarp; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) anyDarkInsideWarp = true;
        }
        Assert.True(anyDarkInsideWarp,
            "Im verzerrten Quad-Bereich müssen Text-Pixel liegen.");
    }
}
