using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

public class EdgeCaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _exportDir;
    private readonly TemplateRepository _repo;
    private readonly SettingsService _settings;

    public EdgeCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Edge_" + Guid.NewGuid());
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

    private string CreatePng(string name)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(10, 10), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.Yellow, null, new Rect(0, 0, 10, 10));
        }
        bmp.Save(path);
        return path;
    }

    [AvaloniaFact]
    public async Task BatchExport_TemplateWithoutImageSlots_ProducesNothing_NoException()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: _settings);
        mw.CreateNewTemplate();
        mw.AddNewTextset();

        var written = await mw.BatchExportAsync(_exportDir);

        Assert.Empty(written);
    }

    [AvaloniaFact]
    public async Task BatchExport_SlotWithoutFile_IsSkipped_NoException()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: _settings);
        var t = mw.CreateNewTemplate();
        // Slot direkt am Modell mit FileName="" anhängen, ohne ImportImage.
        t.Model.ImageSlots.Add(new ImageSlot { Name = "leer", FileName = "" });
        mw.AddNewTextset();
        await mw.FlushAutoSaveAsync();

        var written = await mw.BatchExportAsync(_exportDir);

        Assert.Empty(written);
    }

    [AvaloniaFact]
    public async Task BatchExport_SlotWithMissingPhysicalFile_IsSkipped_NoException()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: _settings);
        var t = mw.CreateNewTemplate();
        t.Model.ImageSlots.Add(new ImageSlot { Name = "ghost", FileName = "ghost.png" });
        // Datei wird absichtlich NICHT angelegt.
        mw.AddNewTextset();
        await mw.FlushAutoSaveAsync();

        var written = await mw.BatchExportAsync(_exportDir);

        Assert.Empty(written);
    }

    [AvaloniaFact]
    public void ApplyTextset_WithUnknownKeys_LeavesUnknownFieldsUntouched()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: _settings);
        var t = mw.CreateNewTemplate();
        var src = CreatePng("front.png");
        mw.AddImage(src);
        var f = mw.AddTextFieldToCurrentSlot()!;
        f.Name = "titel";
        f.CurrentText = "alt";

        var set = mw.AddNewTextset()!;
        set.SetValue("vollkommen-unbekannt", "X");
        set.SetValue("noch-eins", "Y");

        mw.ApplySelectedTextsetCommand.Execute(null);

        // titel hat keinen passenden Eintrag im Set → bleibt "alt"
        Assert.Equal("alt", f.CurrentText);
    }
}
