using CardTemplateEditor.Models;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

public class TemplateTextFieldsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateRepository _repo;

    public TemplateTextFieldsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TemplateTextFields_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _repo = new TemplateRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private TemplateViewModel MakeTemplateWithSlot(out Guid slotId)
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
    public void AddTextField_AppearsInTemplate_AndInMatchingSlot()
    {
        var vm = MakeTemplateWithSlot(out var slotId);

        var fieldVm = vm.AddTextField(slotId, "titel");

        Assert.Single(vm.TextFields);
        Assert.Same(fieldVm, vm.TextFields[0]);

        var slot = vm.ImageSlots[0];
        Assert.Single(slot.TextFields);
        Assert.Same(fieldVm, slot.TextFields[0]);

        Assert.Equal(slotId, fieldVm.ImageSlotId);
        Assert.Equal("titel", fieldVm.Name);
        Assert.Single(vm.Model.TextFields);
    }

    [Fact]
    public void RemoveTextField_RemovesFromTemplate_AndFromSlot()
    {
        var vm = MakeTemplateWithSlot(out var slotId);
        var fieldVm = vm.AddTextField(slotId);
        var slot = vm.ImageSlots[0];
        Assert.Single(slot.TextFields);

        vm.RemoveTextField(fieldVm);

        Assert.Empty(vm.TextFields);
        Assert.Empty(slot.TextFields);
        Assert.Empty(vm.Model.TextFields);
    }

    [Fact]
    public void Constructor_HydratesSlotTextFields_FromExistingModel()
    {
        var slotId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var model = new Template
        {
            Name = "T",
            ImageSlots = { new ImageSlot { Id = slotId, Name = "Front" } },
            TextFields =
            {
                new TextField { Id = fieldId, ImageSlotId = slotId, Name = "titel" },
            },
        };

        var vm = new TemplateViewModel(model, _repo);

        var slot = vm.ImageSlots[0];
        Assert.Single(slot.TextFields);
        Assert.Equal(fieldId, slot.TextFields[0].Id);
        Assert.Single(vm.TextFields);
    }

    [Fact]
    public void MainWindow_SelectingNewTemplate_ClearsSelectedTextField()
    {
        var mw = new MainWindowViewModel(_repo);
        var t = new TemplateViewModel(new Template { Name = "A" }, _repo);
        mw.CurrentTemplate = t;
        mw.SelectedTextField = new TextFieldViewModel(new TextField());

        mw.CurrentTemplate = new TemplateViewModel(new Template { Name = "B" }, _repo);

        Assert.Null(mw.SelectedTextField);
    }
}
