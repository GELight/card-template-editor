using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

public class ExportCurrentSetTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _exportDir;
    private readonly TemplateRepository _repo;
    private readonly SettingsService _settings;

    public ExportCurrentSetTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ExpCur_" + Guid.NewGuid());
        _exportDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_exportDir);
        _repo = new TemplateRepository(_tempDir);
        _settings = new SettingsService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestPng(string name)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(40, 30), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.Lime, null, new Rect(0, 0, 40, 30));
        }
        bmp.Save(path);
        return path;
    }

    [AvaloniaFact]
    public void ExportCurrentSet_WithSelectedTextset_AppliesValues_AndWritesPerSlotFile()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: _settings);
        var t = mw.CreateNewTemplate();
        t.Name = "MyTemplate";
        var src1 = CreateTestPng("front.png");
        var src2 = CreateTestPng("back.png");
        var s1 = mw.AddImage(src1)!;
        s1.Name = "vorderseite";
        var s2 = mw.AddImage(src2)!;
        s2.Name = "rueckseite";
        var f = mw.AddTextFieldToCurrentSlot()!;
        f.Name = "titel";
        f.CurrentText = "alt";
        var set = mw.AddNewTextset()!;
        set.Name = "Glurak";
        set.SetValue("titel", "neu");

        var written = mw.ExportCurrentSet(_exportDir);

        Assert.Equal(2, written.Count);
        Assert.Contains(written, p => Path.GetFileName(p) == "MyTemplate_Glurak_vorderseite.png");
        Assert.Contains(written, p => Path.GetFileName(p) == "MyTemplate_Glurak_rueckseite.png");
        Assert.All(written, p => Assert.True(File.Exists(p)));
        // ApplyTextset wurde aufgerufen → Wert wurde aktualisiert
        Assert.Equal("neu", f.CurrentText);
        // LastExportDirectory wurde persistiert
        Assert.Equal(_exportDir, mw.LastExportDirectory);
        Assert.Equal(_exportDir, _settings.Load().LastExportDirectory);
    }

    [AvaloniaFact]
    public void ExportCurrentSet_WithoutTextset_UsesCurrentTextValues_AndCurrentSetPlaceholder()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: _settings);
        var t = mw.CreateNewTemplate();
        t.Name = "T";
        var src = CreateTestPng("front.png");
        var slot = mw.AddImage(src)!;
        slot.Name = "image";

        var written = mw.ExportCurrentSet(_exportDir);

        Assert.Single(written);
        Assert.Equal("T_current_image.png", Path.GetFileName(written[0]));
    }

    [Fact]
    public void ExportCurrentSet_WithoutTemplate_ReturnsEmpty()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: _settings);

        var written = mw.ExportCurrentSet(_exportDir);

        Assert.Empty(written);
    }

    [Fact]
    public void FileNamePattern_PersistsThroughSettingsService()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: _settings);
        mw.FileNamePattern = "{index}-{template}-{image}";

        var loaded = _settings.Load();
        Assert.Equal("{index}-{template}-{image}", loaded.FileNamePattern);
    }
}
