using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

/// <summary>
/// Vollständiger End-to-End-Smoke ohne UI: Template → Bild → TextField → Textset →
/// Batch-Export. Verifiziert, dass die Module zusammenspielen.
/// </summary>
public class EndToEndSmokeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _exportDir;
    private readonly TemplateRepository _repo;
    private readonly SettingsService _settings;

    public EndToEndSmokeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "E2ESmoke_" + Guid.NewGuid());
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

    private string CreatePng(string name, int w, int h, IBrush color)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(color, null, new Rect(0, 0, w, h));
        }
        bmp.Save(path);
        return path;
    }

    [AvaloniaFact]
    public async Task FullFlow_TwoSlots_TwoSets_FourPngsExported_AndPersisted()
    {
        var mw = new MainWindowViewModel(
            _repo,
            autoSaveDebounce: TimeSpan.FromMilliseconds(20),
            settingsService: _settings);

        // 1. Template anlegen
        var t = mw.CreateNewTemplate();
        t.Name = "pokemon";

        // 2. zwei Bilder hochladen
        var front = CreatePng("front.png", 30, 30, Brushes.Red);
        var back = CreatePng("back.png", 30, 30, Brushes.Green);
        var s1 = mw.AddImage(front)!;
        s1.Name = "vorderseite";
        var s2 = mw.AddImage(back)!;
        s2.Name = "rueckseite";

        // 3. Textfelder anlegen
        mw.SelectedImageSlot = s1;
        var fTitel = mw.AddTextFieldToCurrentSlot()!;
        fTitel.Name = "titel";
        fTitel.X = 4; fTitel.Y = 4; fTitel.Width = 22; fTitel.Height = 12;
        mw.SelectedImageSlot = s2;
        var fHp = mw.AddTextFieldToCurrentSlot()!;
        fHp.Name = "hp";
        fHp.X = 4; fHp.Y = 4; fHp.Width = 22; fHp.Height = 12;

        // 4. zwei Textsets
        var glurak = mw.AddNewTextset()!;
        glurak.Name = "glurak";
        glurak.SetValue("titel", "Glurak");
        glurak.SetValue("hp", "120");
        var pikachu = mw.AddNewTextset()!;
        pikachu.Name = "pikachu";
        pikachu.SetValue("titel", "Pikachu");
        pikachu.SetValue("hp", "60");

        // 5. AutoSave durchläuft → persistiert
        await mw.FlushAutoSaveAsync();
        var loaded = _repo.LoadTemplate(t.Id)!;
        Assert.Equal(2, loaded.ImageSlots.Count);
        Assert.Equal(2, loaded.TextFields.Count);
        Assert.Equal(2, loaded.Textsets.Count);

        // 6. Batch-Export
        var written = await mw.BatchExportAsync(_exportDir);

        Assert.Equal(4, written.Count);
        var names = written.Select(Path.GetFileName).ToHashSet();
        Assert.Contains("pokemon_glurak_vorderseite.png", names);
        Assert.Contains("pokemon_glurak_rueckseite.png", names);
        Assert.Contains("pokemon_pikachu_vorderseite.png", names);
        Assert.Contains("pokemon_pikachu_rueckseite.png", names);

        // Original-CurrentText wurde durch Batch nicht überschrieben.
        // (Wenn der User nichts manuell gesetzt hat, war das "" — und ist es immer noch.)
        Assert.Equal("", t.Model.TextFields[0].CurrentText);
    }
}
