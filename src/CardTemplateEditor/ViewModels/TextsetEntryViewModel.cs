namespace CardTemplateEditor.ViewModels;

/// <summary>
/// Bindbares (Feldname → Wert)-Paar eines ausgewählten Textsets.
/// Schreibt direkt in das zugrundeliegende Textset-Modell.
/// </summary>
public class TextsetEntryViewModel : ViewModelBase
{
    private readonly TextsetViewModel _set;

    public TextsetEntryViewModel(string fieldName, TextsetViewModel set)
    {
        FieldName = fieldName;
        _set = set;
    }

    public string FieldName { get; }

    public string Value
    {
        get => _set.GetValue(FieldName);
        set
        {
            if (_set.GetValue(FieldName) == value) return;
            _set.SetValue(FieldName, value);
            OnPropertyChanged();
        }
    }
}
