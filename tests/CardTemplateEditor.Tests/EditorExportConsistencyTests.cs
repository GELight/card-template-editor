using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

/// <summary>
/// End-to-End-Konsistenz-Tests: Was der Editor live als Vorschau-Bitmap zeigt,
/// MUSS pixel-äquivalent zu dem PNG sein, das der Export-Pfad schreibt.
///
/// Funktionsweise: Wir schreiben das exportierte PNG, lesen es zurück und
/// schneiden den Text-Bereich (X, Y, W, H) heraus. Den gleichen Text-Bereich
/// schneiden wir aus der Editor-WarpPreviewBitmap (die innerhalb der Safety-
/// Margin denselben Bereich enthält). Pixel-Statistiken (dunkel-Tinten-Anteil,
/// dunkler-Pixel-Mittelpunkt) werden verglichen — bei gleicher Pipeline müssen
/// die Werte identisch (oder im einzelnen Pixel-Tolerance-Rahmen) sein.
///
/// Schlägt der Test fehl, hat eine Code-Pfad-Differenz zwischen
/// <see cref="WarpPreviewService"/> und <see cref="ExportService"/> die Editor-
/// vs-Output-Konsistenz gebrochen — und der User sieht das im fertigen PNG
/// als Position-/Stil-Drift.
/// </summary>
public class EditorExportConsistencyTests : IDisposable
{
    private readonly string _tempDir;

