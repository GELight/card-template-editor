using CardTemplateEditor.Models;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

public class TextsetTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateRepository _repo;

    public TextsetTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TextsetTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _repo = new TemplateRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private TemplateViewModel MakeTemplate(out Guid slotId)
    {
        slotId = Guid.NewGuid();
        var model = new Template
        {
            Name = "T",
            ImageSlots = { new ImageSlot { Id = slotId, Name = "Front" } },
        };
        return new TemplateViewModel(model, _repo);
    }

    [Fact]
    public void TextsetViewModel_NameChange_FiresPropertyChanged()
    {
        var vm = new TextsetViewModel(new Textset { Name = "A" });
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Name = "A"; // unchanged → kein Event
        vm.Name = "B";

        Assert.Single(raised);
        Assert.Equal(nameof(TextsetViewModel.Name), raised[0]);
        Assert.Equal("B", vm.Model.Name);
    }

    [Fact]
    public void TextsetViewModel_SetValue_FiresValuesNotification_WritesThroughToModel()
    {
        var vm = new TextsetViewModel(new Textset { Name = "A" });
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SetValue("titel", "Glurak");
        vm.SetValue("titel", "Glurak"); // unchanged → kein Event
        vm.SetValue("titel", "Pikachu");

        Assert.Equal("Pikachu", vm.GetValue("titel"));
        Assert.Equal("Pikachu", vm.Model.Values["titel"]);
        Assert.Equal(2, raised.Count);
        Assert.All(raised, p => Assert.Equal(nameof(TextsetViewModel.Values), p));
    }

    [Fact]
    public void TextsetViewModel_GetValue_OnUnknownKey_ReturnsEmptyString()
    {
        var vm = new TextsetViewModel(new Textset());
        Assert.Equal("", vm.GetValue("unbekannt"));
    }

    [Fact]
    public void AddTextset_AppearsInTemplateAndModel_AndAutoNumbersOnDuplicate()
    {
        var vm = MakeTemplate(out _);

        var a = vm.AddTextset();          // "Set"
        var b = vm.AddTextset();          // "Set 2"
        var c = vm.AddTextset("Set");     // "Set 3"
        var d = vm.AddTextset("Eigen");   // "Eigen"
        var e = vm.AddTextset("Eigen");   // "Eigen 2"

        Assert.Equal(new[] { "Set", "Set 2", "Set 3", "Eigen", "Eigen 2" },
            new[] { a.Name, b.Name, c.Name, d.Name, e.Name });
        Assert.Equal(5, vm.Textsets.Count);
        Assert.Equal(5, vm.Model.Textsets.Count);
    }

    [Fact]
    public void AddTextset_BlankBaseName_FallsBackToDefault()
    {
        var vm = MakeTemplate(out _);

        var a = vm.AddTextset("   ");

        Assert.Equal("Set", a.Name);
    }

    [Fact]
    public void TryRenameTextset_RejectsBlankAndDuplicate_AcceptsValid()
    {
        var vm = MakeTemplate(out _);
        var a = vm.AddTextset("Alpha");
        var b = vm.AddTextset("Beta");

        Assert.False(vm.TryRenameTextset(a, "")); // leer → abgelehnt
        Assert.False(vm.TryRenameTextset(a, "   ")); // whitespace → abgelehnt
        Assert.False(vm.TryRenameTextset(a, "Beta")); // duplikat → abgelehnt
        Assert.Equal("Alpha", a.Name);

        Assert.True(vm.TryRenameTextset(a, "Alpha")); // identisch → ok, no-op
        Assert.True(vm.TryRenameTextset(a, "Gamma"));
        Assert.Equal("Gamma", a.Name);
    }

    [Fact]
    public void RemoveTextset_RemovesFromTemplateAndModel()
    {
        var vm = MakeTemplate(out _);
        var a = vm.AddTextset();
        var b = vm.AddTextset();

        vm.RemoveTextset(a);

        Assert.Single(vm.Textsets);
        Assert.Same(b, vm.Textsets[0]);
        Assert.Single(vm.Model.Textsets);
        Assert.Equal(b.Id, vm.Model.Textsets[0].Id);
    }

    [Fact]
    public void ApplyTextset_SetsCurrentText_OnlyForKnownNamedFields_LeavesUnknownUntouched()
    {
        var vm = MakeTemplate(out var slotId);
        var titel = vm.AddTextField(slotId, "titel");
        var unter = vm.AddTextField(slotId, "untertitel");
        var unbenannt = vm.AddTextField(slotId, ""); // namenslos
        unter.CurrentText = "Original-Untertitel";
        unbenannt.CurrentText = "bleibt";

        var set = vm.AddTextset("Glurak");
        set.SetValue("titel", "Glurak");
        // "untertitel" gibt es im Set nicht → bleibt unverändert
        // "extra" gibt es im Set, aber kein Feld → ignoriert
        set.SetValue("extra", "wird-ignoriert");

        vm.ApplyTextset(set);

        Assert.Equal("Glurak", titel.CurrentText);
        Assert.Equal("Original-Untertitel", unter.CurrentText);
        Assert.Equal("bleibt", unbenannt.CurrentText);
    }

    [Fact]
    public void TemplateConstructor_HydratesTextsetsFromModel()
    {
        var model = new Template
        {
            Name = "T",
            Textsets =
            {
                new Textset { Name = "A", Values = { ["titel"] = "x" } },
                new Textset { Name = "B" },
            },
        };

        var vm = new TemplateViewModel(model, _repo);

        Assert.Equal(2, vm.Textsets.Count);
        Assert.Equal("A", vm.Textsets[0].Name);
        Assert.Equal("x", vm.Textsets[0].GetValue("titel"));
    }

    [Fact]
    public void Roundtrip_TextsetsPersistThroughRepository()
    {
        var vm = MakeTemplate(out var slotId);
        vm.AddTextField(slotId, "titel");
        var set = vm.AddTextset("Glurak");
        set.SetValue("titel", "Glurak");

        _repo.SaveTemplate(vm.Model);
        var loaded = _repo.LoadTemplate(vm.Model.Id)!;

        Assert.Single(loaded.Textsets);
        Assert.Equal("Glurak", loaded.Textsets[0].Name);
        Assert.Equal("Glurak", loaded.Textsets[0].Values["titel"]);
    }
}
