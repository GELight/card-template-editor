using System.Collections.Generic;
using CardTemplateEditor.Models;

namespace CardTemplateEditor.ViewModels;

public class TextsetViewModel : ViewModelBase
{
    private readonly Textset _model;

    public TextsetViewModel(Textset model)
    {
        _model = model;
    }

    public Textset Model => _model;

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

    public IReadOnlyDictionary<string, string> Values => _model.Values;

    public string GetValue(string fieldName) =>
        _model.Values.TryGetValue(fieldName, out var v) ? v : "";

    public void SetValue(string fieldName, string value)
    {
        if (_model.Values.TryGetValue(fieldName, out var existing) && existing == value) return;
        _model.Values[fieldName] = value;
        OnPropertyChanged(nameof(Values));
    }

    public bool RemoveValue(string fieldName)
    {
        if (!_model.Values.Remove(fieldName)) return false;
        OnPropertyChanged(nameof(Values));
        return true;
    }
}
