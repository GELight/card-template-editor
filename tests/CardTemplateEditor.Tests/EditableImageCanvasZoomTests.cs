using Avalonia;
using CardTemplateEditor.Views.Controls;

namespace CardTemplateEditor.Tests;

/// <summary>
/// Reine Mathe-Tests für die Zoom-Hilfsfunktionen — keine UI-Layout nötig.
/// Schützt die Maus-Anker- und Auto-Fit-Berechnungen gegen Refactor-Drift.
/// </summary>
public class EditableImageCanvasZoomTests
{
    [Fact]
    public void ComputeEffectiveScale_FitsImageInViewport_AtZoomFactor1()
    {
        // 1000×500 Bild in 800×600 Viewport: Auto-Fit-Faktor = 800/1000 = 0.8.
        // Mit User-ZoomFactor = 1.0 → EffectiveScale = 0.8.
        var s = EditableImageCanvas.ComputeEffectiveScale(800, 600, 1000, 500, 1.0);
        Assert.Equal(0.8, s, precision: 6);
    }

    [Fact]
    public void ComputeEffectiveScale_TallImage_LimitedByHeight()
    {
        // 100×1000 Bild in 800×600: Auto-Fit-Faktor = min(800/100, 600/1000) = 0.6.
        var s = EditableImageCanvas.ComputeEffectiveScale(800, 600, 100, 1000, 1.0);
        Assert.Equal(0.6, s, precision: 6);
    }

    [Fact]
    public void ComputeEffectiveScale_UserZoomMultiplies()
    {
        // Auto-Fit 0.8, User-Zoom 2 → 1.6.
        var s = EditableImageCanvas.ComputeEffectiveScale(800, 600, 1000, 500, 2.0);
        Assert.Equal(1.6, s, precision: 6);
    }

    [Fact]
    public void ComputeEffectiveScale_UserZoomBelowOne_ShrinksBelowAutoFit()
    {
        // Auto-Fit 0.8, User-Zoom 0.5 → 0.4 (User darf unter Auto-Fit zoomen).
        var s = EditableImageCanvas.ComputeEffectiveScale(800, 600, 1000, 500, 0.5);
        Assert.Equal(0.4, s, precision: 6);
    }

    [Fact]
    public void ComputeEffectiveScale_DegenerateInputs_DoesNotDivideByZero()
    {
        // Layout noch nicht gemessen → 0 als Viewport. Soll user-Zoom direkt
        // zurückgeben (kein Crash, kein 0).
        var s = EditableImageCanvas.ComputeEffectiveScale(0, 0, 1000, 500, 1.0);
        Assert.Equal(1.0, s, precision: 6);

        // Bild ohne Pixel-Dimensionen → ebenfalls keine Division durch 0.
        s = EditableImageCanvas.ComputeEffectiveScale(800, 600, 0, 0, 1.5);
        Assert.Equal(1.5, s, precision: 6);
    }

    [Fact]
    public void ComputeMouseAnchoredScrollOffset_KeepsCursorOverSameContentPoint()
    {
        // Vor dem Zoom: scroll bei (100, 50), cursor bei (200, 150) im Viewport,
        // alte Skala = 1.0 → contentPoint = (300, 200) in Bild-Pixeln.
        // Neue Skala = 2.0: derselbe contentPoint muss wieder unter dem Cursor
        // landen → newOffset = (300*2 - 200, 200*2 - 150) = (400, 250).
        var newOffset = EditableImageCanvas.ComputeMouseAnchoredScrollOffset(
            oldOffset: new Point(100, 50),
            cursorInViewport: new Point(200, 150),
            oldScale: 1.0,
            newScale: 2.0);
        Assert.Equal(400, newOffset.X, precision: 6);
        Assert.Equal(250, newOffset.Y, precision: 6);
    }

    [Fact]
    public void ComputeMouseAnchoredScrollOffset_ZoomingOut_ProducesValidOffset()
    {
        // Beim Zoomen RAUS (kleinere Skala) müsste der neue Offset kleiner
        // werden, damit derselbe Bild-Pixel weiter unter dem Cursor sitzt.
        var newOffset = EditableImageCanvas.ComputeMouseAnchoredScrollOffset(
            oldOffset: new Point(400, 200),
            cursorInViewport: new Point(100, 100),
            oldScale: 2.0,
            newScale: 1.0);
        // contentPoint vor Zoom = (250, 150). Nach Zoom: 250*1 - 100 = 150,
        // 150*1 - 100 = 50.
        Assert.Equal(150, newOffset.X, precision: 6);
        Assert.Equal(50, newOffset.Y, precision: 6);
    }

    [Fact]
    public void ComputeMouseAnchorFromContentPos_KeepsPixelUnderCursor()
    {
        // Robusterer Pfad: cursorInContent wird direkt aus
        // PointerEventArgs.GetPosition(ContentRoot) gelesen — da sind die
        // Layout-Transforms schon von Avalonia rausgerechnet, also haben wir
        // den Bild-Pixel unter dem Cursor in PixelW/PixelH-Koordinaten.
        // Anker-Mathe: scrollOffset_neu = pixelPos × newScale − cursorInViewport
        var newOffset = EditableImageCanvas.ComputeMouseAnchorFromContentPos(
            cursorInContent: new Point(500, 300),
            cursorInViewport: new Point(100, 100),
            newScale: 0.8);
        // 500*0.8 = 400, − 100 = 300. 300*0.8 = 240, − 100 = 140.
        Assert.Equal(300, newOffset.X, precision: 6);
        Assert.Equal(140, newOffset.Y, precision: 6);
    }

