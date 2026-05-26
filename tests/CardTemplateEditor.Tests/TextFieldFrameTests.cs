using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CardTemplateEditor.Models;
using CardTemplateEditor.ViewModels;
using CardTemplateEditor.Views.Controls;

namespace CardTemplateEditor.Tests;

public class TextFieldFrameTests
{
    // --- ComputeDrag ---------------------------------------------------------

    [Fact]
    public void ComputeDrag_AddsDelta_ToStartingPosition()
    {
        var (x, y) = TextFieldFrame.ComputeDrag(startX: 50, startY: 100, dx: 30, dy: -20);
        Assert.Equal(80, x);
        Assert.Equal(80, y);
    }

    [Fact]
    public void ComputeDrag_AcceptsNegativeCoordinates()
    {
        var (x, y) = TextFieldFrame.ComputeDrag(startX: 10, startY: 10, dx: -50, dy: -50);
        Assert.Equal(-40, x);
        Assert.Equal(-40, y);
    }

    // --- ComputeResize -------------------------------------------------------

    [Fact]
    public void ComputeResize_SE_GrowsWidthAndHeight()
    {
        var r = TextFieldFrame.ComputeResize("SE", 50, 50, 200, 30, dx: 40, dy: 25);
        Assert.Equal(50, r.X);
        Assert.Equal(50, r.Y);
        Assert.Equal(240, r.Width);
        Assert.Equal(55, r.Height);
    }

    [Fact]
    public void ComputeResize_NW_ShrinksFromTopLeft_AndMovesOrigin()
    {
        var r = TextFieldFrame.ComputeResize("NW", 100, 100, 200, 60, dx: 20, dy: 10);
        Assert.Equal(120, r.X);
        Assert.Equal(110, r.Y);
        Assert.Equal(180, r.Width);
        Assert.Equal(50, r.Height);
    }

    [Fact]
    public void ComputeResize_E_DoesNotMoveOrigin()
    {
        var r = TextFieldFrame.ComputeResize("E", 10, 20, 100, 40, dx: 30, dy: 999);
        Assert.Equal(10, r.X);
        Assert.Equal(20, r.Y);
        Assert.Equal(130, r.Width);
        Assert.Equal(40, r.Height);
    }

    [Fact]
    public void ComputeResize_S_DoesNotMoveOrigin()
    {
        var r = TextFieldFrame.ComputeResize("S", 10, 20, 100, 40, dx: 999, dy: 25);
        Assert.Equal(10, r.X);
        Assert.Equal(20, r.Y);
        Assert.Equal(100, r.Width);
        Assert.Equal(65, r.Height);
    }

    [Fact]
    public void ComputeResize_KeepAspect_DragDiagonal_LocksRatio_AtSE()
    {
        // 200×100 (Aspekt 2:1) am SE: Drag (50, 10) — X dominiert
        // (50 > 10*2 = 20), also locked auf (50, 25). Final: 250×125.
        var r = TextFieldFrame.ComputeResize("SE", 0, 0, 200, 100,
            dx: 50, dy: 10, keepAspect: true);
        Assert.Equal(250, r.Width, precision: 4);
        Assert.Equal(125, r.Height, precision: 4);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
    }

    [Fact]
    public void ComputeResize_KeepAspect_DragVerticalDominant_LocksToHeight()
    {
        // 200×100 am SE: Drag (5, 30) — Y dominiert (30*2=60 > 5),
        // also dominant = 60, dx → 60, dy → 30. Final: 260×130.
        var r = TextFieldFrame.ComputeResize("SE", 0, 0, 200, 100,
            dx: 5, dy: 30, keepAspect: true);
        Assert.Equal(260, r.Width, precision: 4);
        Assert.Equal(130, r.Height, precision: 4);
    }

    [Fact]
    public void ComputeResize_KeepAspect_NW_GrowsBothDirectionsTowardOrigin()
    {
        // NW-Drag mit Alt: das Frame schrumpft, aber Aspekt bleibt fix.
        // 200×100 am NW: Drag (30, 5). NW: sx=-1, sy=-1 → growX = -30,
        // growY = -5. |growX| = 30 > |growY*aspect| = 10 → dominant=-30,
        // dx = -1 * -30 = 30, dy = -1 * -30/2 = 15. Frame schrumpft um
        // (30, 15) am NW → newW=170, newH=85, newX=30, newY=15.
        var r = TextFieldFrame.ComputeResize("NW", 0, 0, 200, 100,
            dx: 30, dy: 5, keepAspect: true);
        Assert.Equal(170, r.Width, precision: 4);
        Assert.Equal(85, r.Height, precision: 4);
        Assert.Equal(30, r.X, precision: 4);
        Assert.Equal(15, r.Y, precision: 4);
    }

