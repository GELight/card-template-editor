using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CardTemplateEditor.ViewModels;
using CardTemplateEditor.Views.Dialogs;

namespace CardTemplateEditor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        // Live-Modifier-Tracking für die Status-Toolbar (Iteration 14): jede
        // Modifier-Änderung am Window aktualisiert MainWindowViewModel.EditMode
        // und damit die Anzeige + die Hover-Farben der Handles. Tunnel-Phase,
        // damit auch Events ankommen, die innere Controls (TextBox, Buttons)
        // im Routing konsumieren würden.
        AddHandler(KeyDownEvent, OnAnyKeyChange,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnAnyKeyChange,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnAnyKeyChange(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.EditMode = ResolveModeFromModifiers(e.KeyModifiers);
    }

    /// <summary>
    /// Reine Funktion (für Tests): mappt Avalonia-Modifier-Tasten auf den
    /// User-sichtbaren Edit-Mode. Konvention: Strg gewinnt vor Alt; Shift+Alt
    /// hat Vorrang vor Alt allein.
    /// </summary>
    public static TextFieldEditMode ResolveModeFromModifiers(KeyModifiers mods)
    {
        var ctrl = (mods & KeyModifiers.Control) != 0;
        var alt = (mods & KeyModifiers.Alt) != 0;
        var shift = (mods & KeyModifiers.Shift) != 0;

        if (ctrl) return TextFieldEditMode.Distort;
        if (alt && shift) return TextFieldEditMode.Skew;
        if (alt) return TextFieldEditMode.ScaleUniform;
        return TextFieldEditMode.Scale;
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Öffnet einen Folder-Picker, in dem der User den Ablageort für Templates
    /// und Bilder festlegt. Die Auswahl wird sofort in settings.json gespeichert,
    /// wird aber erst mit dem nächsten App-Start für Listing/Import voll aktiv —
    /// der bisherige Pfad bleibt als Fallback erhalten, alte Templates
    /// verschwinden also nicht.
    /// </summary>
    private async void OnChooseDataDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        IStorageFolder? startFolder = null;
        try { startFolder = await StorageProvider.TryGetFolderFromPathAsync(vm.CurrentDataDirectory); }
        catch { /* gepflegter Fallback: ohne Default starten */ }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Datenverzeichnis wählen (Templates + Bilder)",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.ChangeDataDirectory(path);
    }

    private async void OnAddImageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.CurrentTemplate is null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Bild auswählen",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Bilder")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" },
                },
            },
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        vm.AddImage(path);
    }

    private async Task<string?> PickExportFolderAsync(MainWindowViewModel vm, string title)
    {
        IStorageFolder? startFolder = null;
        if (!string.IsNullOrEmpty(vm.LastExportDirectory))
        {
            try { startFolder = await StorageProvider.TryGetFolderFromPathAsync(vm.LastExportDirectory); }
            catch { /* gepflegter Fallback: ohne Default starten */ }
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async void OnExportCurrentSetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var dir = await PickExportFolderAsync(vm, "Zielordner für Export wählen");
        if (string.IsNullOrEmpty(dir))
        {
            vm.ExportStatus = "Export abgebrochen — kein Zielordner gewählt.";
            return;
        }
        try
        {
            var written = vm.ExportCurrentSet(dir);
            vm.ExportStatus = written.Count == 0
                ? $"Kein Bild geschrieben — fehlen Bilder im Template? (Ziel: {dir})"
                : $"{written.Count} Datei(en) exportiert nach {dir}";
        }
        catch (Exception ex)
        {
            vm.LastExportError = ex.Message;
            vm.ExportStatus = $"Fehler: {ex.Message}";
        }
    }

    private async void OnBatchExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var dir = await PickExportFolderAsync(vm, "Zielordner für Batch-Export wählen");
        if (string.IsNullOrEmpty(dir))
        {
            vm.ExportStatus = "Batch-Export abgebrochen — kein Zielordner gewählt.";
            return;
        }
        try
        {
            var written = await vm.BatchExportAsync(dir);
            vm.ExportStatus = $"Batch: {written.Count} Datei(en) exportiert nach {dir}";
        }
        catch (Exception ex)
        {
            vm.LastExportError = ex.Message;
            vm.ExportStatus = $"Fehler: {ex.Message}";
        }
    }

    private async void OnDeleteSelectedTextFieldClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedTextField is { } field)
            await RequestDeleteTextFieldAsync(field);
    }

    private void OnResetCornerOffsetsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.SelectedTextField?.ResetCornerOffsets();
    }

    private void OnResetRotationOriginClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.SelectedTextField?.ResetRotationOrigin();
    }

    /// <summary>
    /// Wird vom TextFieldFrame aufgerufen, wenn der User DEL drückt während ein
    /// Frame Keyboard-Fokus hat. Wenn das Feld in einem Textset referenziert ist,
    /// erst Confirm-Dialog zeigen; sonst direkt löschen.
    /// </summary>
    public async Task RequestDeleteTextFieldAsync(TextFieldViewModel field)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.IsTextFieldUsedByAnyTextset(field))
        {
            var label = string.IsNullOrEmpty(field.Name) ? "(unbenannt)" : field.Name;
            var ok = await ConfirmDialog.ConfirmAsync(
                this,
                title: "Textfeld löschen?",
                message: $"Das Textfeld \"{label}\" wird in mindestens einem Textset referenziert.\n" +
                         "Beim Löschen gehen die Verbindungen zu diesem Feldnamen verloren. Trotzdem löschen?");
            if (!ok) return;
        }

        vm.RemoveTextField(field);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.FlushAutoSaveAsync();
        }
    }
}
