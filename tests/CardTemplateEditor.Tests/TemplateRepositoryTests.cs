using CardTemplateEditor.Models;
using CardTemplateEditor.Services;

namespace CardTemplateEditor.Tests;

public class TemplateRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateRepository _repo;

    public TemplateRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CardTplTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _repo = new TemplateRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SaveAndLoad_RoundtripsAllFields()
    {
        var slotId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var template = new Template
        {
            Id = Guid.NewGuid(),
            Name = "Pokemon",
            ImageSlots = new List<ImageSlot>
            {
                new() { Id = slotId, Name = "Vorderseite", FileName = "front.png" },
            },
            TextFields = new List<TextField>
            {
                new()
                {
                    Id = fieldId,
                    ImageSlotId = slotId,
                    Name = "titel",
                    X = 10, Y = 20, Width = 100, Height = 30,
                    FontFamily = "Arial",
                    FontSize = 24,
                    FontWeight = "Bold",
                    Color = "#112233",
                    CurrentText = "Glurak",
                },
            },
            Textsets = new List<Textset>
            {
                new()
                {
                    Id = setId,
                    Name = "Set A",
                    Values = new Dictionary<string, string> { ["titel"] = "Glurak" },
                },
            },
        };

        _repo.SaveTemplate(template);
        var loaded = _repo.LoadTemplate(template.Id);

        Assert.NotNull(loaded);
        Assert.Equal(template.Id, loaded!.Id);
        Assert.Equal("Pokemon", loaded.Name);

        var slot = Assert.Single(loaded.ImageSlots);
        Assert.Equal(slotId, slot.Id);
        Assert.Equal("Vorderseite", slot.Name);
        Assert.Equal("front.png", slot.FileName);

        var field = Assert.Single(loaded.TextFields);
        Assert.Equal(fieldId, field.Id);
        Assert.Equal(slotId, field.ImageSlotId);
        Assert.Equal("titel", field.Name);
        Assert.Equal(10, field.X);
        Assert.Equal(20, field.Y);
        Assert.Equal(100, field.Width);
        Assert.Equal(30, field.Height);
        Assert.Equal("Arial", field.FontFamily);
        Assert.Equal(24, field.FontSize);
        Assert.Equal("Bold", field.FontWeight);
        Assert.Equal("#112233", field.Color);
        Assert.Equal("Glurak", field.CurrentText);

        var set = Assert.Single(loaded.Textsets);
        Assert.Equal(setId, set.Id);
        Assert.Equal("Set A", set.Name);
        Assert.Equal("Glurak", set.Values["titel"]);
    }

    [Fact]
    public void SaveAndLoad_RoundtripsRotationAndCornerOffsets()
    {
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            Name = "Warp",
            ImageSlots = new List<ImageSlot> { new() { Id = slotId, FileName = "x.png" } },
            TextFields = new List<TextField>
            {
                new()
                {
                    ImageSlotId = slotId,
                    X = 0, Y = 0, Width = 50, Height = 30,
                    Rotation = 42.5,
                    CornerNWdx = 1, CornerNWdy = 2,
                    CornerNEdx = 3, CornerNEdy = 4,
                    CornerSEdx = -5, CornerSEdy = 6,
                    CornerSWdx = 7, CornerSWdy = -8,
                },
            },
        };

        _repo.SaveTemplate(template);
        var loaded = _repo.LoadTemplate(template.Id);

        var f = Assert.Single(loaded!.TextFields);
        Assert.Equal(42.5, f.Rotation);
        Assert.Equal(1, f.CornerNWdx);
        Assert.Equal(2, f.CornerNWdy);
        Assert.Equal(3, f.CornerNEdx);
        Assert.Equal(4, f.CornerNEdy);
        Assert.Equal(-5, f.CornerSEdx);
        Assert.Equal(6, f.CornerSEdy);
        Assert.Equal(7, f.CornerSWdx);
        Assert.Equal(-8, f.CornerSWdy);
    }

    [Fact]
    public void SaveAndLoad_RoundtripsRotationOriginRelXY()
    {
        // Iteration 14 Sub-Task D: der verschiebbare Drehpunkt muss
        // persistiert werden, sonst verliert der User seine Einstellung
        // beim nächsten App-Start.
        var slotId = Guid.NewGuid();
        var template = new Template
        {
            Name = "Origin",
            ImageSlots = new List<ImageSlot> { new() { Id = slotId, FileName = "y.png" } },
            TextFields = new List<TextField>
            {
                new()
                {
                    ImageSlotId = slotId,
                    X = 10, Y = 20, Width = 100, Height = 50,
                    Rotation = 30,
                    RotationOriginRelX = 0.25,
                    RotationOriginRelY = 0.75,
                },
            },
        };

        _repo.SaveTemplate(template);
        var loaded = _repo.LoadTemplate(template.Id);

        var f = Assert.Single(loaded!.TextFields);
        Assert.Equal(0.25, f.RotationOriginRelX);
        Assert.Equal(0.75, f.RotationOriginRelY);
    }

    [Fact]
    public void LoadTemplate_ReturnsNull_WhenMissing()
    {
        var result = _repo.LoadTemplate(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public void ListTemplates_ReturnsAllSavedTemplates()
    {
        var a = new Template { Name = "A" };
        var b = new Template { Name = "B" };
        _repo.SaveTemplate(a);
        _repo.SaveTemplate(b);

        var ids = _repo.ListTemplates().Select(t => t.Id).ToHashSet();
        Assert.Contains(a.Id, ids);
        Assert.Contains(b.Id, ids);
    }

    [Fact]
    public void ListTemplates_OnEmptyDir_ReturnsEmpty()
    {
        Assert.Empty(_repo.ListTemplates());
    }

    [Fact]
    public void ImportImage_CopiesFileWithSlotIdAndExtension()
    {
        var templateId = Guid.NewGuid();
        var slotId = Guid.NewGuid();
        _repo.SaveTemplate(new Template { Id = templateId });

        var src = Path.Combine(_tempDir, "src.png");
        File.WriteAllBytes(src, new byte[] { 1, 2, 3 });

        var fileName = _repo.ImportImage(templateId, slotId, src);

        Assert.Equal(slotId + ".png", fileName);
        var dest = _repo.GetImagePath(templateId, fileName);
        Assert.True(File.Exists(dest));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(dest));
    }

    [Theory]
    [InlineData("photo.jpg", ".jpg")]
    [InlineData("photo.jpeg", ".jpeg")]
    [InlineData("photo.PNG", ".png")]
    [InlineData("photo.JPG", ".jpg")]
    [InlineData("noext", ".png")]
    public void ImportImage_PreservesLowercasedExtension(string sourceName, string expectedExt)
    {
        var templateId = Guid.NewGuid();
        var slotId = Guid.NewGuid();
        _repo.SaveTemplate(new Template { Id = templateId });

        var src = Path.Combine(_tempDir, sourceName);
        File.WriteAllBytes(src, new byte[] { 9, 9 });

        var fileName = _repo.ImportImage(templateId, slotId, src);

        Assert.Equal(slotId + expectedExt, fileName);
        Assert.True(File.Exists(_repo.GetImagePath(templateId, fileName)));
    }

    [Fact]
    public void DeleteTemplate_RemovesDirectory()
    {
        var template = new Template { Name = "X" };
        _repo.SaveTemplate(template);
        Assert.True(Directory.Exists(_repo.TemplateDir(template.Id)));

        _repo.DeleteTemplate(template.Id);

        Assert.False(Directory.Exists(_repo.TemplateDir(template.Id)));
    }

    // ------------------------------------------------------------------
    // Multi-Root-Verhalten (Datenverzeichnis-Wechsel: alt = Fallback,
    // neu = aktiv). Zentraler Use-Case: User stellt den Pfad um, alte
    // Templates müssen weiter sichtbar sein, neue landen im neuen Pfad.
    // ------------------------------------------------------------------

    [Fact]
    public void ListTemplates_IncludesTemplatesFromFallbackRoot()
    {
        var oldRoot = Path.Combine(_tempDir, "old");
        var newRoot = Path.Combine(_tempDir, "new");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);

        var oldRepo = new TemplateRepository(oldRoot);
        var oldTemplate = new Template { Name = "Old" };
        oldRepo.SaveTemplate(oldTemplate);

        var repo = new TemplateRepository(newRoot, new[] { oldRoot });
        var ids = repo.ListTemplates().Select(t => t.Id).ToList();

        Assert.Contains(oldTemplate.Id, ids);
    }

    [Fact]
    public void SaveTemplate_NewTemplate_LandsInActiveRoot()
    {
        var oldRoot = Path.Combine(_tempDir, "old");
        var newRoot = Path.Combine(_tempDir, "new");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);

        var repo = new TemplateRepository(newRoot, new[] { oldRoot });
        var t = new Template { Name = "FreshlyCreated" };
        repo.SaveTemplate(t);

        Assert.True(File.Exists(Path.Combine(newRoot, "templates", t.Id.ToString(), "template.json")));
        Assert.False(File.Exists(Path.Combine(oldRoot, "templates", t.Id.ToString(), "template.json")));
    }

    [Fact]
    public void SaveTemplate_LoadedFromFallback_StaysInFallbackRoot()
    {
        // Kritisch: ein aus dem Default-/Alt-Root geladenes Template darf
        // beim AutoSave NICHT in den neuen aktiven Root abwandern — sonst
        // hinterließe der User nach jeder Bearbeitung einen verwaisten
        // Ordner am alten Ort und seine Bilder fänden den Bezug nicht mehr.
        var oldRoot = Path.Combine(_tempDir, "old");
        var newRoot = Path.Combine(_tempDir, "new");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);

        var oldRepo = new TemplateRepository(oldRoot);
        var oldTemplate = new Template { Name = "Old" };
        oldRepo.SaveTemplate(oldTemplate);

        var repo = new TemplateRepository(newRoot, new[] { oldRoot });
        var loaded = repo.ListTemplates().Single(t => t.Id == oldTemplate.Id);
        loaded.Name = "Old, geändert";
        repo.SaveTemplate(loaded);

        var oldFile = Path.Combine(oldRoot, "templates", loaded.Id.ToString(), "template.json");
        var newFile = Path.Combine(newRoot, "templates", loaded.Id.ToString(), "template.json");
        Assert.True(File.Exists(oldFile), "Save sollte das Template im ursprünglichen Root behalten.");
        Assert.False(File.Exists(newFile), "Save darf das Template NICHT in den aktiven Root duplizieren.");
    }

    [Fact]
    public void ImportImage_ForFallbackTemplate_StoresImageInFallbackRoot()
    {
        // Wenn ein neues Bild zu einem Template hinzugefügt wird, das aus
        // dem Fallback-Root stammt, muss die Bilddatei dort bei seinem
        // template.json landen — sonst klaffen JSON und Bild auseinander.
        var oldRoot = Path.Combine(_tempDir, "old");
        var newRoot = Path.Combine(_tempDir, "new");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);

        var oldRepo = new TemplateRepository(oldRoot);
        var oldTemplate = new Template { Name = "Old" };
        oldRepo.SaveTemplate(oldTemplate);

        var repo = new TemplateRepository(newRoot, new[] { oldRoot });
        var loaded = repo.ListTemplates().Single(t => t.Id == oldTemplate.Id);

        var src = Path.Combine(_tempDir, "src.png");
        File.WriteAllBytes(src, new byte[] { 9, 9, 9 });
        var slotId = Guid.NewGuid();
        var fileName = repo.ImportImage(loaded.Id, slotId, src);

        var oldImagePath = Path.Combine(oldRoot, "templates", loaded.Id.ToString(), "images", fileName);
        Assert.True(File.Exists(oldImagePath));
    }

    [Fact]
    public void ListTemplates_DuplicatesAcrossRoots_ActiveWins()
    {
        // Edge-Case: derselbe Template-Guid existiert (unbeabsichtigt) in
        // mehreren Roots. Erwartung: der aktive Root gewinnt — sein Stand
        // ist der jüngere "Source of Truth".
        var oldRoot = Path.Combine(_tempDir, "old");
        var newRoot = Path.Combine(_tempDir, "new");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);

        var sharedId = Guid.NewGuid();
        new TemplateRepository(oldRoot).SaveTemplate(new Template { Id = sharedId, Name = "Alt" });
        new TemplateRepository(newRoot).SaveTemplate(new Template { Id = sharedId, Name = "Neu" });

        var repo = new TemplateRepository(newRoot, new[] { oldRoot });
        var listed = repo.ListTemplates().Where(t => t.Id == sharedId).ToList();

        Assert.Single(listed);
        Assert.Equal("Neu", listed[0].Name);
    }
}