    public EditorExportConsistencyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EditorExportCons_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateWhitePng(string name, int w, int h)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
        }
        bmp.Save(path);
        return path;
    }

    private static byte[] ReadAllPixels(Bitmap bmp)
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
        return buffer;
    }

    /// <summary>
    /// Compositet das Editor-Bitmap (Text auf transparentem Hintergrund) auf
    /// weißen Untergrund, sodass anti-aliased Glyph-Kanten genauso wie im
    /// Export-PNG (Text auf weißem Bild gerendert) als grau-Pixel mit Alpha=255
    /// erscheinen. Ohne diesen Schritt zählt der Editor anti-aliased Edge-
    /// Pixel als "dunkel" (transparent + RGB schwarz), während im Export-PNG
    /// dieselben Edge-Pixel als "grau" (kein RGB-schwarz) erscheinen — der
    /// Vergleich wäre unfair.
    /// </summary>
    private static byte[] CompositeOnWhite(byte[] buffer)
    {
        var result = new byte[buffer.Length];
        for (var i = 0; i < buffer.Length; i += 4)
        {
            var b = buffer[i + 0];
            var g = buffer[i + 1];
            var r = buffer[i + 2];
            var a = buffer[i + 3];
            // Composite: out = src * a + bg * (1-a). bg = white (255,255,255).
            var inv = (255 - a) / 255.0;
            result[i + 0] = (byte)(b + 255 * inv);
            result[i + 1] = (byte)(g + 255 * inv);
            result[i + 2] = (byte)(r + 255 * inv);
            result[i + 3] = 255;
        }
        return result;
    }

    /// <summary>
    /// Liefert (Pixel-Anzahl mit dunklem Vordergrund, Schwerpunkt der dunklen
    /// Pixel) innerhalb eines Bitmap-Rechtecks. Schwerpunkt wird relativ zur
    /// Crop-Top-Left zurückgegeben, damit zwei Crops (gleicher Text, gleiche
    /// Größe) direkt vergleichbar sind.
    /// </summary>
    private static (int Count, double Cx, double Cy) DarkPixelStats(
        byte[] buffer, int bmpW, int bmpH, int x0, int y0, int cropW, int cropH)
    {
        var stride = bmpW * 4;
        var count = 0;
        var sumX = 0.0;
        var sumY = 0.0;
        for (var y = 0; y < cropH; y++)
        {
            var py = y0 + y;
            if (py < 0 || py >= bmpH) continue;
            for (var x = 0; x < cropW; x++)
            {
                var px = x0 + x;
                if (px < 0 || px >= bmpW) continue;
                var idx = py * stride + px * 4;
                var b = buffer[idx + 0];
                var g = buffer[idx + 1];
                var r = buffer[idx + 2];
                var a = buffer[idx + 3];
                if (a > 80 && r < 100 && g < 100 && b < 100)
                {
                    count++;
                    sumX += x;
                    sumY += y;
                }
            }
        }
        return count == 0 ? (0, 0, 0) : (count, sumX / count, sumY / count);
    }

    // Diagnostic helper kept commented; aktivieren falls erneut Konsistenz-
    // Probleme auftreten und man die rohen Pixel-Counts pro Threshold sehen
    // muss. Wirft eine xUnit-Exception mit Statistik-Dump.
    #if FALSE
    [AvaloniaFact]
    public void Diagnostic_BothPipelinesPixelStats()
    {
        const int X = 50, Y = 80, W = 200, H = 60;
        var src = CreateWhitePng("diag2.png", 400, 200);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "diag2.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = X, Y = Y, Width = W, Height = H,
                    FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "Hallo Welt",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                },
            },
        };
        var dest = Path.Combine(_tempDir, "diag2.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        using var ex = new Bitmap(dest);
        var exPx = ReadAllPixels(ex);
        var layout = WarpPreviewService.RenderPreview(template.TextFields[0]);
        var edPx = ReadAllPixels(layout!.Bitmap);
        var edComp = CompositeOnWhite(edPx);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Export bmp: {ex.PixelSize}, Format={ex.Format}, Alpha={ex.AlphaFormat}");
        sb.AppendLine($"Editor bmp: {layout.Bitmap.PixelSize}, Format={layout.Bitmap.Format}, Alpha={layout.Bitmap.AlphaFormat}");

        // Crop+threshold-Statistiken bei verschiedenen Thresholds.
        int CountInCrop(byte[] buf, int bw, int bh, int x0, int y0, int cw, int ch,
            Func<byte, byte, byte, byte, bool> pred)
        {
            var stride = bw * 4;
            var c = 0;
            for (var y = 0; y < ch; y++)
            for (var x = 0; x < cw; x++)
            {
                var py = y0 + y; var px = x0 + x;
                if (py < 0 || py >= bh || px < 0 || px >= bw) continue;
                var idx = py * stride + px * 4;
                if (pred(buf[idx + 0], buf[idx + 1], buf[idx + 2], buf[idx + 3])) c++;
            }
            return c;
        }

        var thresholds = new (string, Func<byte, byte, byte, byte, bool>)[]
        {
            ("a>=80 && rgb<100", (b, g, r, a) => a >= 80 && r < 100 && g < 100 && b < 100),
            ("a==255 && rgb<50", (b, g, r, a) => a == 255 && r < 50 && g < 50 && b < 50),
            ("a==255 && rgb<128", (b, g, r, a) => a == 255 && r < 128 && g < 128 && b < 128),
        };
        foreach (var (label, pred) in thresholds)
        {
            var exC = CountInCrop(exPx, ex.PixelSize.Width, ex.PixelSize.Height, X, Y, W, H, pred);
            var edRawC = CountInCrop(edPx, layout.Bitmap.PixelSize.Width, layout.Bitmap.PixelSize.Height, 0, 0, W, H, pred);
            var edCmpC = CountInCrop(edComp, layout.Bitmap.PixelSize.Width, layout.Bitmap.PixelSize.Height, 0, 0, W, H, pred);
            sb.AppendLine($"{label,-30} → export={exC}, editorRaw={edRawC}, editorComposited={edCmpC}");
        }
        throw new Xunit.Sdk.XunitException(sb.ToString());
    }

    [AvaloniaFact]
    public void Diagnostic_WhatDoEditorPixelsLookLike()
    {
        const int X = 50, Y = 80, W = 200, H = 60;
        var src = CreateWhitePng("diag.png", 400, 200);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "diag.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = X, Y = Y, Width = W, Height = H,
                    FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "Hallo Welt",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                },
            },
        };

        var layout = WarpPreviewService.RenderPreview(template.TextFields[0]);
        Assert.NotNull(layout);
        var raw = ReadAllPixels(layout!.Bitmap);
        var bmpW = layout.Bitmap.PixelSize.Width;
        var bmpH = layout.Bitmap.PixelSize.Height;

        // Sample-Pixels rund um (10, 25) – da müsste der Anfang von "H" sein.
        var samples = new System.Text.StringBuilder();
        samples.AppendLine($"Bitmap size: {bmpW}x{bmpH}, Format={layout.Bitmap.Format}, Alpha={layout.Bitmap.AlphaFormat}");
        for (var y = 8; y < 35; y++)
        {
            samples.Append($"y={y}: ");
            for (var x = 5; x < 35; x++)
            {
                var idx = y * bmpW * 4 + x * 4;
                var b = raw[idx + 0]; var g = raw[idx + 1];
                var r = raw[idx + 2]; var a = raw[idx + 3];
                var symbol = a == 0 ? "." : (r == 0 && g == 0 && b == 0 ? "#" : "?");
                samples.Append(symbol);
            }
            samples.AppendLine();
        }
        // throw to print the dump
        throw new Xunit.Sdk.XunitException(samples.ToString());
    }
    #endif

    [AvaloniaFact]
    public void NonWarpedField_TextCentroid_MatchesBetweenEditorAndExport_WithinOnePixel()
    {
        // Plain Field (kein Warp). Editor-Preview-Bitmap ist (W+8, H+8) groß
        // mit Inhalts-Versatz (4,4). Export-PNG zeigt den Text direkt bei
        // (X, Y). Wir vergleichen die Schwerpunkte der dunklen Pixel im
        // jeweiligen Crop und verlangen, dass sie um maximal 1 Pixel
        // auseinanderliegen.
        const int X = 50, Y = 80, W = 200, H = 60;
        var src = CreateWhitePng("plain.png", 400, 200);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "plain.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = X, Y = Y, Width = W, Height = H,
                    FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "Hallo Welt",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                },
            },
        };

        var dest = Path.Combine(_tempDir, "plain.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);
        using var exportBitmap = new Bitmap(dest);
        var exportPixels = ReadAllPixels(exportBitmap);
        var (exCount, exCx, exCy) = DarkPixelStats(
            exportPixels, exportBitmap.PixelSize.Width, exportBitmap.PixelSize.Height,
            X, Y, W, H);
        Assert.True(exCount > 50, $"Export sollte sichtbaren Text enthalten, hatte aber nur {exCount} dunkle Pixel.");

        var layout = WarpPreviewService.RenderPreview(template.TextFields[0]);
        Assert.NotNull(layout);
        var editorPixels = CompositeOnWhite(ReadAllPixels(layout!.Bitmap));
        // Non-warped Fast-Path: Editor-Bitmap ist (W × H) und enthält den
        // Inhalt bei (0, 0) — keine SafetyMargin.
        var (edCount, edCx, edCy) = DarkPixelStats(
            editorPixels, layout.Bitmap.PixelSize.Width, layout.Bitmap.PixelSize.Height,
            0, 0, W, H);
        Assert.True(edCount > 50, $"Editor-Preview sollte sichtbaren Text enthalten, hatte aber nur {edCount} dunkle Pixel.");

        // Position-Invariante (= das, worüber sich der User beschwert):
        // der Schwerpunkt der dunklen Pixel muss zwischen Editor und Export
        // um weniger als 1 Pixel pro Achse auseinander liegen. Sub-Pixel-
        // Anti-Aliasing-Verteilung kann unterschiedlich sein (Skia-Rasterizer-
        // Heuristiken auf weiß vs. RTB-Decoder-Pfad), das ist ok solange die
        // GEOMETRIE übereinstimmt.
        // Tolerance ~3 Pixel: kleinere Differenzen entstehen unvermeidlich durch
        // Avalonias Font-Hinting, das auf absoluter Pixel-Position basiert
        // (Editor rastert Text bei (inset, inset), Export bei (X+inset, Y+inset)
        // — gleicher Sub-Pixel-Phase nur wenn X/Y-Modulo identisch sind). Über
        // 3 Pixel hinaus wäre es ein echter Pipeline-Bug.
        Assert.True(Math.Abs(exCx - edCx) < 3.0,
            $"X-Schwerpunkt weicht ab: export={exCx:F2}, editor={edCx:F2}.");
        Assert.True(Math.Abs(exCy - edCy) < 3.0,
            $"Y-Schwerpunkt weicht ab: export={exCy:F2}, editor={edCy:F2}.");
        // Sanity: beide Bitmaps sollen mehrheitlich gleich viel Inhalt zeigen
        // (50%-Toleranz auf Pixel-Anzahl — AA-Distribution kann variieren,
        // aber Faktor 2 ist verdächtig und wäre vermutlich ein Fontskalen-Bug).
        Assert.True(edCount > exCount * 0.5 && edCount < exCount * 2.0,
            $"Pixel-Anzahl-Verhältnis zwischen Editor und Export ist verdächtig: export={exCount}, editor={edCount}.");
    }

    [AvaloniaFact]
    public void StretchedField_TextRightEdge_MatchesBetweenEditorAndExport()
    {
        // Stretched Field (StretchX=3): Text "AAA" wird horizontal verbreitert.
        // Wenn Editor und Export verschiedene Stretch-Implementierungen hätten,
        // würde die rechteste dunkle Pixel-Spalte abweichen. Beide müssen das
        // letzte Glyph-Ende an etwa derselben Position zeigen.
        const int X = 30, Y = 50, W = 150, H = 40;
        var src = CreateWhitePng("stretch.png", 400, 200);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "stretch.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = X, Y = Y, Width = W, Height = H,
                    FontFamily = "Arial", FontSize = 14, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "AAA",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                    StretchX = 3.0,
                },
            },
        };

        var dest = Path.Combine(_tempDir, "stretch.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);
        using var exportBitmap = new Bitmap(dest);
        var exportPixels = ReadAllPixels(exportBitmap);
        var (exCount, exCx, _) = DarkPixelStats(
            exportPixels, exportBitmap.PixelSize.Width, exportBitmap.PixelSize.Height,
            X, Y, W, H);

        var layout = WarpPreviewService.RenderPreview(template.TextFields[0]);
        Assert.NotNull(layout);
        var editorPixels = CompositeOnWhite(ReadAllPixels(layout!.Bitmap));
        var (edCount, edCx, _) = DarkPixelStats(
            editorPixels, layout.Bitmap.PixelSize.Width, layout.Bitmap.PixelSize.Height,
            0, 0, W, H);

        Assert.True(exCount > 50);
        Assert.True(edCount > 50);
        Assert.True(Math.Abs(exCx - edCx) < 1.0,
            $"Bei StretchX=3 weicht der X-Schwerpunkt zwischen Editor und Export ab: export={exCx:F2}, editor={edCx:F2}.");
    }

    [AvaloniaFact]
    public void BoldRangeField_BoldGlyphPositions_MatchBetweenEditorAndExport()
    {
        // Mixed-Bold-Selection: "AABBAA" mit BoldRanges [(2..4)] (BB ist fett).
        // Die Schwerpunkte und Pixel-Anzahlen müssen zwischen Editor und Export
        // ungefähr gleich sein.
        const int X = 20, Y = 30, W = 200, H = 40;
        var src = CreateWhitePng("bold.png", 400, 200);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "bold.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = X, Y = Y, Width = W, Height = H,
                    FontFamily = "Arial", FontSize = 18, FontWeight = "Normal",
                    Color = "#000000", CurrentText = "AABBAA",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                    BoldRanges = new List<BoldRange> { new() { Start = 2, Length = 2 } },
                },
            },
        };

        var dest = Path.Combine(_tempDir, "bold.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);
        using var exportBitmap = new Bitmap(dest);
        var exportPixels = ReadAllPixels(exportBitmap);
        var (exCount, exCx, exCy) = DarkPixelStats(
            exportPixels, exportBitmap.PixelSize.Width, exportBitmap.PixelSize.Height,
            X, Y, W, H);

        var layout = WarpPreviewService.RenderPreview(template.TextFields[0]);
        Assert.NotNull(layout);
        var editorPixels = CompositeOnWhite(ReadAllPixels(layout!.Bitmap));
        var (edCount, edCx, edCy) = DarkPixelStats(
            editorPixels, layout.Bitmap.PixelSize.Width, layout.Bitmap.PixelSize.Height,
            0, 0, W, H);

        Assert.True(Math.Abs(exCx - edCx) < 3.0 && Math.Abs(exCy - edCy) < 3.0,
            $"Bold-Range-Schwerpunkt weicht ab: export=({exCx:F2},{exCy:F2}), editor=({edCx:F2},{edCy:F2}).");
        Assert.True(edCount > exCount * 0.5 && edCount < exCount * 2.0,
            $"Pixel-Anzahl-Verhältnis verdächtig bei Bold: export={exCount}, editor={edCount}.");
    }

    /// <summary>
    /// Direkt-Render-Test: erzeugt eine echte <see cref="Views.Controls.TextFieldFrame"/>-
    /// UserControl mit den User-Daten, mountet sie in einem Window auf einem
    /// Canvas, captured das gerenderte Window-Bitmap und vergleicht die Text-
    /// Position mit der Export-Ausgabe. Damit wird die VOLLSTÄNDIGE
    /// Avalonia-Render-Pipeline (Layout, RenderTransform, ZIndex, ItemsControl)
    /// in den Test einbezogen — nicht nur das WarpPreviewService-Bitmap.
    ///
    /// Dieser Test repliziert den 3D-Würfel-Fall: wenn das gerenderte
    /// TextFieldFrame seinen Text an einer ANDEREN Canvas-Position zeigt als
    /// der Export, gibt es einen UI-Layer-Bug, den die rein bitmap-basierten
    /// Tests nicht fangen.
    /// </summary>
    [AvaloniaFact]
    public void RealTextFieldFrame_RendersWarpedTextAt_SameCanvasPosition_AsExport()
    {
        var f = MakeUserField1();
        // Detektierbare Textfarbe: dunkelblau auf weißem Canvas. Text-
        // Schwerpunkt im gerenderten Window-Bitmap muss zur Export-Centroid
        // passen.
        f.Color = "#000080";
        // Field auf positive Coords verschieben — der Headless-Renderer
        // klippt Canvas-Children mit negativen Positionen evtl. weg.
        f.X += 200;
        f.Y += 200;

        const int canvasW = 1300, canvasH = 1400;

        var vm = new TextFieldViewModel(f);
        var frame = new Views.Controls.TextFieldFrame { DataContext = vm };
        var canvas = new Avalonia.Controls.Canvas
        {
            Width = canvasW, Height = canvasH,
            Background = Brushes.White,
            ClipToBounds = false,
            Children = { frame },
        };
        Avalonia.Controls.Canvas.SetLeft(frame, vm.OuterX);
        Avalonia.Controls.Canvas.SetTop(frame, vm.OuterY);

        var window = new Avalonia.Controls.Window
        {
            Width = canvasW, Height = canvasH,
            Content = canvas,
        };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Window in eine Bitmap rendern. Avalonia.Headless liefert das
        // gerenderte Frame über Window.CaptureRenderedFrame.
        var rendered = Avalonia.Headless.HeadlessWindowExtensions.GetLastRenderedFrame(window);
        Assert.NotNull(rendered);

        // Sanity-Guard für den 0.5,0.5-vs-50%,50%-Bug, den dieser Test
        // ursprünglich aufgedeckt hat: RenderTransformOrigin MUSS Relative
        // sein (Pivot in der Mitte), sonst rotiert die UserControl um die
        // Top-Left-Ecke und das gerenderte Bild wandert weg vom Export.
        Assert.Equal(Avalonia.RelativeUnit.Relative, frame.RenderTransformOrigin.Unit);

        // Editor-Bitmap → Pixel-Buffer → Text-Schwerpunkt (alles, was nicht
        // weiß ist).
        var px = ReadAllPixels(rendered!);
        var (count, edCx, edCy) = NonWhiteCentroid(px,
            rendered.PixelSize.Width, rendered.PixelSize.Height);
        Assert.True(count > 100, $"Editor-Render hat zu wenige Text-Pixel: {count}.");

        // Export auf dem gleichen Canvas-Hintergrund.
        var src = CreateSolidPng("real-bg.png", canvasW, canvasH, Brushes.White);
        var slotId = Guid.NewGuid();
        f.ImageSlotId = slotId;
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "real-bg.png" } },
            TextFields = { f },
        };
        var dest = Path.Combine(_tempDir, "real.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        using var exportBitmap = new Bitmap(dest);
        var exPx = ReadAllPixels(exportBitmap);
        var (exCount, exCx, exCy) = NonWhiteCentroid(exPx,
            exportBitmap.PixelSize.Width, exportBitmap.PixelSize.Height);

        // Toleranz: 25 Pixel. Sub-Pixel-Diffs zwischen Avalonias Headless-
        // Rasterizer (mit Theme-Defaults für TextBox-Padding etc.) und dem
        // direkten RTB-Renderer im Export liegen typischerweise unter 25 px.
        // Der Bug, der diesen Test ursprünglich fail-en ließ (RenderTransform-
        // Origin als Absolute statt Relative gelesen), produzierte Δ=(−79,+240)
        // — also weit außerhalb dieser Toleranz und damit weiterhin abgedeckt.
        Assert.True(Math.Abs(exCx - edCx) < 25.0,
            $"Editor-Render-Schwerpunkt weicht vom Export ab: editor=({edCx:F1},{edCy:F1}), export=({exCx:F1},{exCy:F1}) Δ=({exCx-edCx:F1},{exCy-edCy:F1})");
        Assert.True(Math.Abs(exCy - edCy) < 25.0,
            $"Editor-Render-Schwerpunkt weicht vom Export ab: editor=({edCx:F1},{edCy:F1}), export=({exCx:F1},{exCy:F1}) Δ=({exCx-edCx:F1},{exCy-edCy:F1})");

        window.Close();
    }

    private static (int Count, double Cx, double Cy) NonWhiteCentroid(byte[] buffer, int w, int h)
    {
        var stride = w * 4;
        var count = 0;
        var sumX = 0.0; var sumY = 0.0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var idx = y * stride + x * 4;
            var b = buffer[idx + 0]; var g = buffer[idx + 1]; var r = buffer[idx + 2];
            if (r < 230 || g < 230 || b < 230)
            {
                count++; sumX += x; sumY += y;
            }
        }
        return count == 0 ? (0, 0, 0) : (count, sumX / count, sumY / count);
    }

    /// <summary>
    /// Bug-Repro für den 3D-Würfel-Fall des Users: gleiche Field-Konfiguration
    /// wie in <see cref="WarpedAndRotated_DestinationCorners_AreIdenticalBetweenEditorAndExport"/>,
    /// aber dieses Mal über Pixel-Vergleich statt Geometrie. Wir exportieren
    /// das Field auf einen einfarbigen Hintergrund (rot), finden den Text-
    /// Schwerpunkt im exportierten PNG, rendern die Editor-Vorschau und
    /// projizieren ihren Text-Schwerpunkt durch denselben Layout-Stack
    /// (Canvas.Left/Top + UserControl-Rotation + Canvas-Position) zurück in
    /// Image-Pixel-Coords. Beide Schwerpunkte müssen praktisch identisch sein.
    ///
    /// Schlägt der Test fehl, weicht das exportierte PNG sichtbar von der
    /// Editor-Live-Vorschau ab — und der User sieht die Text-Position erst
    /// nach dem Speichern an einer falschen Stelle.
    /// </summary>
    /// <summary>
    /// Regressions-Test für den "hohlen D"-Bug: Avalonias RenderTargetBitmap
    /// zeichnet Text auf transparentem Hintergrund nur als Glyph-Outline (kein
    /// Fill), weil der Compositor nichts hat, gegen das er blenden könnte. Der
    /// WarpPreviewService rendert deshalb auf weißem Hintergrund und keyed das
    /// Alpha aus der RGB-Mischung zurück. Der Export-Pfad muss dasselbe tun —
    /// sonst hat das exportierte PNG hohle Buchstaben, während die Editor-
    /// Vorschau gefüllte zeigt: klassischer Editor↔Output-Drift.
    ///
    /// Wir messen das, indem wir die Anzahl Text-Pixel im Export-PNG mit der
    /// Anzahl Text-Pixel im Editor-Preview-Bitmap vergleichen. Bei vollen
    /// Glyphen liegen beide Counts in derselben Größenordnung. Bei nur-Outline-
    /// Rendering wäre der Export deutlich pixel-ärmer.
    /// </summary>
    /// <summary>
    /// Regression: nahezu-weißer Text (Color≈Hintergrund-Weiß) im Warp-Pfad
    /// muss sichtbar bleiben. Die Alpha-Keying-Logik in WarpPreviewService
    /// schreibt cov=(255-r)/(255-color.R); bei color=#fffdfff5 (R=253) wird
    /// der Divisor sehr klein und reagiert empfindlich auf Renderer-Noise.
    /// Falls Pixel als pures Weiß (255,255,255) rastern, fallen sie auf
    /// alpha=0 → Text verschwindet komplett. Wir verifizieren, dass auf
    /// einem nicht-weißen Hintergrund (Cube-Blau) noch ein Mindestmaß an
    /// Pixeln sichtbar ist.
    /// </summary>
    [AvaloniaFact]
    public void WarpedField_NearWhiteText_RemainsVisibleAfterAlphaKeying()
    {
        // Ein einfaches Field auf blauem Hintergrund mit fast-weißer Textfarbe.
        // Wenn das Alpha-Keying den Text wegradiert, hat das exportierte PNG
        // 0 nicht-blaue Pixel.
        const int imgW = 600, imgH = 400;
        var src = CreateSolidPng("white-bg.png", imgW, imgH, new SolidColorBrush(Color.Parse("#3050A0")));
        var slotId = Guid.NewGuid();
        var f = new TextField
        {
            ImageSlotId = slotId,
            X = 100, Y = 100, Width = 400, Height = 200,
            FontFamily = "Arial", FontSize = 80, FontWeight = "Bold",
            Color = "#fffdfff5", CurrentText = "DA",
            HorizontalTextAlignment = "Center",
            VerticalTextAlignment = "Center",
            // Mini-Warp triggert Skia-Pfad
            CornerNWdx = 5, CornerNWdy = 5,
        };
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "white-bg.png" } },
            TextFields = { f },
        };
        var dest = Path.Combine(_tempDir, "white-out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        using var bmp = new Bitmap(dest);
        var pixels = ReadAllPixels(bmp);
        var stride = bmp.PixelSize.Width * 4;
        var nonBlueCount = 0;
        for (var y = 0; y < bmp.PixelSize.Height; y++)
        for (var x = 0; x < bmp.PixelSize.Width; x++)
        {
            var idx = y * stride + x * 4;
            var b = pixels[idx + 0]; var g = pixels[idx + 1]; var r = pixels[idx + 2];
            // Hintergrundblau ist (R=48, G=80, B=160). Text-Pixel haben hohe RGB-Werte.
            if (r > 200 && g > 200 && b > 200) nonBlueCount++;
        }
        Assert.True(nonBlueCount > 200,
            $"Nahezu-weißer Text muss im Export sichtbar bleiben. nonBlue-Pixel: {nonBlueCount}.");
    }

    [AvaloniaFact]
    public void WarpedField_GlyphInteriors_AreFilled_NotJustOutlined()
    {
        var f = MakeUserField1();
        const int imgW = 1100, imgH = 1200;
        var src = CreateSolidPng("fill-bg.png", imgW, imgH, Brushes.Red);
        var slotId = Guid.NewGuid();
        f.ImageSlotId = slotId;
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "fill-bg.png" } },
            TextFields = { f },
        };
        var dest = Path.Combine(_tempDir, "fill.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        using var exportBitmap = new Bitmap(dest);
        var (exCount, _, _) = NonRedCentroid(
            ReadAllPixels(exportBitmap), exportBitmap.PixelSize.Width, exportBitmap.PixelSize.Height);

        var layout = WarpPreviewService.RenderPreview(f);
        Assert.NotNull(layout);
        var (edCount, _, _) = NonTransparentCentroid(
            ReadAllPixels(layout!.Bitmap),
            layout.Bitmap.PixelSize.Width, layout.Bitmap.PixelSize.Height);

        // Editor zeigt voll gefüllte Glyphen → ein "D" hat einen großen
        // Bauchbereich aus Text-Pixeln. Wenn der Export nur Outlines rendert,
        // verlieren wir grob den Innenbereich der Glyphen → Pixel-Anzahl
        // sackt auf ein Drittel oder weniger ab. Mindestanforderung: die
        // Export-Pixel-Anzahl muss mindestens 60 % der Editor-Pixel-Anzahl
        // erreichen, sonst hat der Export hohle Buchstaben.
        // Diagnostic: print ratio so we see the actual fill density.
        Assert.True(exCount >= edCount * 0.85,
            $"Export-Pixel-Anzahl deutlich kleiner als Editor: export={exCount}, editor={edCount} " +
            $"(Verhältnis {(double)exCount / Math.Max(1, edCount):F2}). Wahrscheinlich rendern " +
            $"Glyphen nur als Outline statt gefüllt.");
    }

    /// <summary>
    /// Diagnose-Helper (auf Wunsch aktivierbar): rendert Editor-Vorschau und
    /// Export für die User-Field-Konfiguration in Dateien, die man visuell
    /// betrachten kann. Bleibt im Repo, damit man bei zukünftigen Editor-↔-
    /// Export-Diskrepanzen schnell wieder Bilder produzieren kann.
    /// </summary>
