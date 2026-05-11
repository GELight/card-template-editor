using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;
using CardTemplateEditor.Views.Controls;

namespace CardTemplateEditor.Tests;

/// <summary>
/// EditableImageCanvas pusht den aktuellen LayoutTransform-Scale auf jedes
/// TextFieldViewModel seines Slots — dadurch bleiben Borders + Handles in
/// Screen-Pixeln konstant (1 / scale in Bild-Pixel-Coords).
/// </summary>
public class EditableImageCanvasEffectiveScaleTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateRepository _repo;

    public EditableImageCanvasEffectiveScaleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EicScale_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _repo = new TemplateRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestPng(string name, int w, int h)
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
    public void AfterLayout_ExistingTextField_HasEffectiveScaleMatchingCanvas()
    {
        // 1000×500 Bild in einem 400×300 Canvas → Auto-Fit verkleinert das
        // Bild deutlich. Das TextField-VM des Slots muss den errechneten
        // Scale-Faktor erben, damit seine Handle-Größen entsprechend skalieren.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("front.png", 1000, 500));
        var field = mw.AddTextFieldToCurrentSlot()!;

        var editor = new EditableImageCanvas
        {
            DataContext = mw.SelectedImageSlot,
            Width = 400, Height = 300,
        };
        var window = new Window { Width = 400, Height = 300, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.EffectiveScale > 0);
        Assert.True(editor.EffectiveScale < 1.0,
            $"Bei 1000×500 in 400×300 Container muss Auto-Fit < 1 sein, war {editor.EffectiveScale}.");
        Assert.Equal(editor.EffectiveScale, field.EffectiveScale, precision: 6);

        window.Close();
    }

    [AvaloniaFact]
    public void NewTextField_AddedAfterLayout_InheritsCurrentEffectiveScale()
    {
        // Ein TextField, das NACH dem Layout-Pass hinzugefügt wird, soll den
        // aktuellen Scale ebenfalls erben — sonst hätte es Default 1.0 und
        // sein Border wäre direkt nach dem Anlegen sichtbar zu dünn/dick.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("front.png", 2000, 1000));

        var editor = new EditableImageCanvas
        {
            DataContext = mw.SelectedImageSlot,
            Width = 400, Height = 300,
        };
        var window = new Window { Width = 400, Height = 300, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var scaleAfterLayout = editor.EffectiveScale;
        Assert.True(scaleAfterLayout > 0 && scaleAfterLayout < 1.0);

        var newField = mw.AddTextFieldToCurrentSlot()!;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(scaleAfterLayout, newField.EffectiveScale, precision: 6);

        window.Close();
    }

    [AvaloniaFact]
    public void ZoomChange_PropagatesToTextFieldEffectiveScale()
    {
        // User dreht am Zoom-Rad → ScaleTransform wird aktualisiert → die
        // TextField-VMs müssen den neuen Faktor erhalten, damit Handles
        // ihre Screen-Pixel-Größe halten.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var slot = mw.AddImage(CreateTestPng("front.png", 1000, 500))!;
        var field = mw.AddTextFieldToCurrentSlot()!;

        var editor = new EditableImageCanvas
        {
            DataContext = slot,
            Width = 400, Height = 300,
        };
        var window = new Window { Width = 400, Height = 300, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var initialScale = field.EffectiveScale;

        slot.ZoomFactor = 2.0;
        Dispatcher.UIThread.RunJobs();

        // ZoomFactor=2 verdoppelt den Scale: TextField-VM soll mitziehen.
        Assert.Equal(initialScale * 2.0, field.EffectiveScale, precision: 6);

        window.Close();
    }
}
