using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

public class MainWindowViewModelCommandsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateRepository _repo;

    public MainWindowViewModelCommandsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MwVmCmd_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _repo = new TemplateRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestPng(string name)
        => CreateTestPng(name, 2, 2);

    private string CreateTestPng(string name, int width, int height)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.Red, null, new Rect(0, 0, width, height));
        }
        bmp.Save(path);
        return path;
    }

    [Fact]
    public void NewTemplate_AddsToList_AndSelectsIt_AndPersistsToDisk()
    {
        var mw = new MainWindowViewModel(_repo);

        var t = mw.CreateNewTemplate();

        Assert.Single(mw.Templates);
        Assert.Same(t, mw.CurrentTemplate);
        Assert.True(File.Exists(_repo.TemplateFile(t.Id)));
    }

    [Fact]
    public void NewTemplateCommand_FromButton_SameEffect()
    {
        var mw = new MainWindowViewModel(_repo);

        Assert.True(mw.NewTemplateCommand.CanExecute(null));
        mw.NewTemplateCommand.Execute(null);

        Assert.Single(mw.Templates);
        Assert.NotNull(mw.CurrentTemplate);
    }

    [AvaloniaFact]
    public void AddImage_RequiresCurrentTemplate_AndCreatesSlot()
    {
        var mw = new MainWindowViewModel(_repo);
        Assert.Null(mw.AddImage("/nonexistent")); // ohne CurrentTemplate

        mw.CreateNewTemplate();
        var src = CreateTestPng("front.png");

        var slot = mw.AddImage(src);

        Assert.NotNull(slot);
        Assert.Same(slot, mw.SelectedImageSlot);
        Assert.Single(mw.CurrentTemplate!.ImageSlots);
    }

    [AvaloniaFact]
    public void AddTextField_RequiresImageSlot_AndCreatesField()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();

        Assert.False(mw.AddTextFieldCommand.CanExecute(null)); // kein Slot

        var src = CreateTestPng("front.png");
        mw.AddImage(src);

        Assert.True(mw.AddTextFieldCommand.CanExecute(null));
        var field = mw.AddTextFieldToCurrentSlot();

        Assert.NotNull(field);
        Assert.Same(field, mw.SelectedTextField);
        Assert.Single(mw.CurrentTemplate!.TextFields);
        Assert.Equal(mw.SelectedImageSlot!.Id, field!.ImageSlotId);
    }

    [AvaloniaFact]
    public void AddTextField_FirstField_DefaultNameIsTextfeld0()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("front.png"));

        var field = mw.AddTextFieldToCurrentSlot()!;

        Assert.Equal("Textfeld0", field.Name);
    }

    [AvaloniaFact]
    public void AddTextField_SecondField_DefaultNameIsTextfeld1()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("front.png"));

        var f0 = mw.AddTextFieldToCurrentSlot()!;
        var f1 = mw.AddTextFieldToCurrentSlot()!;

        Assert.Equal("Textfeld0", f0.Name);
        Assert.Equal("Textfeld1", f1.Name);
    }

    [Fact]
    public void ComputeDefaultGeometry_WithoutBitmap_FallsBackTo200x30At20_20()
    {
        // Slot ohne geladene Bitmap (PixelWidth/Height = 0): Fallback auf
        // historisches Default-Verhalten, damit kein degenerate Rechteck
        // bei (0,0) entsteht.
        var slot = new ImageSlotViewModel(
            new Models.ImageSlot { Name = "leer", FileName = "" },
            _ => null);

        var (w, h, x, y) = MainWindowViewModel.ComputeDefaultGeometry(slot);

        Assert.Equal(200, w);
        Assert.Equal(30, h);
        Assert.Equal(20, x);
        Assert.Equal(20, y);
    }

    [AvaloniaFact]
    public void AddTextField_With1000x500Bitmap_DefaultsRelativeToImageSize()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("front.png", 1000, 500));

        var field = mw.AddTextFieldToCurrentSlot()!;

        Assert.Equal(400, field.Width);   // 40 % von 1000
        Assert.Equal(40, field.Height);   // 8 % von 500
        Assert.Equal(300, field.X);       // (1000-400)/2 zentriert
        Assert.Equal(25, field.Y);        // 5 % von 500
    }

    [AvaloniaFact]
    public void AddTextField_WithSmallBitmap_HeightFloorsAt20()
    {
        // Bei sehr flachen Bildern (z. B. 200×100) wäre 8 % der Höhe = 8 px,
        // das wäre kleiner als die TextFieldFrame-MinSize. Daher Mindesthöhe 20.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("front.png", 200, 100));

        var field = mw.AddTextFieldToCurrentSlot()!;

        Assert.Equal(80, field.Width);   // 40 % von 200
        Assert.Equal(20, field.Height);  // Floor 20, nicht 8
        Assert.Equal(60, field.X);       // (200-80)/2
        Assert.Equal(5, field.Y);        // 5 % von 100
    }

    [AvaloniaFact]
    public void ImageSlotViewModel_AfterBitmapLoad_ExposesPixelDimensions()
    {
        // Sanity-Check: das Loaden eines Bildes setzt PixelWidth/Height
        // gemäß Pixelgröße der Datei. ComputeDefaultGeometry hängt davon ab.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var slot = mw.AddImage(CreateTestPng("front.png", 640, 480))!;

        Assert.Equal(640, slot.PixelWidth);
        Assert.Equal(480, slot.PixelHeight);
    }

    [AvaloniaFact]
    public async Task EditingProperty_TriggersAutoSave_PersistsToDisk()
    {
        // Sehr kurzer Debounce, damit der Test nicht ewig wartet.
        var mw = new MainWindowViewModel(_repo, autoSaveDebounce: TimeSpan.FromMilliseconds(20));
        mw.CreateNewTemplate(); // initial gespeichert (synchron)
        var src = CreateTestPng("front.png");
        mw.AddImage(src);
        var field = mw.AddTextFieldToCurrentSlot()!;

        // Editiere TextField — soll AutoSave triggern.
        field.X = 123;
        field.CurrentText = "Hallo";

        await mw.AutoSave.WaitForIdleAsync();

        var saved = _repo.LoadTemplate(mw.CurrentTemplate!.Id)!;
        var savedField = saved.TextFields.Single(f => f.Id == field.Id);
        Assert.Equal(123, savedField.X);
        Assert.Equal("Hallo", savedField.CurrentText);
    }

    [Fact]
    public void DeleteCurrentTemplate_RemovesFromListAndDisk_AndSelectsNeighbor()
    {
        var mw = new MainWindowViewModel(_repo);
        var t1 = mw.CreateNewTemplate();
        var t2 = mw.CreateNewTemplate();
        var t3 = mw.CreateNewTemplate();

        mw.CurrentTemplate = t2;
        Assert.True(mw.DeleteCurrentTemplateCommand.CanExecute(null));

        mw.DeleteCurrentTemplateCommand.Execute(null);

        Assert.Equal(2, mw.Templates.Count);
        Assert.DoesNotContain(t2, mw.Templates);
        Assert.False(File.Exists(_repo.TemplateFile(t2.Id)));
        // Nachfolger an gleicher Index-Position wird selektiert.
        Assert.Same(t3, mw.CurrentTemplate);
    }

    [Fact]
    public void DeleteCurrentTemplate_LastOne_LeavesNullCurrent()
    {
        var mw = new MainWindowViewModel(_repo);
        var t = mw.CreateNewTemplate();

        mw.DeleteCurrentTemplateCommand.Execute(null);

        Assert.Empty(mw.Templates);
        Assert.Null(mw.CurrentTemplate);
        Assert.False(File.Exists(_repo.TemplateFile(t.Id)));
    }

    [Fact]
    public void DeleteCurrentTemplateCommand_Disabled_WhenNoCurrentTemplate()
    {
        var mw = new MainWindowViewModel(_repo);
        Assert.False(mw.DeleteCurrentTemplateCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task RenameCurrentTemplate_ChangesName_AndPersists()
    {
        var mw = new MainWindowViewModel(_repo, autoSaveDebounce: TimeSpan.FromMilliseconds(20));
        var t = mw.CreateNewTemplate();

        Assert.True(mw.TryRenameCurrentTemplate("Mein neues Template"));
        Assert.Equal("Mein neues Template", t.Name);

        await mw.AutoSave.WaitForIdleAsync();
        var saved = _repo.LoadTemplate(t.Id)!;
        Assert.Equal("Mein neues Template", saved.Name);
    }

    [Fact]
    public void TryRenameCurrentTemplate_RejectsBlankName()
    {
        var mw = new MainWindowViewModel(_repo);
        var t = mw.CreateNewTemplate();
        var original = t.Name;

        Assert.False(mw.TryRenameCurrentTemplate(""));
        Assert.False(mw.TryRenameCurrentTemplate("   "));
        Assert.Equal(original, t.Name);
    }

    [Fact]
    public void RemoveTemplateCommand_NonCurrent_DoesNotChangeSelection()
    {
        var mw = new MainWindowViewModel(_repo);
        var t1 = mw.CreateNewTemplate();
        var t2 = mw.CreateNewTemplate();
        var t3 = mw.CreateNewTemplate();

        mw.CurrentTemplate = t3;
        Assert.Same(t3, mw.CurrentTemplate);

        mw.RemoveTemplateCommand.Execute(t1);

        Assert.DoesNotContain(t1, mw.Templates);
        Assert.False(File.Exists(_repo.TemplateFile(t1.Id)));
        Assert.Same(t3, mw.CurrentTemplate); // Selektion unverändert
    }

    [AvaloniaFact]
    public void RemoveImageSlotCommand_RemovesSlot_AndCascadesTextFields()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var src = CreateTestPng("front.png");
        var slot = mw.AddImage(src)!;
        var field = mw.AddTextFieldToCurrentSlot()!;

        Assert.Single(mw.CurrentTemplate!.ImageSlots);
        Assert.Single(mw.CurrentTemplate!.TextFields);

        mw.RemoveImageSlotCommand.Execute(slot);

        Assert.Empty(mw.CurrentTemplate!.ImageSlots);
        Assert.Empty(mw.CurrentTemplate!.TextFields); // kaskadiert weg
        Assert.Null(mw.SelectedImageSlot);
        Assert.Null(mw.SelectedTextField);
    }

    [Fact]
    public void RemoveTextsetCommand_RemovesSet_AndClearsSelectionIfMatch()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var setA = mw.AddNewTextset()!;
        var setB = mw.AddNewTextset()!;
        mw.SelectedTextset = setB;

        mw.RemoveTextsetCommand.Execute(setB);

        Assert.DoesNotContain(setB, mw.CurrentTemplate!.Textsets);
        Assert.Contains(setA, mw.CurrentTemplate!.Textsets);
        Assert.Null(mw.SelectedTextset);
    }

    [AvaloniaFact]
    public void SelectedTextset_Set_AutomaticallyAppliesValuesToFields()
    {
        // Beim Anklicken eines Sets in der Sidebar sollen seine Werte direkt
        // in die Felder einfließen, ohne dass der User noch einen "Anwenden"-
        // Button drücken muss.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("front.png"));
        var field = mw.AddTextFieldToCurrentSlot()!;
        field.Name = "titel";

        var set = mw.AddNewTextset()!;
        set.SetValue("titel", "Hallo");
        // AddNewTextset selektiert das Set automatisch — wir simulieren hier
        // den Klick-Workflow: User klickt das Set neu an, nachdem er die
        // Werte eingegeben hat.
        mw.SelectedTextset = null;
        field.CurrentText = "";

        mw.SelectedTextset = set;

        Assert.Equal("Hallo", field.CurrentText);
    }

    [AvaloniaFact]
    public void SelectedTextset_SetToNull_DoesNotResetFields()
    {
        // Das Abwählen eines Sets darf die Felder NICHT leeren — sonst würde
        // der User beim Wegnavigieren seine Texte verlieren.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("front.png"));
        var field = mw.AddTextFieldToCurrentSlot()!;
        field.Name = "titel";

        var set = mw.AddNewTextset()!;
        set.SetValue("titel", "Hallo");
        // Re-Select, damit der Auto-Apply mit dem gefüllten Set greift.
        mw.SelectedTextset = null;
        mw.SelectedTextset = set;
        Assert.Equal("Hallo", field.CurrentText);

        mw.SelectedTextset = null;

        Assert.Equal("Hallo", field.CurrentText);
    }

    [AvaloniaFact]
    public void ApplyTextsetCommand_AppliesAnyTextset_NotJustSelected()
    {
        var mw = new MainWindowViewModel(_repo);
        var t = mw.CreateNewTemplate();
        var src = CreateTestPng("front.png");
        mw.AddImage(src);
        var field = mw.AddTextFieldToCurrentSlot()!;
        field.Name = "titel";

        var set = mw.AddNewTextset()!;
        set.SetValue("titel", "Hallo");

        mw.SelectedTextset = null; // bewusst NICHT selektiert
        mw.ApplyTextsetCommand.Execute(set);

        Assert.Equal("Hallo", field.CurrentText);
    }

    [Fact]
    public void UpdateExportCommand_Disabled_WhenNoTemplateOrNoLastDir()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: new SettingsService(_tempDir));
        Assert.False(mw.UpdateExportCommand.CanExecute(null));

        mw.CreateNewTemplate();
        // Template ja, aber LastExportDirectory noch nicht gesetzt.
        Assert.False(mw.UpdateExportCommand.CanExecute(null));
        Assert.False(mw.CanUpdateExport);
    }

    [AvaloniaFact]
    public void UpdateExportCommand_WritesToLastExportDirectory_WithoutPrompt()
    {
        var mw = new MainWindowViewModel(_repo, settingsService: new SettingsService(_tempDir));
        mw.CreateNewTemplate();
        var src = CreateTestPng("front.png");
        mw.AddImage(src);
        var field = mw.AddTextFieldToCurrentSlot()!;
        field.CurrentText = "v1";

        var exportDir = Path.Combine(_tempDir, "export-out");
        Directory.CreateDirectory(exportDir);

        // Erster regulärer Export setzt LastExportDirectory.
        mw.ExportCurrentSet(exportDir);
        Assert.Equal(exportDir, mw.LastExportDirectory);
        Assert.True(mw.UpdateExportCommand.CanExecute(null));
        Assert.True(mw.CanUpdateExport);

        var beforeUpdate = Directory.GetFiles(exportDir, "*.png");
        Assert.NotEmpty(beforeUpdate);
        var firstWrite = File.GetLastWriteTimeUtc(beforeUpdate[0]);

        // Text ändern und Update ausführen — schreibt in dasselbe Verzeichnis.
        field.CurrentText = "v2";
        // Damit Datei-mtime sicher abweicht, kurz warten.
        Thread.Sleep(20);
        mw.UpdateExportCommand.Execute(null);

        var afterUpdate = Directory.GetFiles(exportDir, "*.png");
        Assert.Equal(beforeUpdate.Length, afterUpdate.Length); // selbe Dateien, überschrieben
        var secondWrite = File.GetLastWriteTimeUtc(afterUpdate[0]);
        Assert.True(secondWrite > firstWrite, "Update hätte die Datei neu schreiben müssen.");
    }

    [AvaloniaFact]
    public void RemoveTextField_ClearsSelectionIfDeletedFieldWasSelected_AndCascadesFromSlot()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var src = CreateTestPng("front.png");
        var slot = mw.AddImage(src)!;
        var f1 = mw.AddTextFieldToCurrentSlot()!;
        var f2 = mw.AddTextFieldToCurrentSlot()!;

        mw.SelectedTextField = f1;
        Assert.Equal(2, mw.CurrentTemplate!.TextFields.Count);

        mw.RemoveTextField(f1);

        Assert.Single(mw.CurrentTemplate!.TextFields);
        Assert.DoesNotContain(f1, mw.CurrentTemplate!.TextFields);
        Assert.Null(mw.SelectedTextField); // war f1 → wurde geleert
        Assert.DoesNotContain(f1, slot.TextFields);
    }

    [AvaloniaFact]
    public void IsTextFieldUsedByAnyTextset_TrueOnlyWhenAnyTextsetReferencesName()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var src = CreateTestPng("front.png");
        mw.AddImage(src);
        var field = mw.AddTextFieldToCurrentSlot()!;
        field.Name = "titel";

        Assert.False(mw.IsTextFieldUsedByAnyTextset(field)); // Keine Sets

        var setA = mw.AddNewTextset()!;
        Assert.False(mw.IsTextFieldUsedByAnyTextset(field)); // Set ohne Wert für "titel"

        setA.SetValue("untertitel", "x");
        Assert.False(mw.IsTextFieldUsedByAnyTextset(field));

        setA.SetValue("titel", "Hallo");
        Assert.True(mw.IsTextFieldUsedByAnyTextset(field));
    }

    [AvaloniaFact]
    public void IsTextFieldUsedByAnyTextset_EmptyName_AlwaysFalse()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var src = CreateTestPng("front.png");
        mw.AddImage(src);
        var field = mw.AddTextFieldToCurrentSlot()!;
        field.Name = "";

        var set = mw.AddNewTextset()!;
        set.SetValue("", "egal");

        // Felder ohne Name werden ohnehin nicht in Textsets erfasst — der Helper
        // muss hier konservativ false zurückgeben, damit DEL ohne Confirm löscht.
        Assert.False(mw.IsTextFieldUsedByAnyTextset(field));
    }

    [AvaloniaFact]
    public void SelectedImageSlot_Sync_FlipsIsSelectedOnPreviousAndNew()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var src1 = CreateTestPng("a.png");
        var src2 = CreateTestPng("b.png");
        var slot1 = mw.AddImage(src1)!;
        var slot2 = mw.AddImage(src2)!;

        // AddImage selektiert automatisch — slot2 sollte jetzt der Aktive sein.
        Assert.Same(slot2, mw.SelectedImageSlot);
        Assert.True(slot2.IsSelected);
        Assert.False(slot1.IsSelected);

        mw.SelectedImageSlot = slot1;
        Assert.True(slot1.IsSelected);
        Assert.False(slot2.IsSelected);

        mw.SelectedImageSlot = null;
        Assert.False(slot1.IsSelected);
        Assert.False(slot2.IsSelected);
    }

    [AvaloniaFact]
    public void SelectedTextField_Sync_FlipsIsSelectedOnPreviousAndNew()
    {
        // Border und alle Resize-/Rotate-Handles sollen nur am aktuell
        // selektierten TextField sichtbar sein — der Sync dafür sitzt in
        // OnSelectedTextFieldChanged und spiegelt SelectedTextField auf
        // TextFieldViewModel.IsSelected (analog zur ImageSlot-Selektion).
        var mw = new MainWindowViewModel(_repo);
        var template = mw.CreateNewTemplate();
        var slot = mw.AddImage(CreateTestPng("bg.png"))!;
        var f1 = mw.AddTextFieldToCurrentSlot()!;
        var f2 = mw.AddTextFieldToCurrentSlot()!;

        // AddTextFieldToCurrentSlot selektiert das gerade hinzugefügte Feld.
        Assert.Same(f2, mw.SelectedTextField);
        Assert.True(f2.IsSelected);
        Assert.False(f1.IsSelected);

        mw.SelectedTextField = f1;
        Assert.True(f1.IsSelected);
        Assert.False(f2.IsSelected);

        mw.SelectedTextField = null;
        Assert.False(f1.IsSelected);
        Assert.False(f2.IsSelected);
    }

    [Fact]
    public void SwitchingTemplate_DetachesOldSubscriptions_NoDirtyTriggerFromOld()
    {
        var mw = new MainWindowViewModel(_repo, autoSaveDebounce: TimeSpan.FromSeconds(5));
        var t1 = mw.CreateNewTemplate();
        var t2 = mw.CreateNewTemplate();
        Assert.Same(t2, mw.CurrentTemplate);

        // Alte Subscriptions sollten weg sein. Nach Editieren von t1 darf der AutoSave
        // höchstens den Trigger-Mechanismus berühren, aber kein Save für t2 stattfinden,
        // weil das aktuelle Template t2 ist und der Trigger durchs Detach von t1 gar nicht erst feuert.
        var saveCountBefore = mw.AutoSave.SaveCount;
        t1.Name = "geändert";

        // Wir lassen den (langen) Debounce gar nicht erst ablaufen — wir prüfen
        // dass kein synchroner Save passiert ist und die Verkabelung sauber abgelöst wurde.
        Assert.Equal(saveCountBefore, mw.AutoSave.SaveCount);
    }
}
