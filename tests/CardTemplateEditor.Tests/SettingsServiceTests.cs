using CardTemplateEditor.Models;
using CardTemplateEditor.Services;

namespace CardTemplateEditor.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _svc;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CardSettingsTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _svc = new SettingsService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileMissing()
    {
        var settings = _svc.Load();
        Assert.Equal("{template}_{set}_{image}", settings.FileNamePattern);
        Assert.Null(settings.LastExportDirectory);
        Assert.Null(settings.LastTemplateId);
    }

    [Fact]
    public void SaveAndLoad_RoundtripsAllFields()
    {
        var id = Guid.NewGuid();
        var original = new AppSettings
        {
            LastExportDirectory = "/tmp/cards",
            FileNamePattern = "{set}-{image}",
            LastTemplateId = id,
        };

        _svc.Save(original);
        var loaded = _svc.Load();

        Assert.Equal("/tmp/cards", loaded.LastExportDirectory);
        Assert.Equal("{set}-{image}", loaded.FileNamePattern);
        Assert.Equal(id, loaded.LastTemplateId);
    }

    [Fact]
    public void Load_OnCorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(_svc.SettingsFile, "{not valid json");
        var settings = _svc.Load();
        Assert.Equal("{template}_{set}_{image}", settings.FileNamePattern);
    }

    [Fact]
    public void SaveAndLoad_PersistsDataDirectoryAndHistory()
    {
        // Roundtrip der neuen Felder, damit ein Pfad-Override über Restarts
        // hinweg erhalten bleibt — sonst verlöre der User seine Einstellung
        // und landete beim nächsten Start wieder im Default.
        var settings = new AppSettings
        {
            DataDirectory = "/mnt/storage/cards",
            PreviousDataDirectories = new List<string>
            {
                "/old/path/one",
                "/old/path/two",
            },
        };

        _svc.Save(settings);
        var loaded = _svc.Load();

        Assert.Equal("/mnt/storage/cards", loaded.DataDirectory);
        Assert.Equal(new[] { "/old/path/one", "/old/path/two" }, loaded.PreviousDataDirectories);
    }

    [Fact]
    public void Load_DefaultsForOldFile_HasNullDataDirAndEmptyHistory()
    {
        // Migration: existierende Settings-Files (v0) haben die neuen Felder
        // nicht. Beim Laden müssen sie auf Defaults fallen, sonst kracht es
        // beim ersten Start mit der neuen App-Version.
        File.WriteAllText(
            _svc.SettingsFile,
            "{\"fileNamePattern\":\"{template}_{set}_{image}\"}");

        var loaded = _svc.Load();

        Assert.Null(loaded.DataDirectory);
        Assert.NotNull(loaded.PreviousDataDirectories);
        Assert.Empty(loaded.PreviousDataDirectories);
    }
}
