using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

/// <summary>
/// Tests für die Bild-gruppierte Darstellung der Textset-Werte im Editor-Panel.
/// Jeder ImageSlot wird zu einer Gruppe; Felder gleichen Namens in zwei Slots
/// erscheinen in beiden Gruppen, teilen sich aber den Wert (kommt aus dem Set).
/// </summary>
public class MainWindowViewModelTextsetGroupingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateRepository _repo;

    public MainWindowViewModelTextsetGroupingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MwVmGrp_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _repo = new TemplateRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestPng(string name)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(2, 2), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 2, 2));
        }
        bmp.Save(path);
        return path;
    }

    [AvaloniaFact]
    public void RebuildGroups_NoSelectedTextset_ReturnsEmptyList()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        mw.AddImage(CreateTestPng("front.png"));
        mw.AddTextFieldToCurrentSlot();

        // Kein Textset ausgewählt → keine Gruppen.
        Assert.Empty(mw.SelectedTextsetGroups);
    }

    [AvaloniaFact]
    public void RebuildGroups_TwoSlotsTwoFieldsEach_ReturnsTwoGroupsWithTwoEntries()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();

        var s1 = mw.AddImage(CreateTestPng("front.png"))!;
        s1.Name = "Vorderseite";
        var f1a = mw.AddTextFieldToCurrentSlot()!; f1a.Name = "titel";
        var f1b = mw.AddTextFieldToCurrentSlot()!; f1b.Name = "untertitel";

        var s2 = mw.AddImage(CreateTestPng("back.png"))!;
        s2.Name = "Rückseite";
        // Neue Felder landen am aktuell ausgewählten Slot — AddImage selektiert
        // den neuen Slot automatisch, daher gehen die nächsten Felder an s2.
        var f2a = mw.AddTextFieldToCurrentSlot()!; f2a.Name = "regel";
        var f2b = mw.AddTextFieldToCurrentSlot()!; f2b.Name = "fußnote";

        var set = mw.AddNewTextset()!;
        mw.SelectedTextset = set;

        Assert.Equal(2, mw.SelectedTextsetGroups.Count);

        var g1 = mw.SelectedTextsetGroups[0];
        Assert.Equal("Vorderseite", g1.SlotName);
        Assert.Equal(new[] { "titel", "untertitel" }, g1.Entries.Select(e => e.FieldName).ToArray());

        var g2 = mw.SelectedTextsetGroups[1];
        Assert.Equal("Rückseite", g2.SlotName);
        Assert.Equal(new[] { "regel", "fußnote" }, g2.Entries.Select(e => e.FieldName).ToArray());
    }

    [AvaloniaFact]
    public void RebuildGroups_FieldNameInBothSlots_AppearsInBothGroupsWithSharedValue()
    {
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();

        var s1 = mw.AddImage(CreateTestPng("front.png"))!;
        s1.Name = "Vorderseite";
        var f1 = mw.AddTextFieldToCurrentSlot()!; f1.Name = "name";

        var s2 = mw.AddImage(CreateTestPng("back.png"))!;
        s2.Name = "Rückseite";
        var f2 = mw.AddTextFieldToCurrentSlot()!; f2.Name = "name"; // gleicher Name wie s1

        var set = mw.AddNewTextset()!;
        mw.SelectedTextset = set;

        Assert.Equal(2, mw.SelectedTextsetGroups.Count);
        Assert.Equal("name", mw.SelectedTextsetGroups[0].Entries.Single().FieldName);
        Assert.Equal("name", mw.SelectedTextsetGroups[1].Entries.Single().FieldName);

        // Wert in einer Gruppe ändern → andere Gruppe spiegelt den neuen Wert
        // wider, weil beide aus demselben Set-Modell-Eintrag lesen.
        mw.SelectedTextsetGroups[0].Entries.Single().Value = "Glurak";
        Assert.Equal("Glurak", mw.SelectedTextsetGroups[1].Entries.Single().Value);
    }

    [AvaloniaFact]
    public void RebuildGroups_SlotWithoutNamedFields_IsSkipped()
    {
        // Ein Slot, dessen einziges TextField keinen Namen hat, soll keine
        // Gruppe erzeugen — sonst zeigt der Editor leere Header.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();

        var s1 = mw.AddImage(CreateTestPng("front.png"))!;
        s1.Name = "Vorderseite";
        var f1 = mw.AddTextFieldToCurrentSlot()!; f1.Name = "titel";

        var s2 = mw.AddImage(CreateTestPng("back.png"))!;
        s2.Name = "Rückseite";
        var f2 = mw.AddTextFieldToCurrentSlot()!;
        f2.Name = ""; // Default ist "Textfeld1", aber für diesen Test soll es leer sein

        var set = mw.AddNewTextset()!;
        // Re-Select, damit der Group-Rebuild auf den finalen Stand reagiert.
        mw.SelectedTextset = null;
        mw.SelectedTextset = set;

        Assert.Single(mw.SelectedTextsetGroups);
        Assert.Equal("Vorderseite", mw.SelectedTextsetGroups[0].SlotName);
    }

    [AvaloniaFact]
    public void RebuildGroups_DuplicateNameWithinSameSlot_IsCollapsedToOneEntry()
    {
        // Zwei Felder mit demselben Namen im selben Slot → eine Eingabezeile.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();

        var s1 = mw.AddImage(CreateTestPng("front.png"))!;
        s1.Name = "Vorderseite";
        var f1a = mw.AddTextFieldToCurrentSlot()!; f1a.Name = "titel";
        var f1b = mw.AddTextFieldToCurrentSlot()!; f1b.Name = "titel";

        var set = mw.AddNewTextset()!;
        mw.SelectedTextset = set;

        Assert.Single(mw.SelectedTextsetGroups);
        Assert.Single(mw.SelectedTextsetGroups[0].Entries);
        Assert.Equal("titel", mw.SelectedTextsetGroups[0].Entries[0].FieldName);
    }

    [AvaloniaFact]
    public void RebuildGroups_RenamingFieldUpdatesEntries()
    {
        // Wird ein Feldname nach Auswahl des Sets geändert, soll der Editor
        // die Gruppe neu aufbauen — die OnDirty-PropertyChanged-Verkabelung
        // im VM kümmert sich darum.
        var mw = new MainWindowViewModel(_repo);
        mw.CreateNewTemplate();
        var slot = mw.AddImage(CreateTestPng("front.png"))!;
        slot.Name = "Vorderseite";
        var field = mw.AddTextFieldToCurrentSlot()!;
        field.Name = "old";

        var set = mw.AddNewTextset()!;
        mw.SelectedTextset = set;

        Assert.Equal("old", mw.SelectedTextsetGroups[0].Entries.Single().FieldName);

        field.Name = "new";

        Assert.Equal("new", mw.SelectedTextsetGroups[0].Entries.Single().FieldName);
    }
}
