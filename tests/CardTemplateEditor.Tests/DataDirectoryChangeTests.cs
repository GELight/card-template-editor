using CardTemplateEditor.Models;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

/// <summary>
/// Verhalten beim Wechsel des Daten-Verzeichnisses:
/// - settings.json wird im (Test-)Default-Pfad geschrieben/gelesen.
/// - Der vorherige aktive Pfad landet in PreviousDataDirectories (außer er
///   war der Default — dann ist er impliziter Fallback).
/// - BuildRepositoryFromSettings übersetzt AppSettings konsistent in einen
///   TemplateRepository mit aktivem Root + Fallback-Liste.
/// </summary>
public class DataDirectoryChangeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsDir;
    private readonly string _rootA;
    private readonly string _rootB;

    public DataDirectoryChangeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DataDirChange_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _settingsDir = Path.Combine(_tempDir, "settings");
        _rootA = Path.Combine(_tempDir, "rootA");
        _rootB = Path.Combine(_tempDir, "rootB");
        Directory.CreateDirectory(_settingsDir);
        Directory.CreateDirectory(_rootA);
        Directory.CreateDirectory(_rootB);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ChangeDataDirectory_SetsActivePath_AndPushesPreviousToHistory()
    {
        // Initial: aktiv = rootA, kein History-Eintrag.
        var settingsService = new SettingsService(_settingsDir);
        settingsService.Save(new AppSettings { DataDirectory = _rootA });

        var repo = new TemplateRepository(_rootA);
        var vm = new MainWindowViewModel(repo, settingsService: settingsService);

        Assert.Equal(_rootA, vm.CurrentDataDirectory);

        vm.ChangeDataDirectory(_rootB);

        var reloaded = settingsService.Load();
        Assert.Equal(_rootB, reloaded.DataDirectory);
        Assert.Contains(_rootA, reloaded.PreviousDataDirectories);
        Assert.Equal(_rootB, vm.CurrentDataDirectory);
    }

    [Fact]
    public void ChangeDataDirectory_SamePath_NoHistoryChange()
    {
        var settingsService = new SettingsService(_settingsDir);
        settingsService.Save(new AppSettings { DataDirectory = _rootA });

        var repo = new TemplateRepository(_rootA);
        var vm = new MainWindowViewModel(repo, settingsService: settingsService);

        vm.ChangeDataDirectory(_rootA);

        var reloaded = settingsService.Load();
        Assert.Empty(reloaded.PreviousDataDirectories);
        Assert.Equal(_rootA, reloaded.DataDirectory);
    }

    [Fact]
    public void ChangeDataDirectory_BackToFormerPath_RemovesItFromHistory()
    {
        // A → B → A: der Eintrag A darf nicht in der History bleiben, sonst
        // würde das Repository A doppelt als Fallback aufnehmen (harmlos,
        // aber unsauber). B muss als History-Eintrag drin sein, weil B
        // jetzt der "alte" aktive Pfad ist.
        var settingsService = new SettingsService(_settingsDir);
        settingsService.Save(new AppSettings { DataDirectory = _rootA });

        var repo = new TemplateRepository(_rootA);
        var vm = new MainWindowViewModel(repo, settingsService: settingsService);

        vm.ChangeDataDirectory(_rootB);
        vm.ChangeDataDirectory(_rootA);

        var reloaded = settingsService.Load();
        Assert.Equal(_rootA, reloaded.DataDirectory);
        Assert.DoesNotContain(_rootA, reloaded.PreviousDataDirectories);
        Assert.Contains(_rootB, reloaded.PreviousDataDirectories);
    }

    [Fact]
    public void BuildRepositoryFromSettings_NoOverride_UsesDefaultDir()
    {
        var settings = new AppSettings();
        var repo = MainWindowViewModel.BuildRepositoryFromSettings(settings);

        Assert.Equal(TemplateRepository.DefaultDataDir, repo.ActiveRoot);
        Assert.Single(repo.AllRoots);
    }

    [Fact]
    public void BuildRepositoryFromSettings_OverrideActive_DefaultBecomesImplicitFallback()
    {
        // Setting nicht-default → Default muss automatisch in der Fallback-
        // Liste landen, sonst verschwinden beim ersten Wechsel die original
        // erstellten Templates aus dem Default-Ordner.
        var settings = new AppSettings { DataDirectory = _rootA };
        var repo = MainWindowViewModel.BuildRepositoryFromSettings(settings);

        Assert.Equal(_rootA, repo.ActiveRoot);
        Assert.Contains(TemplateRepository.DefaultDataDir, repo.AllRoots);
    }

    [Fact]
    public void BuildRepositoryFromSettings_HistoryAndDefault_Combined()
    {
        var settings = new AppSettings
        {
            DataDirectory = _rootA,
            PreviousDataDirectories = new List<string> { _rootB },
        };
        var repo = MainWindowViewModel.BuildRepositoryFromSettings(settings);

        Assert.Equal(_rootA, repo.ActiveRoot);
        Assert.Contains(_rootB, repo.AllRoots);
        Assert.Contains(TemplateRepository.DefaultDataDir, repo.AllRoots);
    }

    [Fact]
    public void BuildRepositoryFromSettings_HistoryContainsActive_NotDuplicated()
    {
        // Edge: aktiv und in History — typisch nach manueller Settings-Edit.
        // Erwartung: AllRoots enthält den Pfad nur einmal.
        var settings = new AppSettings
        {
            DataDirectory = _rootA,
            PreviousDataDirectories = new List<string> { _rootA, _rootB },
        };
        var repo = MainWindowViewModel.BuildRepositoryFromSettings(settings);

        Assert.Equal(_rootA, repo.ActiveRoot);
        Assert.Equal(1, repo.AllRoots.Count(r => string.Equals(r, _rootA)));
    }
}