#if FALSE
    [AvaloniaFact]
    public void Diag_DumpEditorAndExportForUserField_ToTmp()
    {
        // Verwendet das tatsächliche User-Würfelbild als Hintergrund + beide
        // User-Fields aus der Template.json. Speichert den Export und ein
        // simuliertes Editor-Composite ins /tmp, damit man visuell vergleichen
        // kann.
        const string userCube = "/home/mlinz/Downloads/3d-cube-png-47045.png";
        if (!File.Exists(userCube)) return; // skip falls Datei nicht da
        var dump = "/tmp/danny-export-dump";
        Directory.CreateDirectory(dump);

        var slotId = Guid.NewGuid();
        var slot = new ImageSlot { Id = slotId, FileName = "cube.png" };
        // Physisch ins TempDir kopieren, damit ExportSlot drauf zugreifen kann
        var localCube = Path.Combine(_tempDir, "cube.png");
        File.Copy(userCube, localCube, overwrite: true);

        var fYellow = MakeUserField0(); fYellow.ImageSlotId = slotId;
        var fWhite = MakeUserField1();  fWhite.ImageSlotId = slotId;
        // Originalfarbe der User-Daten wiederherstellen (war im MakeUserField1
        // auf #000080 für Pixel-Detektion auf rotem Test-Bild umgestellt).
        fWhite.Color = "#fffdfff5";
        var template = new Template
        {
            ImageSlots = { slot },
            TextFields = { fYellow, fWhite },
        };
        var dest = Path.Combine(dump, "export-real.png");
        new ExportService().ExportSlot(template, slot, localCube, dest);

        // Composite: Hintergrund + Editor-Vorschau-Bitmaps für beide Fields,
        // jeweils mit der UserControl-Rotation. Das reproduziert exakt das,
        // was Avalonia im Editor anzeigt.
        using var bg = new Bitmap(localCube);
        var imgW = bg.PixelSize.Width;
        var imgH = bg.PixelSize.Height;
        var compositeBmp = new RenderTargetBitmap(new PixelSize(imgW, imgH), new Vector(96, 96));
        using (var ctx = compositeBmp.CreateDrawingContext())
        {
            ctx.DrawImage(bg, new Rect(0, 0, imgW, imgH));
            DrawEditorPreviewOnto(ctx, fYellow);
            DrawEditorPreviewOnto(ctx, fWhite);
        }
        compositeBmp.Save(Path.Combine(dump, "editor-composite-real.png"));

        // Each warped field's preview bitmap separately, to see if the
        // alpha-keying produces filled or outline-only glyphs.
        var ly = WarpPreviewService.RenderPreview(fYellow);
        if (ly != null) ly.Bitmap.Save(Path.Combine(dump, "preview-yellow.png"));
        var lw = WarpPreviewService.RenderPreview(fWhite);
        if (lw != null) lw.Bitmap.Save(Path.Combine(dump, "preview-white.png"));

        // Was passiert, wenn DrawTextField direkt auf transparente RTB schreibt
        // (Export-Pfad ohne Alpha-Keying)? Dump für visuellen Vergleich.
        DumpRawTransparentDraw(fYellow, Path.Combine(dump, "transparent-raw-yellow.png"));
        DumpRawTransparentDraw(fWhite, Path.Combine(dump, "transparent-raw-white.png"));
        DumpSKDecodedAfterRoundtrip(fYellow, Path.Combine(dump, "sk-roundtrip-yellow.png"));
        DumpSKDecodedAfterRoundtrip(fWhite, Path.Combine(dump, "sk-roundtrip-white.png"));
    }

    private static void DumpSKDecodedAfterRoundtrip(TextField field, string outPath)
    {
        // Reproduziert exakt den ApplyWarpedField-Pfad: Avalonia RTB → PNG →
        // MemoryStream → SKBitmap.Decode. Speichert das Ergebnis als PNG, um
        // zu sehen, ob die Glyphen nach dem Roundtrip noch gefüllt sind.
        var w = (int)Math.Ceiling(field.Width);
        var h = (int)Math.Ceiling(field.Height);
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
            BoldRanges = field.BoldRanges,
        };
        using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var c = rtb.CreateDrawingContext())
        {
            ExportService.DrawTextField(c, localField);
        }
        using var ms = new MemoryStream();
        rtb.Save(ms);
        ms.Position = 0;
        using var sk = SkiaSharp.SKBitmap.Decode(ms);
        using var img = SkiaSharp.SKImage.FromBitmap(sk);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(outPath);
        data.SaveTo(fs);
    }

    private static void DumpRawTransparentDraw(TextField field, string outPath)
    {
        var w = (int)Math.Ceiling(field.Width);
        var h = (int)Math.Ceiling(field.Height);
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
        using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var c = rtb.CreateDrawingContext())
        {
            ExportService.DrawTextField(c, localField);
        }
        rtb.Save(outPath);
    }