    [Fact]
    public void ComputeMouseAnchorFromContentPos_ZoomingInOnUpperRight_ProducesPositiveOffset()
    {
        // User zeigt auf den oberen rechten Bereich des Bildes und zoomt rein.
        // Pixel (900, 50) bei neuer Skala 2.0 → (1800, 100). Cursor sitzt im
        // Viewport bei (700, 50). Damit der Pixel unter dem Cursor bleibt,
        // muss der Scroll-Offset (1800-700, 100-50) = (1100, 50) sein.
        var newOffset = EditableImageCanvas.ComputeMouseAnchorFromContentPos(
            cursorInContent: new Point(900, 50),
            cursorInViewport: new Point(700, 50),
            newScale: 2.0);
        Assert.Equal(1100, newOffset.X, precision: 6);
        Assert.Equal(50, newOffset.Y, precision: 6);
    }

    // -----------------------------------------------------------------------
    // Drag-Pan-Mathematik: Middle-Mouse-Drag verschiebt den ScrollViewer-
    // Offset gegen die Bewegungsrichtung des Cursors. Die Funktion clampt
    // den Offset auf den scrollbaren Bereich [0, Extent − Viewport], damit
    // sich der Inhalt nicht in den negativen Bereich schieben lässt.
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputePannedScrollOffset_CursorRight_OffsetMovesLeft()
    {
        // User zieht den Cursor um 50px nach RECHTS. Damit der Bildpixel
        // unter dem Cursor bleibt (Drag-Pan-Verhalten = Inhalt mit Cursor
        // mitziehen), muss der Scroll-Offset um 50px nach LINKS:
        // newX = startX − Δx = 200 − 50 = 150.
        var result = EditableImageCanvas.ComputePannedScrollOffset(
            startOffset: new Vector(200, 100),
            startCursor: new Point(0, 0),
            currentCursor: new Point(50, 0),
            extent: new Size(2000, 2000),
            viewport: new Size(800, 600));
        Assert.Equal(150, result.X, precision: 6);
        Assert.Equal(100, result.Y, precision: 6);
    }

    [Fact]
    public void ComputePannedScrollOffset_ClampsToZero_WhenDraggingPastTopLeft()
    {
        // Cursor bewegt sich weit über die Start-Position hinaus; Offset würde
        // negativ, soll aber auf 0 begrenzt werden — sonst zeigt der Scroller
        // einen Inhalt außerhalb der Content-Bounds.
        var result = EditableImageCanvas.ComputePannedScrollOffset(
            startOffset: new Vector(50, 30),
            startCursor: new Point(100, 100),
            currentCursor: new Point(500, 300),
            extent: new Size(2000, 2000),
            viewport: new Size(800, 600));
        Assert.Equal(0, result.X, precision: 6);
        Assert.Equal(0, result.Y, precision: 6);
    }

    [Fact]
    public void ComputePannedScrollOffset_ClampsToMax_WhenDraggingPastBottomRight()
    {
        // Cursor bewegt sich weit nach LINKS/OBEN; Offset würde über
        // (extent − viewport) hinausgehen, soll auf das Maximum geclamped
        // werden — Max = (2000−800, 2000−600) = (1200, 1400).
        var result = EditableImageCanvas.ComputePannedScrollOffset(
            startOffset: new Vector(1000, 1000),
            startCursor: new Point(500, 500),
            currentCursor: new Point(0, 0),
            extent: new Size(2000, 2000),
            viewport: new Size(800, 600));
        Assert.Equal(1200, result.X, precision: 6);
        Assert.Equal(1400, result.Y, precision: 6);
    }

    [Fact]
    public void ComputePannedScrollOffset_NoMovement_ReturnsStartOffset()
    {
        // Cursor steht still → Offset bleibt unverändert.
        var result = EditableImageCanvas.ComputePannedScrollOffset(
            startOffset: new Vector(123, 456),
            startCursor: new Point(50, 50),
            currentCursor: new Point(50, 50),
            extent: new Size(2000, 2000),
            viewport: new Size(800, 600));
        Assert.Equal(123, result.X, precision: 6);
        Assert.Equal(456, result.Y, precision: 6);
    }

    [Fact]
    public void ComputePannedScrollOffset_NotScrollable_ReturnsZero()
    {
        // Wenn Inhalt komplett in Viewport passt (extent <= viewport), darf
        // sich der Offset gar nicht ändern — clamp auf [0, 0].
        var result = EditableImageCanvas.ComputePannedScrollOffset(
            startOffset: new Vector(0, 0),
            startCursor: new Point(0, 0),
            currentCursor: new Point(100, 100),
            extent: new Size(800, 600),
            viewport: new Size(800, 600));
        Assert.Equal(0, result.X, precision: 6);
        Assert.Equal(0, result.Y, precision: 6);
    }
}
