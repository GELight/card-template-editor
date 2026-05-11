using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;

namespace CardTemplateEditor.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExportSvc_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateRedPng(string name, int w = 100, int h = 60)
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
            // BGRA8888 ist Avalonia's Default für CopyPixels
            return (buffer[idx + 2], buffer[idx + 1], buffer[idx + 0], buffer[idx + 3]);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [AvaloniaFact]
    public void ExportSlot_BackgroundOutsideTextField_RemainsSourceColor()
    {
        var src = CreateRedPng("front.png", 100, 60);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            Name = "T",
            ImageSlots = { new ImageSlot { Id = slotId, Name = "Front", FileName = "front.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId, Name = "titel",
                    X = 10, Y = 10, Width = 50, Height = 20,
                    FontFamily = "Arial", FontSize = 18,
                    Color = "#000000", CurrentText = "Hi",
                },
            },
        };
        var dest = Path.Combine(_tempDir, "out.png");

        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        Assert.True(File.Exists(dest));
        var (r, g, b, a) = ReadPixel(dest, 90, 50); // außerhalb TextField (Bildgröße 100x60)
        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
        Assert.Equal(255, a);
    }

    [AvaloniaFact]
    public void ExportSlot_TextFieldArea_ContainsNonBackgroundPixels()
    {
        var src = CreateRedPng("front.png", 100, 60);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            Name = "T",
            ImageSlots = { new ImageSlot { Id = slotId, Name = "Front", FileName = "front.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId, Name = "titel",
                    X = 4, Y = 4, Width = 90, Height = 50,
                    FontFamily = "Arial", FontSize = 36,
                    Color = "#000000",
                    FontWeight = "Bold",
                    CurrentText = "HALLO",
                },
            },
        };
        var dest = Path.Combine(_tempDir, "out.png");

        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        // Wir scannen den Textbereich und erwarten mindestens einen Pixel,
        // der NICHT die Hintergrundfarbe (255,0,0) ist — der Renderer hat Text gezeichnet.
        var foundDarkPixel = false;
        for (var y = 5; y < 50 && !foundDarkPixel; y++)
        for (var x = 5; x < 90 && !foundDarkPixel; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) foundDarkPixel = true;
        }

        Assert.True(foundDarkPixel,
            "Im Textfeld-Bereich wurden keine Pixel gefunden, die von der Hintergrundfarbe abweichen.");
    }

    [AvaloniaFact]
    public void ExportSlot_NoText_PreservesEntireSourceImage()
    {
        var src = CreateRedPng("front.png", 30, 20);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            Name = "T",
            ImageSlots = { new ImageSlot { Id = slotId, Name = "Front", FileName = "front.png" } },
            // kein TextField
        };
        var dest = Path.Combine(_tempDir, "out.png");

        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        var (r, g, b, a) = ReadPixel(dest, 15, 10);
        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(0, b);
        Assert.Equal(255, a);
    }

    [AvaloniaFact]
    public void ExportSlot_HorizontalAlignmentRight_ShiftsTextToRightSideOfBox()
    {
        // Schmaler Text in einer breiten Box: bei "Right" muss die rechte Hälfte
        // dunkle Pixel enthalten, die linke Hälfte bleibt komplett rot.
        var src = CreateRedPng("right.png", 200, 60);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "right.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId, X = 10, Y = 10,
                    Width = 180, Height = 40,
                    FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "Hi",
                    HorizontalTextAlignment = "Right",
                    VerticalTextAlignment = "Top",
                },
            },
        };
        var dest = Path.Combine(_tempDir, "right.png.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        var leftHalfHasDark = false;
        for (var y = 10; y < 50 && !leftHalfHasDark; y++)
        for (var x = 10; x < 90 && !leftHalfHasDark; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) leftHalfHasDark = true;
        }
        var rightHalfHasDark = false;
        for (var y = 10; y < 50 && !rightHalfHasDark; y++)
        for (var x = 110; x < 190 && !rightHalfHasDark; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) rightHalfHasDark = true;
        }

        Assert.False(leftHalfHasDark, "Linke Boxhälfte sollte bei Right-Alignment leer bleiben.");
        Assert.True(rightHalfHasDark, "Rechte Boxhälfte sollte bei Right-Alignment den Text enthalten.");
    }

    [AvaloniaFact]
    public void ExportSlot_VerticalAlignmentBottom_ShiftsTextToBottom()
    {
        var src = CreateRedPng("bottom.png", 100, 200);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "bottom.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId, X = 10, Y = 10,
                    Width = 80, Height = 180,
                    FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "Hi",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Bottom",
                },
            },
        };
        var dest = Path.Combine(_tempDir, "bottom.png.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        var topHalfHasDark = false;
        for (var y = 10; y < 90 && !topHalfHasDark; y++)
        for (var x = 10; x < 90 && !topHalfHasDark; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) topHalfHasDark = true;
        }
        var bottomHalfHasDark = false;
        for (var y = 110; y < 190 && !bottomHalfHasDark; y++)
        for (var x = 10; x < 90 && !bottomHalfHasDark; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) bottomHalfHasDark = true;
        }

        Assert.False(topHalfHasDark, "Obere Boxhälfte sollte bei Bottom-Alignment leer bleiben.");
        Assert.True(bottomHalfHasDark, "Untere Boxhälfte sollte bei Bottom-Alignment den Text enthalten.");
    }

    [AvaloniaFact]
    public void ExportSlot_TextStartsInsideEditorInset_NotAtFrameCorner()
    {
        // Editor zeigt den Text nicht am Frame-Rand, sondern eingerückt um
        // TextFieldGeometry.TextInset (= Border-Padding 8 + TextBox-Padding 2 = 10).
        // ExportService muss denselben Offset zeichnen, sonst weicht das PNG
        // vom Live-Editor ab.
        var src = CreateRedPng("inset.png", 200, 80);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "inset.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = 20, Y = 20,
                    Width = 160, Height = 50,
                    FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "X",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                },
            },
        };
        var dest = Path.Combine(_tempDir, "inset.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        // Der Frame beginnt bei X=20. Die Pixel-Spalten 20..29 sind die Inset-Zone:
        // dort darf KEIN dunkler Text-Pixel sein, sonst wäre der Text bündig am
        // Frame-Rand und das matcht den Editor nicht mehr.
        var anyDarkInInsetZone = false;
        for (var y = 20; y < 70 && !anyDarkInInsetZone; y++)
        for (var x = 20; x < 30 && !anyDarkInInsetZone; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) anyDarkInInsetZone = true;
        }
        Assert.False(anyDarkInInsetZone,
            "Text sollte um TextInset eingerückt sein, nicht direkt am Frame-Rand starten.");

        // Nach dem Inset (ab Spalte 30) muss der Buchstabe X dunkle Pixel haben.
        var anyDarkAfterInset = false;
        for (var y = 20; y < 70 && !anyDarkAfterInset; y++)
        for (var x = 30; x < 80 && !anyDarkAfterInset; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) anyDarkAfterInset = true;
        }
        Assert.True(anyDarkAfterInset, "Im inneren Textbereich müsste der Buchstabe sichtbar sein.");
    }

    [AvaloniaFact]
    public void ExportSlot_EmptyFontFamilyOrColor_FallsBackInsteadOfThrowing()
    {
        // Reproduziert das Symptom auf Linux: ein TextField mit FontFamily=""
        // und Color="" (z. B. nach defekt persistiertem JSON oder leerem Dropdown).
        // ExportSlot soll trotzdem ein PNG schreiben, statt die ganze Pipeline
        // mit einer ArgumentException abzubrechen.
        var src = CreateRedPng("front.png", 50, 30);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "front.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = 5, Y = 5, Width = 40, Height = 20,
                    FontFamily = "",     // <-- leerer Name würde ohne Fallback werfen
                    FontSize = 14,
                    Color = "",          // <-- leere Farbe würde Color.Parse werfen
                    CurrentText = "X",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                },
            },
        };
        var dest = Path.Combine(_tempDir, "fallback.png");

        // Soll NICHT werfen.
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        Assert.True(File.Exists(dest));
    }

    [AvaloniaFact]
    public void ExportSlot_Rotated180_TextRendersUpsideDown_AroundFrameCenter()
    {
        // 180°-Rotation eines Textfelds: ein Buchstabe der NORMAL in der oberen
        // Box-Hälfte säße (Top-Alignment) muss nach 180°-Rotation in der unteren
        // Box-Hälfte landen (Punkt-Spiegelung am Frame-Mittelpunkt).
        // Das ist ein robuster Test für die Rotations-Pipeline, ohne Pixel-genauer
        // Glyph-Form-Vergleich.
        var src = CreateRedPng("rot180.png", 200, 200);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "rot180.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = 50, Y = 50,
                    Width = 100, Height = 100,
                    FontFamily = "Arial", FontSize = 32, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "I",
                    HorizontalTextAlignment = "Center",
                    VerticalTextAlignment = "Top",
                    Rotation = 180,
                },
            },
        };
        var dest = Path.Combine(_tempDir, "rot180.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        // Frame: (50,50)-(150,150), Mitte (100,100). Top-Aligned "I" säße ohne
        // Rotation in der oberen Box-Hälfte (y ≈ 50..100).
        // Mit 180°-Rotation muss der Text in der UNTEREN Box-Hälfte (y ≈ 100..150)
        // sichtbar sein, NICHT in der oberen.
        var topHalfHasDark = false;
        for (var y = 55; y < 95 && !topHalfHasDark; y++)
        for (var x = 60; x < 140 && !topHalfHasDark; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) topHalfHasDark = true;
        }
        var bottomHalfHasDark = false;
        for (var y = 105; y < 145 && !bottomHalfHasDark; y++)
        for (var x = 60; x < 140 && !bottomHalfHasDark; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) bottomHalfHasDark = true;
        }

        Assert.False(topHalfHasDark,
            "Bei 180°-Rotation sollte die obere Box-Hälfte leer sein — Text wird in die untere gespiegelt.");
        Assert.True(bottomHalfHasDark,
            "Bei 180°-Rotation muss der gespiegelte Text in der unteren Box-Hälfte sichtbar sein.");
    }

    [AvaloniaFact]
    public void ExportSlot_Rotated90_TextLeavesAxisAlignedBoxBoundary()
    {
        // 90°-Rotation: ein horizontaler Text in einer flachen Box wird vertikal,
        // erstreckt sich also weit oberhalb und unterhalb der unrotierten Box.
        // Verifikation: Pixel oberhalb der unrotierten Box (y < 100) enthalten
        // jetzt Text — genuiner Beweis, dass die Rotation greift.
        var src = CreateRedPng("rot90.png", 300, 300);
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId, FileName = "rot90.png" } },
            TextFields =
            {
                new TextField
                {
                    ImageSlotId = slotId,
                    X = 100, Y = 100,
                    Width = 100, Height = 30,
                    FontFamily = "Arial", FontSize = 24, FontWeight = "Bold",
                    Color = "#000000", CurrentText = "ABC",
                    HorizontalTextAlignment = "Left",
                    VerticalTextAlignment = "Top",
                    Rotation = 90,
                },
            },
        };
        var dest = Path.Combine(_tempDir, "rot90.out.png");
        new ExportService().ExportSlot(template, template.ImageSlots[0], src, dest);

        // Unrotierte Box: (100,100)-(200,130). Mitte (150, 115).
        // 90° CW um Mitte: (110, 110) → (155, 75); (160, 130) → (135, 125).
        // Erwartet: Text-Pixel oberhalb der unrotierten Box (y < 100, x ≈ 130-160).
        var aboveBoxHasDark = false;
        for (var y = 70; y < 100 && !aboveBoxHasDark; y++)
        for (var x = 130; x < 170 && !aboveBoxHasDark; x++)
        {
            var (r, g, b, _) = ReadPixel(dest, x, y);
            if (r < 200 && g < 50 && b < 50) aboveBoxHasDark = true;
        }
        Assert.True(aboveBoxHasDark,
            "Bei 90°-Rotation muss Text-Output oberhalb der unrotierten Box auftauchen.");
    }

    [Fact]
    public void ExportSlot_MissingSourceImage_ThrowsFileNotFound()
    {
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId } },
        };

        Assert.Throws<FileNotFoundException>(() =>
            new ExportService().ExportSlot(
                template, template.ImageSlots[0],
                Path.Combine(_tempDir, "fehlt.png"),
                Path.Combine(_tempDir, "out.png")));
    }

    [Fact]
    public void ExportSlot_EmptyDestPath_ThrowsArgumentException()
    {
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            ImageSlots = { new ImageSlot { Id = slotId } },
        };

        Assert.Throws<ArgumentException>(() =>
            new ExportService().ExportSlot(
                template, template.ImageSlots[0], "irgendwas.png", ""));
    }
}