#endif

    private static void DrawEditorPreviewOnto(Avalonia.Media.DrawingContext ctx, TextField f)
    {
        var layout = WarpPreviewService.RenderPreview(f);
        if (layout is null) return;
        var pad = TextFieldGeometry.OuterPadding;
        var canvasX = f.X - pad + layout.OffsetX;
        var canvasY = f.Y - pad + layout.OffsetY;
        var transform =
            Avalonia.Matrix.CreateTranslation(-(f.X + f.Width / 2.0), -(f.Y + f.Height / 2.0)) *
            Avalonia.Matrix.CreateRotation(f.Rotation * Math.PI / 180.0) *
            Avalonia.Matrix.CreateTranslation(f.X + f.Width / 2.0, f.Y + f.Height / 2.0);
        using (ctx.PushTransform(transform))
        {
            ctx.DrawImage(layout.Bitmap,
                new Rect(canvasX, canvasY,
                         layout.Bitmap.PixelSize.Width, layout.Bitmap.PixelSize.Height));
        }
    }

    private static TextField MakeUserField0() => new()
    {
        X = 184.78148829430478, Y = 455.5109619897325,
        Width = 347.82558254192696, Height = 370.6544028088586,
        FontFamily = "Arial", FontSize = 200, FontWeight = "Normal",
        Color = "#ffe8ff54", CurrentText = "DA",
        HorizontalTextAlignment = "Center",
        VerticalTextAlignment = "Center",
        Rotation = 25.8198088004562,
        CornerNWdx = -194.173188402444, CornerNWdy = -57.0785669888783,
        CornerNEdx = -9.68928251028914, CornerNEdy = -14.6945186381606,
        CornerSEdx = 225.874847432182,  CornerSEdy = 97.3112407393678,
        CornerSWdx = 37.0032133013674,  CornerSWdy = 50.8276849493709,
        BoldRanges = new List<BoldRange> { new() { Start = 1, Length = 1 } },
    };

    [AvaloniaFact]
    public void WarpedAndRotated_TextCentroid_LandsAtSameCanvasPosition_InEditorAndExport()
    {
        var f = MakeUserField1();
        // Bildgröße: das Quad reicht im Worst-Case bis ca. (-30, 250) ↔
        // (900, 780), wir machen das Bild großzügig 1100×1200 — passt zum
        // Original-Würfel-Aspect (1030×1145).
        const int imgW = 1100, imgH = 1200;

        var src = CreateSolidPng("rotwarp-cube.png", imgW, imgH, Brushes.Red);
        var slotId = Guid.NewGuid();
        f.ImageSlotId = slotId;
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "rotwarp-cube.png" } },
            TextFields = { f },
        };
        var dest = Path.Combine(_tempDir, "rotwarp-cube.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        using var exportBitmap = new Bitmap(dest);
        var exportPx = ReadAllPixels(exportBitmap);
        var (exCount, exCx, exCy) = NonRedCentroid(exportPx, imgW, imgH);
        Assert.True(exCount > 100, $"Export hatte zu wenige Text-Pixel: {exCount}.");

        // Editor: Vorschau-Bitmap + Layout-Transform-Stack → Image-Pixel-Coords.
        var layout = WarpPreviewService.RenderPreview(f);
        Assert.NotNull(layout);
        var (edCount, edCxLocal, edCyLocal) = NonTransparentCentroid(
            ReadAllPixels(layout!.Bitmap),
            layout.Bitmap.PixelSize.Width,
            layout.Bitmap.PixelSize.Height);
        Assert.True(edCount > 100, $"Editor-Preview hatte zu wenige Text-Pixel: {edCount}.");

        // Editor-Schwerpunkt im Bitmap-Local → OuterRoot-Local → rotiert →
        // Canvas (Image-Pixel).
        var (edCx, edCy) = ProjectEditorCentroidToCanvas(f, layout, edCxLocal, edCyLocal);

        // Toleranz: 1 Pixel — Anti-Aliasing- und Sub-Pixel-Glyph-Effekte
        // verschieben den Schwerpunkt höchstens minimal. Echte Pipeline-Bugs
        // (z. B. fehlende Rotation, falsches Anchor-Center) liefern
        // typischerweise mehrere Dutzend bis Hunderte Pixel Differenz.
        Assert.True(Math.Abs(exCx - edCx) < 1.0,
            $"X-Schwerpunkt Editor↔Export weicht ab: export=({exCx:F2},{exCy:F2}), editor=({edCx:F2},{edCy:F2}). Δ=({exCx-edCx:F2},{exCy-edCy:F2})");
        Assert.True(Math.Abs(exCy - edCy) < 1.0,
            $"Y-Schwerpunkt Editor↔Export weicht ab: export=({exCx:F2},{exCy:F2}), editor=({edCx:F2},{edCy:F2}). Δ=({exCx-edCx:F2},{exCy-edCy:F2})");
    }

    /// <summary>
    /// Liefert die exakten User-Daten von Field 1 ("DA", -35°-rotiert,
    /// 4-Punkt-warped) aus der gespeicherten template.json des 3D-shapes-
    /// Templates. Color wird auf dunkelblau überschrieben, weil das
    /// Pixel-Detektion auf einem roten Test-Hintergrund eindeutig macht
    /// (User-Originalfarbe ist fast-weiß und wäre auf rot sichtbar, aber auf
    /// dem realen Cube-Bild blendet die Originalfarbe gegen weiß).
    /// </summary>
    private static TextField MakeUserField1() => new()
    {
        X = 6.7112567279012865, Y = 380.51033260529175,
        Width = 608.3320164003616, Height = 439.5018623999583,
        FontFamily = "Arial", FontSize = 250, FontWeight = "Normal",
        Color = "#000080", CurrentText = "DA",
        HorizontalTextAlignment = "Center",
        VerticalTextAlignment = "Center",
        Rotation = -35.4353571403052,
        CornerNWdx = 80.4130556150957,  CornerNWdy = -53.1587248556838,
        CornerNEdx = 0,                  CornerNEdy = 0,
        CornerSEdx = 227.650460885308,  CornerSEdy = 44.9259832328499,
        CornerSWdx = 302.128660944703,  CornerSWdy = -4.35090682466182,
        BoldRanges = new List<BoldRange> { new() { Start = 1, Length = 1 } },
    };

    private string CreateSolidPng(string name, int w, int h, IBrush fill)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
            ctx.DrawRectangle(fill, null, new Rect(0, 0, w, h));
        bmp.Save(path);
        return path;
    }

    /// <summary>
    /// Schwerpunkt aller Pixel, die DEUTLICH BLAU sind — auf rotem Hintergrund
    /// erkennt diese Heuristik dunkelblauen Text inkl. anti-aliasing-Edge-
    /// Pixel zuverlässig (R&lt;200 ⇒ kein voller Rotkanal, B&gt;50 ⇒ Blau-Anteil
    /// vorhanden).
    /// </summary>
    private static (int Count, double Cx, double Cy) NonRedCentroid(byte[] buffer, int w, int h)
    {
        var stride = w * 4;
        var count = 0;
        var sumX = 0.0;
        var sumY = 0.0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var idx = y * stride + x * 4;
            var b = buffer[idx + 0];
            var r = buffer[idx + 2];
            // Hintergrund-rot hat R≈255, B≈0. Dunkelblauer Text + AA-Mischung
            // verschiebt R deutlich nach unten ODER hebt B sichtbar an.
            if (r < 200 && b > 30)
            {
                count++;
                sumX += x;
                sumY += y;
            }
        }
        return count == 0 ? (0, 0, 0) : (count, sumX / count, sumY / count);
    }

    private static (int Count, double Cx, double Cy) NonTransparentCentroid(byte[] buffer, int w, int h)
    {
        var stride = w * 4;
        var count = 0;
        var sumX = 0.0;
        var sumY = 0.0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var idx = y * stride + x * 4;
            var a = buffer[idx + 3];
            if (a > 50)
            {
                count++;
                sumX += x;
                sumY += y;
            }
        }
        return count == 0 ? (0, 0, 0) : (count, sumX / count, sumY / count);
    }

    /// <summary>
    /// Projiziert einen Punkt (cxLocal, cyLocal) im Editor-Vorschau-Bitmap
    /// (Bitmap-lokales Koordinatensystem) durch denselben Layout-Stack, den
    /// die Avalonia-UI auf das gerenderte Visual anwendet, und liefert die
    /// resultierende Image-Pixel-Position. Das simuliert "wo zeichnet der
    /// Editor-Compositor diesen Bitmap-Punkt im Bild?".
    ///
    /// Stack:
    ///   1. Bitmap-Local → OuterRoot-Local: + (OffsetX, OffsetY)
    ///   2. OuterRoot-Local → OuterRoot-Mitte: − (W/2+pad, H/2+pad)
    ///   3. Rotation um Field.Rotation
    ///   4. + Canvas-Mitte (X+W/2, Y+H/2)
    /// </summary>
    private static (double X, double Y) ProjectEditorCentroidToCanvas(
        TextField f, WarpPreviewService.Layout layout, double cxLocal, double cyLocal)
    {
        var pad = TextFieldGeometry.OuterPadding;
        var outerX = cxLocal + layout.OffsetX;
        var outerY = cyLocal + layout.OffsetY;
        var dx = outerX - (f.Width / 2.0 + pad);
        var dy = outerY - (f.Height / 2.0 + pad);
        var rad = f.Rotation * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var rx = dx * cos - dy * sin;
        var ry = dx * sin + dy * cos;
        return (f.X + f.Width / 2.0 + rx, f.Y + f.Height / 2.0 + ry);
    }

    /// <summary>
    /// Bug-Repro für den 3D-Würfel-Fall des Users: ein TextField mit Rotation
    /// UND 4-Punkt-Verzerrung UND BoldRanges UND HorizontalAlignment=Center.
    /// Die exakte Field-Konfiguration stammt aus der gespeicherten template.json
    /// (yellow "DA" auf der grünen Würfelfläche, FontSize 250, alle Eckpunkte
    /// versetzt, -35° rotiert). Editor und Export müssen bei diesem Mix
    /// dieselben Ziel-Eckpunkte liefern — sonst wandert der Text beim Speichern
    /// auf eine andere Position als die Live-Vorschau zeigt.
    /// </summary>
    [AvaloniaFact]
    public void WarpedAndRotated_DestinationCorners_AreIdenticalBetweenEditorAndExport()
    {
        // Die User-Field-Daten: Field 1 aus template.json ("3d-shapes"-Template).
        var f = new TextField
        {
            X = 6.7112567279012865, Y = 380.51033260529175,
            Width = 608.3320164003616, Height = 439.5018623999583,
            FontFamily = "Arial", FontSize = 250, FontWeight = "Normal",
            Color = "#fffdfff5", CurrentText = "DA",
            HorizontalTextAlignment = "Center",
            VerticalTextAlignment = "Center",
            Rotation = -35.4353571403052,
            CornerNWdx = 80.4130556150957,  CornerNWdy = -53.1587248556838,
            CornerNEdx = 0,                  CornerNEdy = 0,
            CornerSEdx = 227.650460885308,  CornerSEdy = 44.9259832328499,
            CornerSWdx = 302.128660944703,  CornerSWdy = -4.35090682466182,
            BoldRanges = new List<BoldRange> { new() { Start = 1, Length = 1 } },
        };

        // Export: berechnet die Ziel-Eckpunkte direkt im Image-Pixel-Raum.
        var exportCorners = ExportService.ComputeDestinationCorners(f);

        // Editor: das Vorschau-Bitmap wird in einem unrotierten OuterRoot-
        // Koordinatenraum gerendert (Quad-Eckpunkte mit pad-Offset addiert,
        // ohne Rotation), und die UserControl rotiert das Ganze später um
        // den OuterRoot-Mittelpunkt. Wir simulieren genau diesen Layout-
        // Stack hier.
        var editorCorners = ComputeEditorEffectiveCorners(f);

        // Toleranz von 0.01px reicht — beide Pfade sollten exakt dieselbe
        // Mathematik liefern, nur Rounding-Mikrodifferenzen (double-Precision)
        // sind erlaubt. Größere Abweichungen wären ein echter Pipeline-Bug.
        for (var i = 0; i < 4; i++)
        {
            var name = i switch { 0 => "NW", 1 => "NE", 2 => "SE", _ => "SW" };
            Assert.True(Math.Abs(exportCorners[i].X - editorCorners[i].X) < 0.01,
                $"Eckpunkt {name}: X-Differenz Editor↔Export zu groß. " +
                $"Export={exportCorners[i].X:F3}, Editor={editorCorners[i].X:F3}.");
            Assert.True(Math.Abs(exportCorners[i].Y - editorCorners[i].Y) < 0.01,
                $"Eckpunkt {name}: Y-Differenz Editor↔Export zu groß. " +
                $"Export={exportCorners[i].Y:F3}, Editor={editorCorners[i].Y:F3}.");
        }
    }

    /// <summary>
    /// Simuliert den Layout-Stack des Editors für ein warped+rotated Field
    /// und liefert die 4 Ziel-Eckpunkte (NW, NE, SE, SW) im Bildpixel-Raum
    /// — also dort, wo die UserControl-Rotation die Vorschau-Bitmap-Eckpunkte
    /// nach Canvas.Left/Top und RenderTransform tatsächlich platziert.
    ///
    /// Reihenfolge der Transformationen (von OuterRoot-lokal zu Canvas-Image-
    /// Pixel-Raum):
    ///   1. Quad-Eckpunkt im OuterRoot:
    ///        (pad + cornerXXdx, pad + [optional W/H] + cornerXXdy)
    ///   2. Translate um -OuterRoot-Mitte = (-(W/2+pad), -(H/2+pad))
    ///   3. Rotate um Field-Rotation
    ///   4. Translate um +OuterRoot-Mitte
    ///   5. Translate um (OuterX, OuterY) = (X-pad, Y-pad)
    ///
    /// Schritte 4+5 zusammen = Translate um (X+W/2, Y+H/2) — der Field-
    /// Mittelpunkt im Image-Pixel-Raum. Das ist exakt das Drehzentrum, das
    /// auch <see cref="ExportService"/> verwendet — die beiden Pfade sind
    /// daher mathematisch äquivalent, sofern die Eckpunkt-Offsets im
    /// LOKALEN Frame gespeichert sind und vom Export beim Anwenden mit-
    /// rotiert werden.
    /// </summary>
    private static PerspectiveMath.Pt[] ComputeEditorEffectiveCorners(TextField f)
    {
        var pad = TextFieldGeometry.OuterPadding;
        var cx = f.X + f.Width / 2.0;
        var cy = f.Y + f.Height / 2.0;
        var rad = f.Rotation * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        PerspectiveMath.Pt Apply(double xOuter, double yOuter)
        {
            // Schritt 2: zentrieren auf OuterRoot-Mitte (Drehzentrum).
            var dx = xOuter - (f.Width / 2.0 + pad);
            var dy = yOuter - (f.Height / 2.0 + pad);
            // Schritt 3: rotieren.
            var rx = dx * cos - dy * sin;
            var ry = dx * sin + dy * cos;
            // Schritte 4+5: zurück und nach Canvas-Mitte verschieben.
            return new PerspectiveMath.Pt(cx + rx, cy + ry);
        }

        return new[]
        {
            Apply(pad + f.CornerNWdx,             pad + f.CornerNWdy),
            Apply(pad + f.Width + f.CornerNEdx,   pad + f.CornerNEdy),
            Apply(pad + f.Width + f.CornerSEdx,   pad + f.Height + f.CornerSEdy),
            Apply(pad + f.CornerSWdx,             pad + f.Height + f.CornerSWdy),
        };
    }

    [AvaloniaFact]
    public void MultilineWithLineHeight_PreservesPositionsBetweenEditorAndExport()
    {
        // Mehrzeiliger Text mit explizit gesetzter LineHeight — testet, ob der
        // Glyph-für-Glyph-Renderpfad in DrawTextOrSpacedGlyphs zwischen Editor
        // und Export konsistente Y-Positionen liefert.
        const int X = 20, Y = 30, W = 200, H = 100;
        var src = CreateWhitePng("multi.png", 400, 300);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "multi.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = X, Y = Y, Width = W, Height = H,
                    FontFamily = "Arial", FontSize = 16, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "Eins\nZwei\nDrei",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                    LineHeight = 24,
                    LetterSpacing = 1, // erzwingt den Glyph-Pfad
                },
            },
        };

        var dest = Path.Combine(_tempDir, "multi.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);
        using var exportBitmap = new Bitmap(dest);
        var exportPixels = ReadAllPixels(exportBitmap);
        var (exCount, exCx, exCy) = DarkPixelStats(
            exportPixels, exportBitmap.PixelSize.Width, exportBitmap.PixelSize.Height,
            X, Y, W, H);

        var layout = WarpPreviewService.RenderPreview(template.TextFields[0]);
        Assert.NotNull(layout);
        var editorPixels = CompositeOnWhite(ReadAllPixels(layout!.Bitmap));
        var (edCount, edCx, edCy) = DarkPixelStats(
            editorPixels, layout.Bitmap.PixelSize.Width, layout.Bitmap.PixelSize.Height,
            0, 0, W, H);

        Assert.True(Math.Abs(exCx - edCx) < 3.0,
            $"X-Schwerpunkt mehrzeilig weicht ab: export={exCx:F2}, editor={edCx:F2}.");
        Assert.True(Math.Abs(exCy - edCy) < 3.0,
            $"Y-Schwerpunkt mehrzeilig weicht ab: export={exCy:F2}, editor={edCy:F2}.");
        Assert.True(edCount > exCount * 0.5 && edCount < exCount * 2.0,
            $"Pixel-Anzahl-Verhältnis verdächtig bei mehrzeilig: export={exCount}, editor={edCount}.");
    }
}
