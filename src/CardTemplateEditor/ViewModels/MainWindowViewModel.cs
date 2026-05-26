using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardTemplateEditor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly TemplateRepository _repository;
    private readonly AutoSaveService _autoSave;
    private readonly SettingsService _settingsService;
    private readonly ExportService _exportService;
    private readonly BatchExportService _batchExportService;
    private readonly AppSettings _settings;
    private TemplateViewModel? _previousTemplate;

    [ObservableProperty]
    private TemplateViewModel? _currentTemplate;

    [ObservableProperty]
    private TextFieldViewModel? _selectedTextField;

    [ObservableProperty]
    private ImageSlotViewModel? _selectedImageSlot;

    [ObservableProperty]
    private TextsetViewModel? _selectedTextset;

    [ObservableProperty]
    private string? _lastExportError;

    /// <summary>
    /// Sichtbares Feedback in der Action-Bar nach einem Export — Erfolgs- oder
    /// Fehlertext. Wird von OnExportCurrentSetClick / UpdateExport / BatchExport
    /// gesetzt, damit der User Bescheid weiß ohne in den Zielordner zu schauen.
    /// </summary>
    [ObservableProperty]
    private string? _exportStatus;

    [ObservableProperty]
    private double _batchProgressFraction;

    [ObservableProperty]
    private string? _batchProgressLabel;

    [ObservableProperty]
    private bool _isBatchRunning;

    /// <summary>
    /// Aktueller Bearbeitungs-Modus, abgeleitet aus den gehaltenen Modifier-
    /// Tasten (Iteration 14). Wird vom MainWindow-Code-Behind in Reaktion
    /// auf KeyDown/KeyUp gesetzt. Dient nur noch der Toolbar-Status-Anzeige
    /// und der Hover-Farb-Synchronisation der Handles im TextFieldFrame —
    /// die eigentliche Mode-Resolution beim Drag liest die Modifier direkt
    /// aus den Pointer-Events.
    /// </summary>
    [ObservableProperty]
    private TextFieldEditMode _editMode = TextFieldEditMode.Scale;

    /// <summary>Sichtbarer Modus-Name für die Status-Toolbar.</summary>
    public string EditModeDisplayName => EditMode switch
    {
        TextFieldEditMode.Distort => "Perspektive",
        TextFieldEditMode.Skew => "Schräg",
        TextFieldEditMode.Rotate => "Drehen",
        TextFieldEditMode.ScaleUniform => "Skalieren (proportional)",
        _ => "Skalieren",
    };

    /// <summary>Mode-Farb-Indikator für die Status-Toolbar (kleines Rechteck vor dem Text).</summary>
    public Avalonia.Media.IBrush EditModeIndicatorBrush => EditMode switch
    {
        TextFieldEditMode.Distort => Avalonia.Media.Brushes.Gold,
        TextFieldEditMode.Skew => Avalonia.Media.Brushes.MediumSeaGreen,
        TextFieldEditMode.Rotate => Avalonia.Media.Brushes.Orange,
        _ => Avalonia.Media.Brushes.DodgerBlue,
    };

    partial void OnEditModeChanged(TextFieldEditMode value)
    {
        OnPropertyChanged(nameof(EditModeDisplayName));
        OnPropertyChanged(nameof(EditModeIndicatorBrush));
    }

    /// <summary>
    /// Eingaben für das aktuell ausgewählte Textset, gruppiert nach ImageSlot.
    /// Jede Gruppe steht für ein Bild und enthält genau die Feldnamen, die im
    /// jeweiligen Slot platziert wurden — so sieht der User direkt, auf welchem
    /// Bild ein Wert landet. Felder mit gleichem Namen in mehreren Slots tauchen
    /// in jeder Gruppe auf, lesen aber denselben Wert aus dem Set-Modell.
    /// </summary>
    public ObservableCollection<TextsetGroupViewModel> SelectedTextsetGroups { get; } = new();

    public MainWindowViewModel()
        : this(BuildBootstrapRepository(), settingsService: new SettingsService(TemplateRepository.DefaultDataDir))
    {
    }

    /// <summary>
    /// Liest beim App-Start die settings.json (immer im Default-Ordner) und
    /// baut daraus den TemplateRepository: aktiver Root = vom User gewählter
    /// Pfad oder Default; Fallback-Roots = frühere Daten-Verzeichnisse plus
    /// implizit der Default (damit alte Templates nach einem Pfad-Wechsel
    /// nicht verschwinden).
    /// </summary>
    private static TemplateRepository BuildBootstrapRepository()
    {
        var settings = new SettingsService(TemplateRepository.DefaultDataDir).Load();
        return BuildRepositoryFromSettings(settings);
    }

    /// <summary>
    /// Reine Fabrik-Funktion (testbar): aus AppSettings folgt eine konkrete
    /// Repository-Konfiguration. Default ist immer als Fallback dabei,
    /// solange er nicht der aktive Root ist — sonst würde ein User mit
    /// gesetztem DataDirectory seine ursprünglichen Default-Templates nicht
    /// mehr sehen.
    /// </summary>
    public static TemplateRepository BuildRepositoryFromSettings(AppSettings settings)
    {
        var active = string.IsNullOrWhiteSpace(settings.DataDirectory)
            ? TemplateRepository.DefaultDataDir
            : settings.DataDirectory!;

        var fallbacks = new List<string>();
        foreach (var prev in settings.PreviousDataDirectories ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(prev)) continue;
            if (PathsEqual(prev, active)) continue;
            if (fallbacks.Any(f => PathsEqual(f, prev))) continue;
            fallbacks.Add(prev);
        }
        if (!PathsEqual(active, TemplateRepository.DefaultDataDir)
            && !fallbacks.Any(f => PathsEqual(f, TemplateRepository.DefaultDataDir)))
        {
            fallbacks.Add(TemplateRepository.DefaultDataDir);
        }

        return new TemplateRepository(active, fallbacks);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    public MainWindowViewModel(
        TemplateRepository repository,
        TimeSpan? autoSaveDebounce = null,
        SettingsService? settingsService = null,
        ExportService? exportService = null,
        BatchExportService? batchExportService = null)
    {
        _repository = repository;
        _settingsService = settingsService ?? new SettingsService(TemplateRepository.DefaultDataDir);
        _exportService = exportService ?? new ExportService();
        _batchExportService = batchExportService ?? new BatchExportService(_exportService);
        _settings = _settingsService.Load();

        Templates = new ObservableCollection<TemplateViewModel>(
            _repository.ListTemplates().Select(t => new TemplateViewModel(t, _repository)));

        _autoSave = new AutoSaveService(
            autoSaveDebounce ?? TimeSpan.FromMilliseconds(500),
            _ =>
            {
                var current = CurrentTemplate;
                if (current is not null)
                    _repository.SaveTemplate(current.Model);
                return Task.CompletedTask;
            });
    }

    public ObservableCollection<TemplateViewModel> Templates { get; }

    public string FileNamePattern
    {
        get => _settings.FileNamePattern;
        set
        {
            if (_settings.FileNamePattern == value) return;
            _settings.FileNamePattern = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileNamePatternPreview));
        }
    }

    public string FileNamePatternPreview
    {
        get
        {
            var template = CurrentTemplate?.Name ?? "template";
            var set = SelectedTextset?.Name ?? "set";
            var image = CurrentTemplate?.ImageSlots.FirstOrDefault()?.Name ?? "image";
            var formatted = Services.FileNamePattern.Format(
                FileNamePattern, new FileNameContext(template, set, image, 1));
            return $"Beispiel: {formatted}.png";
        }
    }

    public string? LastExportDirectory
    {
        get => _settings.LastExportDirectory;
        private set
        {
            if (_settings.LastExportDirectory == value) return;
            _settings.LastExportDirectory = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanUpdateExport));
            UpdateExportCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Aktuell aktives Daten-Verzeichnis (Templates + Bilder). Liefert den
    /// vom User gewählten Pfad zurück, oder — falls keiner gesetzt ist —
    /// den Plattform-Default. Anzeige in der UI als reines Read-Only-Feld.
    /// </summary>
    public string CurrentDataDirectory =>
        string.IsNullOrWhiteSpace(_settings.DataDirectory)
            ? TemplateRepository.DefaultDataDir
            : _settings.DataDirectory!;

    /// <summary>
    /// Setzt einen neuen Ablageort für künftige Imports / Speicher-Operationen.
    /// Persistiert die Settings, schiebt den vorherigen Pfad in die History
    /// (damit alte Templates beim nächsten Start als Fallback wieder erscheinen)
    /// und meldet via <see cref="ExportStatus"/> zurück, dass die Änderung
    /// erst mit dem nächsten App-Start vollständig wirksam wird.
    ///
    /// Hot-Reload (Templates direkt umladen) wäre möglich, aber das aktuelle
    /// AutoSave-Setup würde dabei laufende Editor-Sessions verlieren — der
    /// einfache Restart-Weg ist robuster und passt zu User-Mental-Model
    /// "ich ändere eine Einstellung, App kennt sie ab dem nächsten Start".
    /// </summary>
    public void ChangeDataDirectory(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return;
        var trimmed = newPath.Trim();
        var oldActive = CurrentDataDirectory;
        var defaultDir = TemplateRepository.DefaultDataDir;

        if (PathsEqual(trimmed, oldActive))
        {
            ExportStatus = $"Datenverzeichnis unverändert: {oldActive}";
            return;
        }

        _settings.DataDirectory = PathsEqual(trimmed, defaultDir) ? null : trimmed;

        // Vorherigen aktiven Pfad in die History aufnehmen — außer es war
        // der Default (der ist immer impliziter Fallback).
        var history = _settings.PreviousDataDirectories ??= new List<string>();
        if (!PathsEqual(oldActive, defaultDir)
            && !history.Any(h => PathsEqual(h, oldActive)))
        {
            history.Insert(0, oldActive);
        }
        // Wenn der neue Pfad in der History stand, dort raus.
        history.RemoveAll(h => PathsEqual(h, trimmed));

        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(CurrentDataDirectory));
        ExportStatus =
            $"Datenverzeichnis ab nächstem Start: {trimmed}. Alte Templates bleiben am bisherigen Ort als Fallback.";
    }

    /// <summary>
    /// Re-Export ohne Folder-Picker ist möglich, sobald ein Template ausgewählt
    /// ist UND ein vorheriger Export-Pfad gespeichert wurde.
    /// </summary>
    public bool CanUpdateExport =>
        CurrentTemplate is not null && !string.IsNullOrEmpty(LastExportDirectory);

    public string PlaceholderText => "Wähle links ein Template oder lege ein neues an.";

    public AutoSaveService AutoSave => _autoSave;

    [RelayCommand]
    private void NewTemplate() => CreateNewTemplate();

    public TemplateViewModel CreateNewTemplate()
    {
        var model = new Template { Name = $"Template {Templates.Count + 1}" };
        _repository.SaveTemplate(model);
        var vm = new TemplateViewModel(model, _repository);
        Templates.Add(vm);
        CurrentTemplate = vm;
        return vm;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteCurrentTemplate))]
    private void DeleteCurrentTemplate() => RemoveTemplate(CurrentTemplate);

    public bool RemoveCurrentTemplate() => RemoveTemplateInternal(CurrentTemplate);

    [RelayCommand]
    private void RemoveTemplate(TemplateViewModel? t) => RemoveTemplateInternal(t);

    private bool RemoveTemplateInternal(TemplateViewModel? t)
    {
        if (t is null) return false;
        var index = Templates.IndexOf(t);
        if (index < 0) return false;

        var wasCurrent = ReferenceEquals(t, CurrentTemplate);
        if (wasCurrent)
        {
            SelectedTextField = null;
            SelectedTextset = null;
            SelectedImageSlot = null;
            // Pending AutoSaves dürfen nicht das gleich gelöschte Template noch einmal schreiben.
            _autoSave.Cancel();
        }

        Templates.Remove(t);
        try { _repository.DeleteTemplate(t.Id); }
        catch { /* best effort */ }

        if (wasCurrent)
        {
            CurrentTemplate = Templates.Count == 0
                ? null
                : Templates[Math.Min(index, Templates.Count - 1)];
        }
        return true;
    }

    [RelayCommand]
    private void RemoveImageSlot(ImageSlotViewModel? slot)
    {
        if (CurrentTemplate is null || slot is null) return;
        if (ReferenceEquals(slot, SelectedImageSlot)) SelectedImageSlot = null;
        if (SelectedTextField is { } tf && tf.ImageSlotId == slot.Id) SelectedTextField = null;
        CurrentTemplate.RemoveImageSlot(slot);
        _autoSave.Trigger();
    }

    /// <summary>
    /// Prüft, ob mindestens ein Textset des aktuellen Templates einen Wert für den
    /// Feldnamen dieses TextFields enthält. Wird vom DEL-Handler genutzt, um ggf.
    /// einen Confirm-Prompt vorzuschalten.
    /// </summary>
    public bool IsTextFieldUsedByAnyTextset(TextFieldViewModel field)
    {
        if (CurrentTemplate is null) return false;
        if (string.IsNullOrEmpty(field.Name)) return false;
        return CurrentTemplate.Textsets.Any(ts => ts.Model.Values.ContainsKey(field.Name));
    }

    /// <summary>
    /// Entfernt ein TextField aus dem aktuellen Template. Räumt SelectedTextField
    /// auf, wenn das gelöschte Feld gerade selektiert war, und triggert AutoSave.
    /// Confirm-Logik liegt bewusst NICHT hier, sondern im View — diese Methode
    /// löscht ohne Rückfrage.
    /// </summary>
    public void RemoveTextField(TextFieldViewModel field)
    {
        if (CurrentTemplate is null) return;
        if (ReferenceEquals(field, SelectedTextField)) SelectedTextField = null;
        CurrentTemplate.RemoveTextField(field);
        _autoSave.Trigger();
    }

    [RelayCommand]
    private void RemoveTextset(TextsetViewModel? set)
    {
        if (CurrentTemplate is null || set is null) return;
        if (ReferenceEquals(set, SelectedTextset)) SelectedTextset = null;
        CurrentTemplate.RemoveTextset(set);
        _autoSave.Trigger();
    }

    [RelayCommand]
    private void ApplyTextset(TextsetViewModel? set)
    {
        if (CurrentTemplate is null || set is null) return;
        CurrentTemplate.ApplyTextset(set);
    }

    private bool CanDeleteCurrentTemplate() => CurrentTemplate is not null;

    public bool TryRenameCurrentTemplate(string newName)
    {
        if (CurrentTemplate is null) return false;
        var trimmed = newName?.Trim() ?? "";
        if (string.IsNullOrEmpty(trimmed)) return false;
        if (CurrentTemplate.Name == trimmed) return true;
        CurrentTemplate.Name = trimmed;
        _autoSave.Trigger();
        return true;
    }

    /// <summary>
    /// Importiert ein Bild ans aktuelle Template als neuer ImageSlot.
    /// Pfad-Auswahl macht die View (FilePicker), das VM nimmt nur den absoluten Pfad.
    /// </summary>
    public ImageSlotViewModel? AddImage(string? sourcePath)
    {
        if (CurrentTemplate is null || string.IsNullOrEmpty(sourcePath)) return null;
        var slot = CurrentTemplate.AddImageSlot(sourcePath);
        SelectedImageSlot = slot;
        _autoSave.Trigger();
        return slot;
    }

    [RelayCommand(CanExecute = nameof(CanAddTextField))]
    private void AddTextField() => AddTextFieldToCurrentSlot();

    public TextFieldViewModel? AddTextFieldToCurrentSlot()
    {
        if (CurrentTemplate is null) return null;
        var slot = SelectedImageSlot ?? CurrentTemplate.ImageSlots.FirstOrDefault();
        if (slot is null) return null;
        var (w, h, x, y) = ComputeDefaultGeometry(slot);
        var vm = CurrentTemplate.AddTextField(
            slot.Id,
            name: $"Textfeld{CurrentTemplate.TextFields.Count}",
            x: x,
            y: y,
            width: w,
            height: h);
        SelectedTextField = vm;
        _autoSave.Trigger();
        return vm;
    }

    /// <summary>
    /// Default-Geometrie für ein neues Textfeld, abgeleitet aus der Pixelgröße
    /// des Bild-Slots: 40 % der Breite, 8 % der Höhe (Mindesthöhe 20 px),
    /// horizontal zentriert, 5 % von oben. Wenn der Slot noch keine Bitmap
    /// geladen hat (PixelWidth/Height = 0), Fallback auf den alten Default
    /// 200×30 bei (20, 20).
    /// </summary>
    public static (double Width, double Height, double X, double Y) ComputeDefaultGeometry(
        ImageSlotViewModel slot)
    {
        var pw = slot.PixelWidth;
        var ph = slot.PixelHeight;
        if (pw <= 0 || ph <= 0)
            return (200, 30, 20, 20);
        var w = pw * 0.40;
        var h = Math.Max(20, ph * 0.08);
        var x = (pw - w) / 2.0;
        var y = ph * 0.05;
        return (w, h, x, y);
    }

    private bool CanAddTextField() =>
        CurrentTemplate is not null && CurrentTemplate.ImageSlots.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAddTextset))]
    private void AddTextset() => AddNewTextset();

    public TextsetViewModel? AddNewTextset()
    {
        if (CurrentTemplate is null) return null;
        var vm = CurrentTemplate.AddTextset();
        SelectedTextset = vm;
        _autoSave.Trigger();
        return vm;
    }

    private bool CanAddTextset() => CurrentTemplate is not null;

    [RelayCommand(CanExecute = nameof(CanModifySelectedTextset))]
    private void RemoveSelectedTextset()
    {
        if (CurrentTemplate is null || SelectedTextset is null) return;
        var s = SelectedTextset;
        SelectedTextset = null;
        CurrentTemplate.RemoveTextset(s);
        _autoSave.Trigger();
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedTextset))]
    private void ApplySelectedTextset()
    {
        if (CurrentTemplate is null || SelectedTextset is null) return;
        CurrentTemplate.ApplyTextset(SelectedTextset);
        // Property-Änderungen an TextFields lösen AutoSave bereits via Subscriptions aus.
    }

    private bool CanModifySelectedTextset() =>
        CurrentTemplate is not null && SelectedTextset is not null;

    /// <summary>
    /// Re-export ins zuletzt verwendete Zielverzeichnis ohne Folder-Picker.
    /// Bestehende PNGs werden überschrieben (ExportSlot schreibt mit overwrite-File-Move).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateExport))]
    private void UpdateExport()
    {
        if (string.IsNullOrEmpty(LastExportDirectory)) return;
        try
        {
            var written = ExportCurrentSet(LastExportDirectory);
            ExportStatus = $"{written.Count} Datei(en) aktualisiert in {LastExportDirectory}";
        }
        catch (Exception ex)
        {
            LastExportError = ex.Message;
            ExportStatus = $"Fehler: {ex.Message}";
        }
    }

    /// <summary>
    /// Exportiert alle ImageSlots des aktuellen Templates mit den AKTUELLEN
    /// TextField-Werten in das Zielverzeichnis. Wenn ein Textset ausgewählt ist,
    /// werden seine Werte vorher angewendet.
    /// </summary>
    public IReadOnlyList<string> ExportCurrentSet(string targetDir)
    {
        if (CurrentTemplate is null || string.IsNullOrWhiteSpace(targetDir))
            return Array.Empty<string>();

        if (SelectedTextset is not null)
            CurrentTemplate.ApplyTextset(SelectedTextset);

        var setName = SelectedTextset?.Name ?? "current";
        var written = new List<string>();
        var index = 1;
        foreach (var slot in CurrentTemplate.ImageSlots)
        {
            if (string.IsNullOrEmpty(slot.FileName)) continue;
            var sourcePath = _repository.GetImagePath(CurrentTemplate.Id, slot.FileName);
            if (!File.Exists(sourcePath)) continue;

            var fileName = Services.FileNamePattern.Format(FileNamePattern, new FileNameContext(
                Template: CurrentTemplate.Name,
                Set: setName,
                Image: slot.Name,
                Index: index));
            var destPath = Path.Combine(targetDir, fileName + ".png");

            _exportService.ExportSlot(CurrentTemplate.Model, slot.Model, sourcePath, destPath);
            written.Add(destPath);
            index++;
        }

        LastExportDirectory = targetDir;
        return written;
    }

    /// <summary>
    /// Iteriert alle Textsets × ImageSlots des aktuellen Templates und schreibt
    /// pro Kombination ein PNG. Mutiert das aktuelle Modell NICHT (BatchExportService
    /// arbeitet auf Snapshots).
    /// </summary>
    public async Task<IReadOnlyList<string>> BatchExportAsync(
        string targetDir,
        CancellationToken ct = default)
    {
        if (CurrentTemplate is null || string.IsNullOrWhiteSpace(targetDir))
            return Array.Empty<string>();

        // Vor dem Batch alle Pending-Saves rausschreiben — der Snapshot soll
        // mit dem zuletzt persistierten Stand übereinstimmen.
        await _autoSave.FlushAsync(ct);

        IsBatchRunning = true;
        BatchProgressFraction = 0;
        BatchProgressLabel = null;

        try
        {
            var req = new BatchExportRequest(
                Template: CurrentTemplate.Model,
                Textsets: CurrentTemplate.Textsets.Select(t => t.Model).ToList(),
                ResolveSourcePath: name => _repository.GetImagePath(CurrentTemplate.Id, name),
                TargetDir: targetDir,
                FileNamePattern: FileNamePattern);

            var progress = new Progress<BatchExportProgress>(p =>
            {
                BatchProgressFraction = p.Total == 0 ? 0 : (double)p.Done / p.Total;
                BatchProgressLabel = $"{p.Done} / {p.Total}";
            });

            var written = await _batchExportService.RunAsync(req, progress, ct);
            LastExportDirectory = targetDir;
            return written;
        }
        finally
        {
            IsBatchRunning = false;
        }
    }

    partial void OnCurrentTemplateChanged(TemplateViewModel? value)
    {
        if (_previousTemplate is not null)
            DetachSubscriptions(_previousTemplate);
        _previousTemplate = value;
        SelectedTextField = null;
        SelectedTextset = null;
        SelectedImageSlot = value?.ImageSlots.FirstOrDefault();
        if (value is not null)
            AttachSubscriptions(value);
        AddTextFieldCommand.NotifyCanExecuteChanged();
        AddTextsetCommand.NotifyCanExecuteChanged();
        RemoveSelectedTextsetCommand.NotifyCanExecuteChanged();
        ApplySelectedTextsetCommand.NotifyCanExecuteChanged();
        DeleteCurrentTemplateCommand.NotifyCanExecuteChanged();
        UpdateExportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUpdateExport));
        OnPropertyChanged(nameof(FileNamePatternPreview));
    }

    partial void OnSelectedImageSlotChanged(ImageSlotViewModel? oldValue, ImageSlotViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        AddTextFieldCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Spiegelt die Selektion auf die einzelnen <see cref="TextFieldViewModel.IsSelected"/>-
    /// Flags. Nur das selektierte Feld zeigt seinen Bearbeitungsrahmen + Handles —
    /// alle anderen Frames bleiben transparent und stören das Layout nicht.
    /// </summary>
    partial void OnSelectedTextFieldChanged(TextFieldViewModel? oldValue, TextFieldViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    partial void OnSelectedTextsetChanged(TextsetViewModel? value)
    {
        // Beim Anklicken eines Sets in der Sidebar fließen seine Werte direkt
        // in die Felder. Beim Abwählen (value == null) bleiben die letzten
        // Texte erhalten — sonst würden manuelle Bearbeitungen verloren gehen.
        if (value is not null && CurrentTemplate is not null)
            CurrentTemplate.ApplyTextset(value);

        RebuildSelectedTextsetGroups();
        RemoveSelectedTextsetCommand.NotifyCanExecuteChanged();
        ApplySelectedTextsetCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FileNamePatternPreview));
    }

    private void RebuildSelectedTextsetGroups()
    {
        SelectedTextsetGroups.Clear();
        if (CurrentTemplate is null || SelectedTextset is null) return;

        // Pro Slot in Reihenfolge der Bilder einen Gruppe-Header bauen. Innerhalb
        // einer Gruppe pro Feldname ein Eintrag (Duplikate gleichen Namens im
        // selben Slot zusammenfassen). Der Eintrag zeigt den Wert direkt aus
        // dem Set-Modell — landet derselbe Name in zwei Slots, kommt der Wert
        // aus einer einzigen Quelle.
        foreach (var slot in CurrentTemplate.ImageSlots)
        {
            var group = new TextsetGroupViewModel(slot.Name);
            var seen = new HashSet<string>();
            foreach (var f in CurrentTemplate.TextFields)
            {
                if (f.ImageSlotId != slot.Id) continue;
                if (string.IsNullOrEmpty(f.Name)) continue;
                if (!seen.Add(f.Name)) continue;
                group.Entries.Add(new TextsetEntryViewModel(f.Name, SelectedTextset));
            }
            // Slots ohne benannte Felder weglassen, sonst flutet das Panel
            // mit leeren Headern.
            if (group.Entries.Count > 0)
                SelectedTextsetGroups.Add(group);
        }
    }

    private void AttachSubscriptions(TemplateViewModel t)
    {
        t.PropertyChanged += OnDirty;
        t.ImageSlots.CollectionChanged += OnSlotsChanged;
        t.TextFields.CollectionChanged += OnTextFieldsChanged;
        t.Textsets.CollectionChanged += OnTextsetsChanged;
        foreach (var s in t.ImageSlots) s.PropertyChanged += OnDirty;
        foreach (var f in t.TextFields) f.PropertyChanged += OnDirty;
        foreach (var ts in t.Textsets) ts.PropertyChanged += OnDirty;
    }

    private void DetachSubscriptions(TemplateViewModel t)
    {
        t.PropertyChanged -= OnDirty;
        t.ImageSlots.CollectionChanged -= OnSlotsChanged;
        t.TextFields.CollectionChanged -= OnTextFieldsChanged;
        t.Textsets.CollectionChanged -= OnTextsetsChanged;
        foreach (var s in t.ImageSlots) s.PropertyChanged -= OnDirty;
        foreach (var f in t.TextFields) f.PropertyChanged -= OnDirty;
        foreach (var ts in t.Textsets) ts.PropertyChanged -= OnDirty;
    }

    private void OnTextsetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (TextsetViewModel ts in e.NewItems) ts.PropertyChanged += OnDirty;
        if (e.OldItems is not null)
            foreach (TextsetViewModel ts in e.OldItems) ts.PropertyChanged -= OnDirty;
        _autoSave.Trigger();
    }

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (ImageSlotViewModel s in e.NewItems) s.PropertyChanged += OnDirty;
        if (e.OldItems is not null)
            foreach (ImageSlotViewModel s in e.OldItems) s.PropertyChanged -= OnDirty;
        if (SelectedImageSlot is null && CurrentTemplate?.ImageSlots.Count > 0)
            SelectedImageSlot = CurrentTemplate.ImageSlots[0];
        _autoSave.Trigger();
        AddTextFieldCommand.NotifyCanExecuteChanged();
    }

    private void OnTextFieldsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (TextFieldViewModel f in e.NewItems) f.PropertyChanged += OnDirty;
        if (e.OldItems is not null)
            foreach (TextFieldViewModel f in e.OldItems) f.PropertyChanged -= OnDirty;
        _autoSave.Trigger();
        if (SelectedTextset is not null) RebuildSelectedTextsetGroups();
    }

    private void OnDirty(object? sender, PropertyChangedEventArgs e)
    {
        _autoSave.Trigger();
        if (sender is TextFieldViewModel && e.PropertyName == nameof(TextFieldViewModel.Name)
            && SelectedTextset is not null)
        {
            RebuildSelectedTextsetGroups();
        }
    }

    public Task FlushAutoSaveAsync() => _autoSave.FlushAsync();
}
