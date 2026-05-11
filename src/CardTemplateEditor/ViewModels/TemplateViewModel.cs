using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;

namespace CardTemplateEditor.ViewModels;

public class TemplateViewModel : ViewModelBase
{
    private readonly Template _model;
    private readonly TemplateRepository _repository;

    public TemplateViewModel(Template model, TemplateRepository repository)
    {
        _model = model;
        _repository = repository;

        ImageSlots = new ObservableCollection<ImageSlotViewModel>(
            model.ImageSlots.Select(s => new ImageSlotViewModel(s, ResolveImagePath)));
        TextFields = new ObservableCollection<TextFieldViewModel>();
        Textsets = new ObservableCollection<TextsetViewModel>(
            model.Textsets.Select(t => new TextsetViewModel(t)));

        foreach (var fieldModel in model.TextFields)
        {
            var fieldVm = new TextFieldViewModel(fieldModel);
            TextFields.Add(fieldVm);
            AttachToSlot(fieldVm);
        }
    }

    public Template Model => _model;

    public Guid Id => _model.Id;

    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name == value) return;
            _model.Name = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ImageSlotViewModel> ImageSlots { get; }

    public ObservableCollection<TextFieldViewModel> TextFields { get; }

    public ObservableCollection<TextsetViewModel> Textsets { get; }

    public ImageSlotViewModel AddImageSlot(string sourceFile, string? slotName = null)
    {
        var slot = new ImageSlot
        {
            Name = slotName ?? Path.GetFileNameWithoutExtension(sourceFile),
        };
        slot.FileName = _repository.ImportImage(_model.Id, slot.Id, sourceFile);
        _model.ImageSlots.Add(slot);
        var vm = new ImageSlotViewModel(slot, ResolveImagePath);
        ImageSlots.Add(vm);
        return vm;
    }

    /// <summary>
    /// Legt ein neues TextField in <paramref name="imageSlotId"/> an.
    /// X/Y default auf 20/20, Width/Height auf den Modell-Default (200×30),
    /// wenn nicht explizit übergeben. Beim Anlegen aus dem Editor ruft
    /// MainWindowViewModel diese Methode mit Bild-relativen Defaults auf.
    /// </summary>
    public TextFieldViewModel AddTextField(
        Guid imageSlotId,
        string name = "",
        double x = 20,
        double y = 20,
        double? width = null,
        double? height = null)
    {
        var field = new TextField
        {
            ImageSlotId = imageSlotId,
            Name = name,
            X = x,
            Y = y,
        };
        if (width is double w) field.Width = w;
        if (height is double h) field.Height = h;
        _model.TextFields.Add(field);
        var vm = new TextFieldViewModel(field);
        TextFields.Add(vm);
        AttachToSlot(vm);
        return vm;
    }

    public void RemoveTextField(TextFieldViewModel vm)
    {
        _model.TextFields.RemoveAll(f => f.Id == vm.Id);
        TextFields.Remove(vm);
        var slot = ImageSlots.FirstOrDefault(s => s.Id == vm.ImageSlotId);
        slot?.TextFields.Remove(vm);
    }

    /// <summary>
    /// Entfernt einen ImageSlot inkl. der ihm zugeordneten TextFields und der
    /// Bilddatei auf der Platte.
    /// </summary>
    public void RemoveImageSlot(ImageSlotViewModel slot)
    {
        var fields = TextFields.Where(f => f.ImageSlotId == slot.Id).ToList();
        foreach (var f in fields) RemoveTextField(f);

        _model.ImageSlots.RemoveAll(s => s.Id == slot.Id);
        ImageSlots.Remove(slot);

        try { _repository.DeleteImageFile(_model.Id, slot.FileName); }
        catch { /* best effort */ }
    }

    public TextsetViewModel AddTextset(string baseName = "Set")
    {
        var name = MakeUniqueTextsetName(string.IsNullOrWhiteSpace(baseName) ? "Set" : baseName);
        var t = new Textset { Name = name };
        _model.Textsets.Add(t);
        var vm = new TextsetViewModel(t);
        Textsets.Add(vm);
        return vm;
    }

    public bool TryRenameTextset(TextsetViewModel vm, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        if (vm.Name == newName) return true;
        if (Textsets.Any(t => t != vm && t.Name == newName)) return false;
        vm.Name = newName;
        return true;
    }

    public void RemoveTextset(TextsetViewModel vm)
    {
        _model.Textsets.RemoveAll(t => t.Id == vm.Id);
        Textsets.Remove(vm);
    }

    public void ApplyTextset(TextsetViewModel set)
    {
        foreach (var f in TextFields)
        {
            if (string.IsNullOrEmpty(f.Name)) continue;
            if (set.Model.Values.TryGetValue(f.Name, out var v))
                f.CurrentText = v;
        }
    }

    private string MakeUniqueTextsetName(string baseName)
    {
        if (Textsets.All(t => t.Name != baseName)) return baseName;
        var i = 2;
        while (Textsets.Any(t => t.Name == $"{baseName} {i}")) i++;
        return $"{baseName} {i}";
    }

    private void AttachToSlot(TextFieldViewModel fieldVm)
    {
        var slot = ImageSlots.FirstOrDefault(s => s.Id == fieldVm.ImageSlotId);
        slot?.TextFields.Add(fieldVm);
    }

    public string? ResolveImagePath(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        return _repository.GetImagePath(_model.Id, fileName);
    }
}