    [Fact]
    public void ComputeResize_KeepAspect_OnEdgeHandle_IsNoOp()
    {
        // KeepAspect wirkt nur an Eck-Handles (NW/NE/SE/SW). An Edge-Handles
        // (N/E/S/W) ist nur eine Achse aktiv — ein Aspekt-Lock würde die
        // andere Achse zwingen, ohne dass der User das gewollt hätte.
        var r = TextFieldFrame.ComputeResize("E", 0, 0, 200, 100,
            dx: 50, dy: 30, keepAspect: true);
        Assert.Equal(250, r.Width, precision: 4);
        Assert.Equal(100, r.Height, precision: 4);
    }

    // --- ClampCornerOffsets (Sub-Task E) ------------------------------------

    [Fact]
    public void ClampCornerOffsets_WithinLimit_DoesNotChange()
    {
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 100 })
        {
            // maxOffset = max(200, 100) / 2 = 100; alle Werte mit |x| ≤ 100 → unverändert.
            CornerNWdx = 50, CornerNWdy = -100,
            CornerNEdx = -50, CornerNEdy = 50,
            CornerSEdx = 100, CornerSEdy = 100,
            CornerSWdx = -100, CornerSWdy = -100,
        };

        TextFieldFrame.ClampCornerOffsets(vm);

        Assert.Equal(50, vm.CornerNWdx);
        Assert.Equal(-100, vm.CornerNWdy);
        Assert.Equal(-50, vm.CornerNEdx);
        Assert.Equal(50, vm.CornerNEdy);
        Assert.Equal(100, vm.CornerSEdx);
        Assert.Equal(100, vm.CornerSEdy);
        Assert.Equal(-100, vm.CornerSWdx);
        Assert.Equal(-100, vm.CornerSWdy);
    }

    [Fact]
    public void ClampCornerOffsets_BeyondLimit_ClampsToHalfFrameSize()
    {
        // Frame 200×100, maxOffset = max(200, 100) / 2 = 100.
        // Alles außerhalb [-100, 100] wird auf den Rand geklemmt — User
        // verliert nie einen Punkt.
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 100 })
        {
            CornerSEdx = 9999, CornerSEdy = -9999,
            CornerNWdx = -1500, CornerNWdy = 1500,
        };

        TextFieldFrame.ClampCornerOffsets(vm);

        Assert.Equal(100, vm.CornerSEdx);
        Assert.Equal(-100, vm.CornerSEdy);
        Assert.Equal(-100, vm.CornerNWdx);
        Assert.Equal(100, vm.CornerNWdy);
    }

    // --- ResolveHandleMode (pure, Modifier-zu-Mode-Mapping) -----------------

    [Fact]
    public void ResolveHandleMode_NoModifier_OnCorner_ReturnsScale()
    {
        Assert.Equal(TextFieldFrame.HandleMode.Scale,
            TextFieldFrame.ResolveHandleMode(KeyModifiers.None, "SE"));
    }

    [Fact]
    public void ResolveHandleMode_Ctrl_OnCorner_ReturnsDistort()
    {
        Assert.Equal(TextFieldFrame.HandleMode.Distort,
            TextFieldFrame.ResolveHandleMode(KeyModifiers.Control, "SE"));
    }

    [Fact]
    public void ResolveHandleMode_Ctrl_OnEdge_FallsBackToScale()
    {
        // Strg an Edge-Handle ist undefiniert → Scale-Fallback, damit der
        // Handle nicht „tot" wirkt.
        Assert.Equal(TextFieldFrame.HandleMode.Scale,
            TextFieldFrame.ResolveHandleMode(KeyModifiers.Control, "N"));
    }

    [Fact]
    public void ResolveHandleMode_Alt_ReturnsScaleUniform()
    {
        Assert.Equal(TextFieldFrame.HandleMode.ScaleUniform,
            TextFieldFrame.ResolveHandleMode(KeyModifiers.Alt, "SE"));
    }

    [Fact]
    public void ResolveHandleMode_ShiftAlt_OnEdge_ReturnsSkew()
    {
        Assert.Equal(TextFieldFrame.HandleMode.Skew,
            TextFieldFrame.ResolveHandleMode(KeyModifiers.Shift | KeyModifiers.Alt, "N"));
    }

    [Fact]
    public void ResolveHandleMode_ShiftAlt_OnCorner_FallsBackToScale()
    {
        // Skew braucht eine Kante; an einer Ecke fallen wir auf Scale zurück.
        Assert.Equal(TextFieldFrame.HandleMode.Scale,
            TextFieldFrame.ResolveHandleMode(KeyModifiers.Shift | KeyModifiers.Alt, "NE"));
    }

    [Fact]
    public void ClampCornerOffsets_EmptyFrame_IsNoOp()
    {
        // Defensive: Width/Height = 0 — ohne Limit-Bereich machen wir nichts,
        // sonst würde Math.Clamp mit (-0, 0) alles auf 0 klemmen, was bei
        // einem brandneuen Field unangenehm wäre.
        var vm = new TextFieldViewModel(new TextField { Width = 0, Height = 0 })
        {
            CornerNWdx = 50, CornerSEdy = -100,
        };

        TextFieldFrame.ClampCornerOffsets(vm);

        Assert.Equal(50, vm.CornerNWdx);
        Assert.Equal(-100, vm.CornerSEdy);
    }

    [Fact]
    public void ComputeResize_ClampsWidthAndHeight_ToMinSize()
    {
        var r = TextFieldFrame.ComputeResize("SE", 10, 20, 100, 40, dx: -500, dy: -500);
        Assert.Equal(TextFieldFrame.MinSize, r.Width);
        Assert.Equal(TextFieldFrame.MinSize, r.Height);
    }

    [Fact]
    public void ComputeResize_NW_ClampedToMin_PinsRightAndBottomEdges()
    {
        // Mit start=(100,100,200,60) und einem Riesendelta nach SE:
        // ohne Clamp wäre newW = -300 (negativ), aber wir clampen auf MinSize=16.
        // Origin muss so verschoben werden, dass die rechte Kante (300) erhalten bleibt.
        var r = TextFieldFrame.ComputeResize("NW", 100, 100, 200, 60, dx: 500, dy: 500);
        Assert.Equal(TextFieldFrame.MinSize, r.Width);
        Assert.Equal(TextFieldFrame.MinSize, r.Height);
        // Rechte Kante des Originals: 100 + 200 = 300
        Assert.Equal(300 - TextFieldFrame.MinSize, r.X);
        // Untere Kante des Originals: 100 + 60 = 160
        Assert.Equal(160 - TextFieldFrame.MinSize, r.Y);
    }

    // --- InverseRotate (rotated-resize delta) -------------------------------

    [Fact]
    public void InverseRotate_ZeroDegrees_ReturnsInputUnchanged()
    {
        var (lx, ly) = TextFieldFrame.InverseRotate(30, 50, rotationDeg: 0);
        Assert.Equal(30, lx, precision: 9);
        Assert.Equal(50, ly, precision: 9);
    }

    [Fact]
    public void InverseRotate_90Degrees_MapsCanvasRightToLocalDown()
    {
        // Frame ist 90° im Uhrzeigersinn rotiert. Cursor-Bewegung "nach rechts"
        // in Canvas-Coords entspricht im lokalen Frame-Raum "nach oben"
        // (negative Y), weil die lokale Y-Achse durch die Rotation jetzt
        // canvas-rechts zeigt.
        var (lx, ly) = TextFieldFrame.InverseRotate(dx: 10, dy: 0, rotationDeg: 90);
        Assert.Equal(0, lx, precision: 6);
        Assert.Equal(-10, ly, precision: 6);
    }

    [Fact]
    public void InverseRotate_180Degrees_NegatesBothAxes()
    {
        var (lx, ly) = TextFieldFrame.InverseRotate(dx: 7, dy: -3, rotationDeg: 180);
        Assert.Equal(-7, lx, precision: 6);
        Assert.Equal(3, ly, precision: 6);
    }

    // --- Headless: das echte Pointer-Plumbing -------------------------------

    [AvaloniaFact]
    public void Resize_ViaPointerEvents_UpdatesViewModel()
    {
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 120, Height = 40,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 400, Height = 300, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 400, Height = 300, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Handles sitzen jetzt zentriert AM Quad-Eckpunkt (Canvas.Left/Top =
        // EckenX/Y − 4). Mit OuterPadding=32 und keinem Warp-Offset liegt die
        // SE-Ecke in Canvas-Coords bei (32+W, 32+H) = (152, 72). Handle-Mitte
        // ebenfalls (152, 72), Bounding-Box (148..156, 68..76).
        var handleStart = new Point(152, 72);
        var handleEnd = new Point(202, 102);

        window.MouseDown(handleStart, MouseButton.Left);
        window.MouseMove(handleEnd);
        window.MouseUp(handleEnd, MouseButton.Left);

        Assert.Equal(170, vm.Width, precision: 0);
        Assert.Equal(70, vm.Height, precision: 0);
        Assert.Equal(0, vm.X);
        Assert.Equal(0, vm.Y);

        window.Close();
    }

    [AvaloniaFact]
    public void CtrlDrag_OnCornerHandle_UpdatesOnlyCornerOffset_NotBoxSize()
    {
        // Iteration 14: Modifier-Tasten sind zurück. Strg+Drag an einem
        // Eck-Handle ist Distort — nur das Eckpunkt-Offset wandert,
        // Box-Dimensionen bleiben fix.
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 120, Height = 40,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 400, Height = 300, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 400, Height = 300, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var start = new Point(152, 72);
        var end = new Point(172, 92);

        // Modifier muss bei jedem Pointer-Event durchgereicht werden, weil
        // Iteration 14 Sub-Task „Live-Modus-Wechsel" den Modus auch während
        // PointerMove dynamisch neu auswertet.
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(end, RawInputModifiers.Control);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal(120, vm.Width);
        Assert.Equal(40, vm.Height);
        Assert.Equal(20, vm.CornerSEdx, precision: 0);
        Assert.Equal(20, vm.CornerSEdy, precision: 0);
        Assert.Equal(0, vm.CornerNWdx);
        Assert.Equal(0, vm.CornerNEdx);
        Assert.Equal(0, vm.CornerSWdx);

        window.Close();
    }

    [AvaloniaFact]
    public void ShiftAltDrag_OnEdgeHandle_SkewsTwoAdjacentCorners()
    {
        // Iteration 14: Shift+Alt an N-Edge schiebt NW- und NE-Offsets
        // parallel ENTLANG der Kante (Skew-along-Axis).
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 120, Height = 40,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 400, Height = 300, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 400, Height = 300, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var start = new Point(92, 32);
        var end = new Point(122, 50);
        var mods = RawInputModifiers.Shift | RawInputModifiers.Alt;

        window.MouseDown(start, MouseButton.Left, mods);
        window.MouseMove(end, mods);
        window.MouseUp(end, MouseButton.Left, mods);

        Assert.Equal(30, vm.CornerNWdx, precision: 0);
        Assert.Equal(30, vm.CornerNEdx, precision: 0);
        Assert.Equal(0, vm.CornerNWdy);
        Assert.Equal(0, vm.CornerNEdy);
        Assert.Equal(0, vm.CornerSEdx);
        Assert.Equal(0, vm.CornerSWdx);
        Assert.Equal(120, vm.Width);
        Assert.Equal(40, vm.Height);

        window.Close();
    }

    [AvaloniaFact]
    public void AltDrag_OnCornerHandle_ScalesUniform_KeepsAspectRatio()
    {
        // Iteration 14: Alt+Drag ist ScaleUniform — Width/Height-Verhältnis
        // bleibt konstant, auch wenn der User „mehr in eine Richtung" zieht.
        // Frame ist 200×100 → Aspekt 2:1. Drag in (50, 10) am SE-Handle:
        // ohne Lock wäre new Size = (250, 110); mit Lock dominiert die
        // X-Komponente (50 > 10*2=20), final ist new Size = (250, 125).
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 200, Height = 100,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 600, Height = 400, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 600, Height = 400, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // SE-Handle in Canvas-Coords: (32 + 200, 32 + 100) = (232, 132).
        var start = new Point(232, 132);
        var end = new Point(282, 142); // Δ = (50, 10)

        window.MouseDown(start, MouseButton.Left, RawInputModifiers.Alt);
        window.MouseMove(end, RawInputModifiers.Alt);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.Alt);

        // Aspekt 2:1 muss erhalten bleiben.
        Assert.Equal(200.0 / 100.0, vm.Width / vm.Height, precision: 4);
        Assert.Equal(250, vm.Width, precision: 0);
        Assert.Equal(125, vm.Height, precision: 0);

        window.Close();
    }

    [AvaloniaFact]
    public void RotationHandle_DragInQuarterCircle_UpdatesRotation()
    {
        // Frame in der Mitte der Canvas, damit der Rotation-Handle und der
        // Frame-Mittelpunkt eine gut testbare Geometrie haben.
        var vm = new TextFieldViewModel(new TextField
        {
            X = 100, Y = 100, Width = 200, Height = 80, Rotation = 0,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 600, Height = 500, Children = { frame } };
        // Canvas.Left/Top so setzen, dass das innere Frame an (vm.X, vm.Y) sitzt.
        Canvas.SetLeft(frame, vm.OuterX);
        Canvas.SetTop(frame, vm.OuterY);

        var window = new Window { Width = 600, Height = 500, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Rotation-Handle sitzt im Outer-Padding mittig oben: in Frame-Coords
        // (OuterWidth/2, ~15). In Canvas-Coords:
        //   X = OuterX + OuterWidth/2 = (X - 32) + (W + 64)/2
        //     = 100 - 32 + (200 + 64)/2 = 68 + 132 = 200
        //   Y ≈ OuterY + 15 = 68 + 15 = 83
        var handleCenter = new Point(200, 83);

        // Frame-Mittelpunkt in Canvas-Coords: (X + W/2, Y + H/2) = (200, 140).
        // Vom Mittelpunkt zum Handle zeigt der Vektor exakt nach oben (0, -57).
        // Das ist Atan2(-57, 0) = -90° (im Uhrzeigersinn-System mit Y nach unten:
        // -90° = "nach oben"). Wenn wir den Pointer um 90° gegen den Uhrzeigersinn
        // bewegen (von oben nach links): neuer Vektor (-57, 0) → 180°.
        // Differenz = 180 - (-90) = 270 → normalisiert auf -90°.
        // Erwartete Rotation: -90° (oder 270 vor Normalisierung).
        window.MouseDown(handleCenter, MouseButton.Left);
        window.MouseMove(new Point(143, 140));
        window.MouseUp(new Point(143, 140), MouseButton.Left);

        Assert.Equal(-90, vm.Rotation, precision: 0);

        window.Close();
    }

    [AvaloniaFact]
    public void Drag_OnFrame_SelectsTextFieldOnOwnerWindow()
    {
        // Owner-Window mit MainWindowViewModel als DataContext: Klick aufs Frame
        // muss SelectedTextField setzen, damit das Properties-Panel reagiert.
        var tempDir = Path.Combine(Path.GetTempPath(), "TfFrameSel_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var repo = new Services.TemplateRepository(tempDir);
            var owner = new MainWindowViewModel(repo);
            var template = owner.CreateNewTemplate();
            var slot = template.AddImageSlot(CreateOnePixelPng(tempDir));
            var fieldVm = template.AddTextField(slot.Id, "feld1");
            fieldVm.Width = 300; fieldVm.Height = 100;

            // SelectedTextField ist beim NewTemplate auf null gesetzt.
            owner.SelectedTextField = null;

            var frame = new TextFieldFrame { DataContext = fieldVm };
            // KEIN fieldVm.IsSelected = true: dieser Test prüft, dass der Klick
            // auf den DragBorder eines NICHT selektierten Feldes die Selektion
            // setzt. DragBorder ist via Background=Transparent immer hit-testbar.
            var canvas = new Canvas { Width = 600, Height = 400, Children = { frame } };
            Canvas.SetLeft(frame, 0);
            Canvas.SetTop(frame, 0);

            var window = new Window { Width = 600, Height = 400, Content = canvas, DataContext = owner };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // OuterPadding=32 → Inner-Frame sitzt bei (32,32). DragBorder-Padding=8
            // → Top-Drag-Strip in Canvas-Coords: y in 32..40, x in 32..32+W.
            // (60, 36) liegt sicher im Drag-Strip oberhalb der TextBox.
            window.MouseDown(new Point(60, 36), MouseButton.Left);
            window.MouseUp(new Point(60, 36), MouseButton.Left);

            Assert.Same(fieldVm, owner.SelectedTextField);
            // Nach dem Klick muss die Owner-Sync IsSelected auf dem VM gespiegelt haben.
            Assert.True(fieldVm.IsSelected);
            window.Close();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateOnePixelPng(string dir)
    {
        var path = Path.Combine(dir, "tiny.png");
        var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1, 1), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(Avalonia.Media.Brushes.White, null, new Rect(0, 0, 1, 1));
        }
        bmp.Save(path);
        return path;
    }

    [AvaloniaFact]
    public void Drag_ViaPointerEvents_UpdatesViewModel()
    {
        // Frame muss groß genug sein, dass das Padding=8 des DragBorders über der TextBox
        // nicht von der Stretch-Berechnung der TextBox aufgefressen wird.
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 300, Height = 100,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 600, Height = 400, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 600, Height = 400, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // OuterPadding=32 → Drag-Strip im inneren Frame: y in 32..40, x in 32..332.
        // (60, 36) liegt sicher oberhalb der TextBox im DragBorder-Padding-Streifen,
        // außerhalb von HandleNW (32..40, 32..40) und HandleN (zentriert).
        var dragStart = new Point(60, 36);
        var dragEnd = new Point(110, 66);

        window.MouseDown(dragStart, MouseButton.Left);
        window.MouseMove(dragEnd);
        window.MouseUp(dragEnd, MouseButton.Left);

        Assert.Equal(50, vm.X, precision: 0);
        Assert.Equal(30, vm.Y, precision: 0);
        Assert.Equal(300, vm.Width);
        Assert.Equal(100, vm.Height);

        window.Close();
    }

    // --- Live-Modus-Wechsel während Drag (Photoshop-Konvention) -------------

    [AvaloniaFact]
    public void DragWithoutModifier_ThenPressCtrl_SwitchesLiveToDistort()
    {
        // Photoshop-Konvention: Drag ohne Modifier startet als Scale, sobald
        // der User WÄHREND des Drags Strg drückt, springt der Modus live auf
        // Distort. Bei Mode-Wechsel wird der Anker zurückgesetzt, sodass nur
        // die Bewegung NACH dem Wechsel als Distort wirkt.
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 200, Height = 100,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 400, Height = 300, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 400, Height = 300, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // SE-Handle bei (32+200, 32+100) = (232, 132).
        var seHandle = new Point(232, 132);

        // Press ohne Modifier → Scale. Erstes Move ohne Modifier wirkt als Scale.
        window.MouseDown(seHandle, MouseButton.Left);
        window.MouseMove(new Point(252, 152));
        // Width sollte sich um 20 vergrößert haben, Height um 20.
        Assert.Equal(220, vm.Width, precision: 0);
        Assert.Equal(120, vm.Height, precision: 0);
        Assert.Equal(0, vm.CornerSEdx);

        // Strg drücken — nächstes Move triggert Mode-Switch zu Distort.
        // Der Mode-Switch reankert auf den aktuellen Stand; dieser Move-Tick
        // führt nur Re-Anker durch, danach folgt der Distort-Drag.
        window.MouseMove(new Point(252, 152), RawInputModifiers.Control);
        // Jetzt 30px nach rechts, 30px nach unten → Distort wirkt am SE.
        window.MouseMove(new Point(282, 182), RawInputModifiers.Control);
        window.MouseUp(new Point(282, 182), MouseButton.Left, RawInputModifiers.Control);

        // Box-Größe darf seit dem Mode-Wechsel nicht weiter gewachsen sein.
        Assert.Equal(220, vm.Width, precision: 0);
        Assert.Equal(120, vm.Height, precision: 0);
        // Distort-Offset um (30, 30).
        Assert.Equal(30, vm.CornerSEdx, precision: 0);
        Assert.Equal(30, vm.CornerSEdy, precision: 0);

        window.Close();
    }

    // --- Origin-Cross-Marker Drag (Sub-Task D) ------------------------------

    [AvaloniaFact]
    public void OriginCrossMarker_Drag_UpdatesRotationOriginRelXY()
    {
        // Frame bei (0,0,200,100): Default-Origin = Mitte, also bei
        // RotationOriginPoint = (32 + 100, 32 + 50) = (132, 82) in OuterRoot.
        // Cross-Marker-Mitte sitzt dort. Drag um (40, 20) → Origin wandert
        // auf (132+40, 82+20) = (172, 102) → relativ zur 200×100-Box:
        // RelX = 100/200 + 40/200 = 0.7, RelY = 50/100 + 20/100 = 0.7.
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 200, Height = 100,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 400, Height = 300, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 400, Height = 300, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var start = new Point(132, 82);
        var end = new Point(172, 102);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);

        Assert.Equal(0.7, vm.RotationOriginRelX, precision: 4);
        Assert.Equal(0.7, vm.RotationOriginRelY, precision: 4);

        window.Close();
    }

    [AvaloniaFact]
    public void RotationHandle_Drag_WithOffsetOrigin_RotatesAroundOrigin()
    {
        // Origin in der NW-Ecke (RelX=0, RelY=0). Frame bei (100,100, 200,80):
        // RotationOriginAbsolute = (100, 100) (= NW-Eck der Box).
        // Rotation-Handle sitzt oben mittig im Outer-Padding (15 px über
        // der Box-Oberkante): in Canvas-Coords (200, 83).
        // Vom Origin (100,100) zum Handle: Vektor (100, -17), Atan2 ≈ -9.65°.
        // Wir ziehen den Pointer auf (200, 200) → Vektor (100, 100), Atan2 = 45°.
        // Delta = 45 - (-9.65) = ~54.65°.
        var vm = new TextFieldViewModel(new TextField
        {
            X = 100, Y = 100, Width = 200, Height = 80,
            RotationOriginRelX = 0, RotationOriginRelY = 0,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 600, Height = 500, Children = { frame } };
        Canvas.SetLeft(frame, vm.OuterX);
        Canvas.SetTop(frame, vm.OuterY);

        var window = new Window { Width = 600, Height = 500, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var handleCenter = new Point(200, 83);
        var dragTo = new Point(200, 200);

        window.MouseDown(handleCenter, MouseButton.Left);
        window.MouseMove(dragTo);
        window.MouseUp(dragTo, MouseButton.Left);

        // Erwartet: ~54.65°. Toleranz 5° (Pointer-Hit-Test-Ungenauigkeit am
        // 14×14-Handle).
        Assert.True(Math.Abs(vm.Rotation - 54.65) < 5,
            $"Rotation um Origin (NW) sollte ~54.65° sein, war {vm.Rotation}.");

        window.Close();
    }

    // --- Tab-Navigation aktiviert Edit-Mode ---------------------------------

    [AvaloniaFact]
    public void TextEditor_FocusedViaTab_EntersEditMode()
    {
        // Tab-Navigation soll den User direkt in den Inline-Edit-Modus
        // springen lassen, ohne dass er erst doppelklicken muss.
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 200, Height = 40,
            CurrentText = "Hallo",
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 400, Height = 200, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 400, Height = 200, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsInEditMode);

        var textEditor = frame.FindControl<TextBox>("TextEditor");
        Assert.NotNull(textEditor);
        textEditor!.Focus(NavigationMethod.Tab);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsInEditMode);
        // Caret steht am Ende, damit der User direkt weitertippen kann.
        Assert.Equal("Hallo".Length, textEditor.CaretIndex);

        window.Close();
    }

    [AvaloniaFact]
    public void TextEditor_FocusedViaPointer_DoesNotEnterEditMode()
    {
        // Klick-Fokus darf den heutigen "Selektion-ohne-Edit"-Workflow nicht
        // brechen: nur Doppelklick oder Tab landet im Edit-Mode.
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 200, Height = 40,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 400, Height = 200, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 400, Height = 200, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var textEditor = frame.FindControl<TextBox>("TextEditor");
        Assert.NotNull(textEditor);
        textEditor!.Focus(NavigationMethod.Pointer);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsInEditMode);

        window.Close();
    }

    [AvaloniaFact]
    public void DragBorder_IsTabStopFalse_TabSkipsToTextEditor()
    {
        // DragBorder darf nicht im Tab-Cycle hängen — sonst landet der User
        // auf einem unsichtbaren Border statt direkt im Eingabefeld.
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 200, Height = 40,
        });

        var frame = new TextFieldFrame { DataContext = vm };
        vm.IsSelected = true; // Chrome (Border + Handles) ist nur am selektierten Feld sichtbar/hit-testbar.
        var canvas = new Canvas { Width = 400, Height = 200, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        var window = new Window { Width = 400, Height = 200, Content = canvas };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var dragBorder = frame.FindControl<Border>("DragBorder");
        Assert.NotNull(dragBorder);
        Assert.False(dragBorder!.IsTabStop);

        window.Close();
    }
}
