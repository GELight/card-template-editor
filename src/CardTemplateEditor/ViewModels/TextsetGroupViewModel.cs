using System.Collections.ObjectModel;

namespace CardTemplateEditor.ViewModels;

/// <summary>
/// Bündel aus Slot-Name und den Textset-Einträgen der Felder dieses Slots.
/// Wird im Set-Editor pro Bild als Gruppe gerendert, damit der User sieht
/// auf welchem Bild ein Feldname platziert ist. Felder mit gleichem Namen
/// in mehreren Slots erscheinen in jeder Slot-Gruppe — der Wert kommt aus
/// dem Set-Modell und ist deshalb in allen Gruppen identisch.
/// </summary>
public class TextsetGroupViewModel : ViewModelBase
{
    public TextsetGroupViewModel(string slotName)
    {
        SlotName = slotName;
    }

    public string SlotName { get; }

    public ObservableCollection<TextsetEntryViewModel> Entries { get; } = new();
}
