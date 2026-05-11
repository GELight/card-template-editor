using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;
using CardTemplateEditor.Views;
using CardTemplateEditor.Views.Controls;

namespace CardTemplateEditor.Tests;

public class EditableImageCanvasClickTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateRepository _repo;

    public EditableImageCanvasClickTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EicClick_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _repo = new TemplateRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestPng(string name, int w = 100, int h = 80)
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

    [AvaloniaFact]
    public void ClickingAnEmptyImageContainer_SelectsThatSlot_NotTheOther()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var slot1 = mw.AddImage(CreateTestPng("a.png"))!;
        var slot2 = mw.AddImage(CreateTestPng("b.png"))!;
        // AddImage selektiert automatisch den letzten — slot2 ist initial aktiv.
        Assert.Same(slot2, mw.SelectedImageSlot);

        var window = new MainWindow { Width = 1200, Height = 700, DataContext = mw };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // slot1 liegt links in der UniformGrid-Spalte, slot2 rechts.
        // Wir treffen jeweils einen Punkt im linken bzw. rechten Drittel des
        // Center-Bereichs — das landet in der jeweiligen EditableImageCanvas.
        var leftClick = new Point(450, 360);   // sollte slot1 sein
        var rightClick = new Point(900, 360);  // sollte slot2 sein

        // Erst auf slot1 klicken
        window.MouseDown(leftClick, MouseButton.Left);
        window.MouseUp(leftClick, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Same(slot1, mw.SelectedImageSlot);
        Assert.True(slot1.IsSelected, "slot1 sollte nach Klick auf slot1 IsSelected=true haben.");
        Assert.False(slot2.IsSelected, "slot2 sollte nach Klick auf slot1 IsSelected=false haben.");

        // Dann auf slot2 zurück
        window.MouseDown(rightClick, MouseButton.Left);
        window.MouseUp(rightClick, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Same(slot2, mw.SelectedImageSlot);
        Assert.True(slot2.IsSelected);
        Assert.False(slot1.IsSelected);

        window.Close();
    }

    [AvaloniaFact]
    public void Image_StaysWithinSlotBounds_WhenWindowShrinks()
    {
        // Regression: User-Report — beim Verkleinern des Fensters rutschten
        // die Bilder nach unten aus dem sichtbaren Bereich. Fix: Center-
        // Alignment auf LayoutTransformControl + explizites InvalidateMeasure
        // auf Resize. Dieser Test stellt sicher, dass die transformierte
        // Layout-Größe nach einem Window-Shrink in den Viewport passt
        // (kein Über-/Unterhang) und dass das Bild mittig sitzt.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        // Großes Bild (1200×900) — bei Auto-Fit sollte es immer in den Slot
        // passen. Ein Shrink darf das Verhältnis nicht brechen.
        mw.AddImage(CreateTestPng("big.png", 1200, 900));

        var window = new MainWindow { Width = 1400, Height = 900, DataContext = mw };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var canvas = window.GetVisualDescendants().OfType<EditableImageCanvas>().Single();
        var scroller = canvas.GetVisualDescendants()
            .OfType<ScrollViewer>().First(s => s.Name == "ZoomScroller");
        var layoutHost = canvas.GetVisualDescendants()
            .OfType<Avalonia.Controls.LayoutTransformControl>()
            .First(l => l.Name == "ZoomLayoutHost");

        // Shrink: sehr klein, damit Auto-Fit deutlich zugreifen muss.
        window.Width = 600;
        window.Height = 400;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Erwartung: Der gerenderte Inhalt (Bounds des LayoutTransformControl
        // im ScrollViewer-Koordinatenraum) muss innerhalb des Viewports
        // liegen — sonst rutscht das Bild aus dem sichtbaren Bereich.
        var hostBounds = layoutHost.Bounds;
        var viewport = scroller.Viewport;
        Assert.True(hostBounds.Width  <= viewport.Width  + 1.5,
            $"LayoutHost.Width {hostBounds.Width} > Viewport.Width {viewport.Width} — Bild ist zu breit für den Slot.");
        Assert.True(hostBounds.Height <= viewport.Height + 1.5,
            $"LayoutHost.Height {hostBounds.Height} > Viewport.Height {viewport.Height} — Bild ist zu hoch für den Slot.");

        window.Close();
    }

    [AvaloniaFact]
    public void FocusedContainer_HasVisibleBorder_UnfocusedHasTransparent()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("a.png"));
        mw.AddImage(CreateTestPng("b.png"));

        var window = new MainWindow { Width = 1200, Height = 700, DataContext = mw };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Beide EditableImageCanvas-Instanzen finden.
        var canvases = window.GetVisualDescendants()
            .OfType<EditableImageCanvas>()
            .ToList();
        Assert.Equal(2, canvases.Count);

        Border BorderOf(EditableImageCanvas c) =>
            c.GetVisualDescendants().OfType<Border>().First(b => b.Name == "OuterBorder");

        // Welcher der beiden Slots ist initial selektiert? Egal welcher — der
        // selektierte muss eine sichtbare (≠ Transparent) BorderBrush haben,
        // der andere Transparent.
        var selected = canvases.First(c => ((ImageSlotViewModel)c.DataContext!).IsSelected);
        var unselected = canvases.First(c => !((ImageSlotViewModel)c.DataContext!).IsSelected);

        var selectedBrush = BorderOf(selected).BorderBrush;
        var unselectedBrush = BorderOf(unselected).BorderBrush;

        Assert.NotEqual(Brushes.Transparent, selectedBrush);
        Assert.Equal(Brushes.Transparent, unselectedBrush);

        // Selektion umschalten — Borders müssen sich entsprechend tauschen.
        mw.SelectedImageSlot = (ImageSlotViewModel)unselected.DataContext!;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(Brushes.Transparent, BorderOf(unselected).BorderBrush);
        Assert.Equal(Brushes.Transparent, BorderOf(selected).BorderBrush);

        window.Close();
    }
}
