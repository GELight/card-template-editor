using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.VisualTree;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Views.Controls;

public partial class TextFieldFrame : UserControl
{
    public const double MinSize = 16;

    /// <summary>
    /// Drag-Modus eines Handle-Drags. Wird in OnHandlePressed aus den
    /// gehaltenen Modifier-Tasten abgeleitet (Iteration 14): kein Modifier
    /// = Scale, Strg = Distort an Ecke, Alt = ScaleUniform (Aspekt-Lock),
    /// Shift+Alt = Skew an Kante. Der Owner-EditMode dient nur noch der
    /// Status-Anzeige in der Toolbar, nicht der Mode-Resolution.
    /// Internal für Pure-Tests von <see cref="ResolveHandleMode"/>.
    /// </summary>
    public enum HandleMode { Scale, ScaleUniform, Distort, Skew, Rotate }

    private bool _isDragging;
    private bool _isRotating;
    private bool _isOriginDragging;
    private HandleMode? _handleMode;
    private string? _handleDirection;
    private Point _gestureStart;
    private double _vmStartX;
    private double _vmStartY;
    private double _vmStartW;
    private double _vmStartH;
    private double _vmStartRotation;
    private double _vmStartCornerDx;
    private double _vmStartCornerDy;
    private double _vmStartOriginRelX;
    private double _vmStartOriginRelY;
    // Skew-Modus: zwei benachbarte Eckpunkt-Offsets müssen synchron geupdatet
    // werden — wir merken uns ihre Startwerte als Tupel A/B (Reihenfolge je
    // nach Kante festgelegt in BeginSkew).
    private (double Dx, double Dy) _skewStartA;
    private (double Dx, double Dy) _skewStartB;
    private double _rotationGestureStartAngleDeg;
    private Visual? _coordinateSpace;

    public TextFieldFrame()
    {
        InitializeComponent();

        DragBorder.PointerPressed += OnDragBorderPressed;
        DragBorder.PointerMoved += OnPointerMoved;
        DragBorder.PointerReleased += OnPointerReleased;
        DragBorder.KeyDown += OnDragBorderKeyDown;
        // Doppelklick auf den Drag-Streifen ODER auf die unsichtbare TextBox
        // schaltet in den Inline-Edit-Modus: TextBox wird sichtbar, Cursor +
        // Selektion erscheinen, der User editiert direkt im Layout.
        DragBorder.DoubleTapped += OnEnterEditMode;
        DragBorder.AddHandler(KeyDownEvent, OnEditModeKeys, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Auch ein Klick direkt in die TextBox soll das Properties-Panel
        // umschalten — sonst bleibt das rechte Panel leer, sobald man nur
        // tippen will und nicht den Drag-Streifen trifft.
        TextEditor.GotFocus += OnTextEditorGotFocus;
        TextEditor.PointerPressed += (_, _) => SelectInOwner();
        TextEditor.DoubleTapped += OnEnterEditMode;
        TextEditor.LostFocus += OnExitEditMode;
        // Strg+B togglet Bold für die aktuelle Selektion in der TextBox.
        // Tunnel-Phase, damit die TextBox das Event nicht vorab konsumiert.
        TextEditor.AddHandler(KeyDownEvent, OnTextEditorKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

        foreach (var handle in new[]
                 {
                     HandleNW, HandleN, HandleNE, HandleE,
                     HandleSE, HandleS, HandleSW, HandleW,
                 })
        {
            handle.PointerPressed += OnHandlePressed;
            handle.PointerMoved += OnPointerMoved;
            handle.PointerReleased += OnPointerReleased;
        }

        HandleRotate.PointerPressed += OnRotateHandlePressed;
        HandleRotate.PointerMoved += OnPointerMoved;
        HandleRotate.PointerReleased += OnPointerReleased;

        HandleOrigin.PointerPressed += OnOriginPressed;
        HandleOrigin.PointerMoved += OnPointerMoved;
        HandleOrigin.PointerReleased += OnPointerReleased;

        // Owner-EditMode beobachten: Klassen .scale/.distort/.skew/.rotate auf
        // den 8 Handles synchron halten, damit das Hover-Feedback (Style-
        // Selectoren in App.axaml) die Mode-Farbe zeigt.
        AttachedToVisualTree += (_, _) => HookOwnerModeChanges();
        DetachedFromVisualTree += (_, _) => UnhookOwnerModeChanges();
    }

    private MainWindowViewModel? _hookedOwner;

    private void HookOwnerModeChanges()
    {
        var window = this.FindAncestorOfType<Window>();
        if (window?.DataContext is not MainWindowViewModel owner) return;
        if (ReferenceEquals(_hookedOwner, owner)) return;
        UnhookOwnerModeChanges();
        _hookedOwner = owner;
        owner.PropertyChanged += OnOwnerPropertyChanged;
        ApplyModeClasses(owner.EditMode);
    }

    private void UnhookOwnerModeChanges()
    {
        if (_hookedOwner is null) return;
        _hookedOwner.PropertyChanged -= OnOwnerPropertyChanged;
        _hookedOwner = null;
    }

    private void OnOwnerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.EditMode)) return;
        if (sender is MainWindowViewModel owner) ApplyModeClasses(owner.EditMode);
    }

    /// <summary>
    /// Spiegelt den globalen EditMode auf die Klassen-Liste der 8 Handles.
    /// Style-Selektoren in UserControl.Styles greifen auf
    /// `.handle.scale`, `.handle.distort`, `.handle.skew`, `.handle.rotate`
    /// und liefern die zum Mode passende Fill-Farbe (Hover-Vorschau auch
    /// während der User den Modifier hält, ohne schon zu klicken).
    /// Setzt zusätzlich <see cref="TextFieldViewModel.IsSkewActive"/>, damit
    /// die 4 Eck-Handles im Skew-Modus ausgeblendet werden.
    /// </summary>
    private void ApplyModeClasses(TextFieldEditMode mode)
    {
        var handles = new[]
        {
            HandleNW, HandleN, HandleNE, HandleE,
            HandleSE, HandleS, HandleSW, HandleW,
        };
        foreach (var h in handles)
        {
            h.Classes.Set("scale", mode == TextFieldEditMode.Scale);
            h.Classes.Set("distort", mode == TextFieldEditMode.Distort);
            h.Classes.Set("skew", mode == TextFieldEditMode.Skew);
            h.Classes.Set("rotate", mode == TextFieldEditMode.Rotate);
        }
        if (DataContext is TextFieldViewModel vm)
            vm.IsSkewActive = mode == TextFieldEditMode.Skew;
    }

    private void OnDragBorderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TextFieldViewModel vm) return;
        if (e.Source is Rectangle) return;
        if (e.Source is Ellipse) return;
        if (e.Source is Avalonia.Controls.Shapes.Line) return;
        if (e.Source is Avalonia.Controls.Shapes.Path) return;     // Origin-Cross-Marker
        if (!BeginGesture(vm, e)) return;
        SelectInOwner();
        // Keyboard-Fokus auf den Drag-Border, damit Tastatur-Aktionen wie DEL
        // gezielt diesem Frame zugeordnet werden — und nicht z. B. dem letzten
        // TextField-Eingabefeld in der Sidebar.
        DragBorder.Focus();
        _isDragging = true;
        e.Pointer.Capture(DragBorder);
        e.Handled = true;
    }

    /// <summary>
    /// Einheitlicher Handler für alle 8 Handles. Modus wird aus den beim
    /// Press gehaltenen Modifier-Tasten abgeleitet — und in
    /// <see cref="OnPointerMoved"/> live aktualisiert, sobald der User
    /// Modifier-Tasten während des Drags drückt oder loslässt
    /// (Photoshop-Konvention). Kein Modifier=Scale, Strg=Distort,
    /// Alt=ScaleUniform, Shift+Alt=Skew.
    /// </summary>
    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TextFieldViewModel vm) return;
        if (sender is not Rectangle handle) return;
        if (handle.Tag is not string direction) return;
        if (!BeginGesture(vm, e)) return;
        SelectInOwner();
        DragBorder.Focus();

        _handleMode = ResolveHandleMode(e.KeyModifiers, direction);
        _handleDirection = direction;
        InitForCurrentMode(vm);

        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    /// <summary>
    /// Reine Funktion: leitet aus Modifier-Tasten + Handle-Richtung den
    /// aktiven Drag-Modus ab. Mode/Handle-Mismatch (z. B. Strg an Edge,
    /// Shift+Alt an Ecke) fällt auf Scale zurück, damit kein Handle „tot"
    /// wirkt. Internal für Tests.
    /// </summary>
    internal static HandleMode ResolveHandleMode(KeyModifiers mods, string direction)
    {
        var ctrl = (mods & KeyModifiers.Control) != 0;
        var alt = (mods & KeyModifiers.Alt) != 0;
        var shift = (mods & KeyModifiers.Shift) != 0;
        var isCorner = direction is "NW" or "NE" or "SE" or "SW";
        var isEdge = direction is "N" or "E" or "S" or "W";

        return (ctrl, alt, shift, isCorner, isEdge) switch
        {
            (true, _, _, true, _) => HandleMode.Distort,            // Strg + Eck
            (false, true, true, _, true) => HandleMode.Skew,        // Shift+Alt + Edge
            (false, true, false, _, _) => HandleMode.ScaleUniform,  // Alt allein
            _ => HandleMode.Scale,
        };
    }

    /// <summary>
    /// Mode-spezifische Initialisierung: für Distort den Eck-Offset-Startwert
    /// merken, für Skew die zwei betroffenen Eckpunkte einlesen. Wird sowohl
    /// von <see cref="OnHandlePressed"/> als auch von
    /// <see cref="ReanchorGesture"/> beim dynamischen Mode-Wechsel aufgerufen.
    /// </summary>
    private void InitForCurrentMode(TextFieldViewModel vm)
    {
        if (_handleDirection is null) return;
        switch (_handleMode)
        {
            case HandleMode.Distort:
                (_vmStartCornerDx, _vmStartCornerDy) = ReadCornerOffset(vm, _handleDirection);
                break;
            case HandleMode.Skew:
                BeginSkew(vm, _handleDirection);
                break;
        }
    }

    /// <summary>
    /// Setzt Anker (Cursor + VM-Startwerte) auf den aktuellen Stand zurück,
    /// damit der neue Modus von hier aus weiterarbeitet — ohne dass die
    /// bisherigen Drag-Änderungen verworfen werden. Wird beim Live-Mode-
    /// Wechsel in <see cref="OnPointerMoved"/> aufgerufen.
    /// </summary>
    private void ReanchorGesture(TextFieldViewModel vm, Point current)
    {
        _gestureStart = current;
        _vmStartX = vm.X;
        _vmStartY = vm.Y;
        _vmStartW = vm.Width;
        _vmStartH = vm.Height;
        _vmStartRotation = vm.Rotation;
        InitForCurrentMode(vm);
    }

    /// <summary>
    /// Liest die zwei Eckpunkt-Offsets, die bei einem Skew an dieser Kante
    /// synchron verschoben werden müssen, und merkt sich ihre Startwerte.
    /// Reihenfolge A/B = "im Uhrzeigersinn": A=NW, B=NE für die N-Kante usw.
    /// </summary>
    private void BeginSkew(TextFieldViewModel vm, string edge)
    {
        switch (edge)
        {
            case "N":
                _skewStartA = (vm.CornerNWdx, vm.CornerNWdy);
                _skewStartB = (vm.CornerNEdx, vm.CornerNEdy);
                break;
            case "E":
                _skewStartA = (vm.CornerNEdx, vm.CornerNEdy);
                _skewStartB = (vm.CornerSEdx, vm.CornerSEdy);
                break;
            case "S":
                _skewStartA = (vm.CornerSEdx, vm.CornerSEdy);
                _skewStartB = (vm.CornerSWdx, vm.CornerSWdy);
                break;
            case "W":
                _skewStartA = (vm.CornerSWdx, vm.CornerSWdy);
                _skewStartB = (vm.CornerNWdx, vm.CornerNWdy);
                break;
        }
    }

    private static (double Dx, double Dy) ReadCornerOffset(TextFieldViewModel vm, string corner)
        => corner switch
        {
            "NW" => (vm.CornerNWdx, vm.CornerNWdy),
            "NE" => (vm.CornerNEdx, vm.CornerNEdy),
            "SE" => (vm.CornerSEdx, vm.CornerSEdy),
            "SW" => (vm.CornerSWdx, vm.CornerSWdy),
            _ => (0, 0),
        };

    private static void WriteCornerOffset(TextFieldViewModel vm, string corner, double dx, double dy)
    {
        switch (corner)
        {
            case "NW": vm.CornerNWdx = dx; vm.CornerNWdy = dy; break;
            case "NE": vm.CornerNEdx = dx; vm.CornerNEdy = dy; break;
            case "SE": vm.CornerSEdx = dx; vm.CornerSEdy = dy; break;
            case "SW": vm.CornerSWdx = dx; vm.CornerSWdy = dy; break;
        }
    }

    private void OnOriginPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TextFieldViewModel vm) return;
        if (!BeginGesture(vm, e)) return;
        SelectInOwner();
        DragBorder.Focus();
        _isOriginDragging = true;
        _vmStartOriginRelX = vm.RotationOriginRelX;
        _vmStartOriginRelY = vm.RotationOriginRelY;
        e.Pointer.Capture(HandleOrigin);
        e.Handled = true;
    }

    private void OnRotateHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TextFieldViewModel vm) return;
        if (!BeginGesture(vm, e)) return;
        SelectInOwner();
        DragBorder.Focus();
        _isRotating = true;
        // Startwinkel vom Frame-Mittelpunkt zum aktuellen Pointer (in Canvas-Coords).
        _rotationGestureStartAngleDeg = AngleFromOriginDeg(_gestureStart, vm);
        e.Pointer.Capture(HandleRotate);
        e.Handled = true;
    }

    private async void OnDragBorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        if (DataContext is not TextFieldViewModel vm) return;
        // Im Edit-Mode darf DEL Zeichen löschen, NICHT das Feld. Erst
        // Edit-Mode verlassen oder Modifier nutzen.
        if (vm.IsInEditMode) return;
        var window = this.FindAncestorOfType<Window>();
        if (window is Views.MainWindow main)
        {
            e.Handled = true;
            await main.RequestDeleteTextFieldAsync(vm);
        }
    }

    /// <summary>
    /// TextBox erhält Fokus per Tab-Navigation (Avalonia liefert
    /// NavigationMethod.Tab in den GotFocusEventArgs). In dem Fall wollen
    /// wir gleich in den Edit-Mode springen, damit der User direkt tippen
    /// kann — ohne erst noch doppelklicken zu müssen.
    /// Klick-Fokus (NavigationMethod.Pointer) bleibt bewusst „nur Selektion":
    /// der heutige Doppelklick-Workflow zum Editieren bleibt unverändert.
    /// </summary>
    private void OnTextEditorGotFocus(object? sender, Avalonia.Input.FocusChangedEventArgs e)
    {
        SelectInOwner();
        // NavigationMethod liefert, wie der Fokus gewechselt wurde — Tab/
        // Pointer/Directional/Unspecified. Klick-Fokus soll den heutigen
        // "Selektion-ohne-Edit"-Workflow behalten; nur Tab springt direkt
        // in den Inline-Edit-Modus.
        if (e.NavigationMethod != NavigationMethod.Tab) return;
        if (DataContext is not TextFieldViewModel vm) return;
        vm.IsInEditMode = true;
        // Cursor ans Ende, damit User direkt weitertippen kann ohne erst
        // klicken zu müssen.
        if (string.IsNullOrEmpty(TextEditor.SelectedText))
            TextEditor.CaretIndex = TextEditor.Text?.Length ?? 0;
    }

    /// <summary>
    /// Doppelklick auf Drag-Border oder TextBox-Bereich → Inline-Edit-Mode.
    /// TextBox wird sichtbar (Opacity-Binding via ViewModel), Caret-Fokus
    /// wandert auf den TextEditor, User kann tippen / selektieren.
    /// </summary>
    private void OnEnterEditMode(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is not TextFieldViewModel vm) return;
        vm.IsInEditMode = true;
        SelectInOwner();
        TextEditor.Focus();
        // Cursor an Klick-Position setzen, falls die TextBox-DoubleTap
        // Quelle ist; sonst ans Ende.
        if (string.IsNullOrEmpty(TextEditor.SelectedText))
            TextEditor.CaretIndex = TextEditor.Text?.Length ?? 0;
        e.Handled = true;
    }

    /// <summary>
    /// Esc beendet den Edit-Mode (Tunnel-Phase, damit der TextBox-Eigene
    /// Esc-Handler uns nicht überschattet). Enter würde die TextBox eine
    /// neue Zeile einfügen, das wollen wir hier zulassen.
    /// </summary>
    private void OnEditModeKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (DataContext is not TextFieldViewModel vm) return;
        if (!vm.IsInEditMode) return;
        vm.IsInEditMode = false;
        DragBorder.Focus();
        e.Handled = true;
    }

    /// <summary>
    /// Beim Fokusverlust der TextBox Edit-Mode beenden — User klickt
    /// woanders hin, Preview-Bitmap übernimmt wieder.
    /// </summary>
    private void OnExitEditMode(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not TextFieldViewModel vm) return;
        vm.IsInEditMode = false;
    }

    /// <summary>
    /// Strg+B in der TextBox: aktuelle Selektion fett toggeln. Wir lesen
    /// SelectionStart/SelectionEnd (Avalonia normalisiert nicht — Start kann
    /// größer als End sein, wenn rückwärts selektiert wurde).
    /// </summary>
    private void OnTextEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.B) return;
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        if (DataContext is not TextFieldViewModel vm) return;

        var s = TextEditor.SelectionStart;
        var en = TextEditor.SelectionEnd;
        var start = Math.Min(s, en);
        var length = Math.Abs(en - s);
        if (length <= 0) return;
        vm.ToggleBoldOnSelection(start, length);
        e.Handled = true;
    }

    private bool BeginGesture(TextFieldViewModel vm, PointerEventArgs e)
    {
        // Coords relativ zum nächsten Canvas-Vorfahren — das ist genau der
        // ItemsPanel-Canvas im EditableImageCanvas, dessen Logik-Raum mit den
        // Bildpixel-Koordinaten der TextFields übereinstimmt. So bleibt der
        // Drag korrekt, auch wenn das Bild per LayoutTransform skaliert ist.
        _coordinateSpace = this.FindAncestorOfType<Canvas>() ?? (Visual?)TopLevel.GetTopLevel(this);
        if (_coordinateSpace is null) return false;
        _gestureStart = e.GetPosition(_coordinateSpace);
        _vmStartX = vm.X;
        _vmStartY = vm.Y;
        _vmStartW = vm.Width;
        _vmStartH = vm.Height;
        _vmStartRotation = vm.Rotation;
        return true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not TextFieldViewModel vm) return;
        if (_coordinateSpace is null) return;
        if (!_isDragging && !_isRotating && !_isOriginDragging && _handleMode is null) return;

        var current = e.GetPosition(_coordinateSpace);

        // Photoshop-Konvention: Modifier-Tasten WÄHREND des Drags drücken/
        // loslassen wechselt den Modus live. Bei Modus-Wechsel setzen wir den
        // Anker auf den aktuellen Stand zurück, damit der neue Modus von hier
        // aus weiterläuft (bisherige Änderungen bleiben erhalten).
        if (_handleMode is not null && _handleDirection is not null)
        {
            var newMode = ResolveHandleMode(e.KeyModifiers, _handleDirection);
            if (newMode != _handleMode)
            {
                _handleMode = newMode;
                ReanchorGesture(vm, current);
                return; // dieser Tick re-ankert nur — der nächste Move arbeitet mit neuem Modus
            }
        }

        var dx = current.X - _gestureStart.X;
        var dy = current.Y - _gestureStart.Y;

        if (_isDragging)
        {
            // Drag bewegt das Frame parallel zum Cursor — Rotation hat keinen
            // Einfluss, weil X/Y die Bildpixel-Position der unrotierten Box sind.
            var (nx, ny) = ComputeDrag(_vmStartX, _vmStartY, dx, dy);
            vm.X = nx;
            vm.Y = ny;
            return;
        }
        if (_isOriginDragging)
        {
            // Origin folgt dem Cursor in Frame-lokalen Coords. Bei rotierten
            // Frames muss das Cursor-Delta zurück in den unrotierten Raum,
            // sonst wandert der Origin gegen die visuelle Achse.
            var (lox, loy) = InverseRotate(dx, dy, _vmStartRotation);
            if (_vmStartW > 0)
                vm.RotationOriginRelX = Math.Clamp(
                    _vmStartOriginRelX + lox / _vmStartW, -1.0, 2.0);
            if (_vmStartH > 0)
                vm.RotationOriginRelY = Math.Clamp(
                    _vmStartOriginRelY + loy / _vmStartH, -1.0, 2.0);
            return;
        }
        if (_isRotating)
        {
            var nowDeg = AngleFromOriginDeg(current, vm);
            var delta = nowDeg - _rotationGestureStartAngleDeg;
            vm.Rotation = _vmStartRotation + delta;
            return;
        }

        // Handle-Drag: Modus wurde beim Press fixiert. Cursor-Delta zurück
        // in den lokalen (unrotierten) Frame-Raum drehen — sonst würde ein
        // Drag-Verhalten an einer rotierten Box gegen die visuelle Achse
        // arbeiten.
        var (lx, ly) = InverseRotate(dx, dy, _vmStartRotation);
        switch (_handleMode)
        {
            case HandleMode.Scale when _handleDirection is not null:
                var r = ComputeResize(_handleDirection, _vmStartX, _vmStartY,
                    _vmStartW, _vmStartH, lx, ly);
                vm.X = r.X; vm.Y = r.Y;
                vm.Width = r.Width; vm.Height = r.Height;
                break;
            case HandleMode.ScaleUniform when _handleDirection is not null:
                var ru = ComputeResize(_handleDirection, _vmStartX, _vmStartY,
                    _vmStartW, _vmStartH, lx, ly, keepAspect: true);
                vm.X = ru.X; vm.Y = ru.Y;
                vm.Width = ru.Width; vm.Height = ru.Height;
                break;
            case HandleMode.Distort when _handleDirection is not null:
                WriteCornerOffset(vm, _handleDirection,
                    _vmStartCornerDx + lx, _vmStartCornerDy + ly);
                break;
            case HandleMode.Skew when _handleDirection is not null:
                ApplySkew(vm, _handleDirection, lx, ly);
                break;
        }
    }

    /// <summary>
    /// Skew-Mathematik: bei einer waagerechten Kante (N/S) bewegen wir beide
    /// Eckpunkte parallel ENTLANG der Kante (= horizontale Komponente lx),
    /// vertikale Cursor-Bewegung wird ignoriert. Bei einer senkrechten Kante
    /// (E/W) entsprechend nur die Y-Komponente. So entsteht ein klassischer
    /// "Skew along axis" wie in Photoshop.
    /// </summary>
    private void ApplySkew(TextFieldViewModel vm, string edge, double lx, double ly)
    {
        switch (edge)
        {
            case "N":
                vm.CornerNWdx = _skewStartA.Dx + lx;
                vm.CornerNEdx = _skewStartB.Dx + lx;
                break;
            case "S":
                vm.CornerSEdx = _skewStartA.Dx + lx;
                vm.CornerSWdx = _skewStartB.Dx + lx;
                break;
            case "E":
                vm.CornerNEdy = _skewStartA.Dy + ly;
                vm.CornerSEdy = _skewStartB.Dy + ly;
                break;
            case "W":
                vm.CornerSWdy = _skewStartA.Dy + ly;
                vm.CornerNWdy = _skewStartB.Dy + ly;
                break;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging && !_isRotating && !_isOriginDragging && _handleMode is null) return;

        // Sub-Task E: nach einem Distort/Skew-Drag könnten die Eckpunkt-
        // Offsets so weit nach außen gezogen worden sein, dass die Handles
        // nicht mehr hit-testbar sind (Pointer landet außerhalb des
        // ScrollViewer-Viewports → Avalonia kann das visuelle Element nicht
        // mehr treffen). Wir klemmen daher die Cornerxx-Offsets auf einen
        // sicheren Bereich, sobald der Drag endet.
        if ((_handleMode == HandleMode.Distort || _handleMode == HandleMode.Skew)
            && DataContext is TextFieldViewModel vm)
        {
            ClampCornerOffsets(vm);
        }

        _isDragging = false;
        _isRotating = false;
        _isOriginDragging = false;
        _handleMode = null;
        _handleDirection = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// Klemmt die 4 Eckpunkt-Offsets so, dass jeder Eckpunkt maximal eine
    /// halbe Frame-Größe (max(W, H) / 2) außerhalb der unrotated Box landen
    /// darf. Damit bleibt der Handle nach dem Release immer hit-testbar —
    /// der User kann ihn wieder anklicken und weiterziehen, ohne dass er
    /// „verloren" geht. (Notausgang für bereits weit verlorene Punkte:
    /// <see cref="TextFieldViewModel.ResetCornerOffsets"/> über den
    /// „Eckpunkte zurücksetzen"-Button im Properties-Panel.)
    /// </summary>
    public static void ClampCornerOffsets(TextFieldViewModel vm)
    {
        var maxOffset = Math.Max(vm.Width, vm.Height) / 2.0;
        if (maxOffset <= 0) return;

        vm.CornerNWdx = Math.Clamp(vm.CornerNWdx, -maxOffset, maxOffset);
        vm.CornerNWdy = Math.Clamp(vm.CornerNWdy, -maxOffset, maxOffset);
        vm.CornerNEdx = Math.Clamp(vm.CornerNEdx, -maxOffset, maxOffset);
        vm.CornerNEdy = Math.Clamp(vm.CornerNEdy, -maxOffset, maxOffset);
        vm.CornerSEdx = Math.Clamp(vm.CornerSEdx, -maxOffset, maxOffset);
        vm.CornerSEdy = Math.Clamp(vm.CornerSEdy, -maxOffset, maxOffset);
        vm.CornerSWdx = Math.Clamp(vm.CornerSWdx, -maxOffset, maxOffset);
        vm.CornerSWdy = Math.Clamp(vm.CornerSWdy, -maxOffset, maxOffset);
    }

    private void SelectInOwner()
    {
        if (DataContext is not TextFieldViewModel vm) return;
        var window = this.FindAncestorOfType<Window>();
        if (window?.DataContext is MainWindowViewModel owner)
            owner.SelectedTextField = vm;
    }

    /// <summary>
    /// Winkel in Grad (im Uhrzeigersinn, 0° = nach rechts), berechnet vom
    /// Drehpunkt des Modell-Rechtecks zum Punkt p in Canvas-Coords. Der
    /// Drehpunkt (= Origin) ergibt sich aus
    /// <see cref="TextFieldViewModel.RotationOriginRelX"/>/<see cref="TextFieldViewModel.RotationOriginRelY"/>;
    /// Default ist die Frame-Mitte. Avalonia's RotateTransform misst Winkel
    /// ebenfalls im Uhrzeigersinn, weil Y in Bildschirmkoordinaten nach
    /// unten zeigt.
    /// </summary>
    private static double AngleFromOriginDeg(Point p, TextFieldViewModel vm)
    {
        var origin = vm.RotationOriginAbsolute;
        return Math.Atan2(p.Y - origin.Y, p.X - origin.X) * 180.0 / Math.PI;
    }

    /// <summary>
    /// Inverse Rotation um <paramref name="rotationDeg"/> auf den Vektor (dx, dy).
    /// Liefert das Cursor-Delta in lokalen (unrotierten) Frame-Coords.
    /// </summary>
    public static (double X, double Y) InverseRotate(double dx, double dy, double rotationDeg)
    {
        var rad = -rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        return (dx * cos - dy * sin, dx * sin + dy * cos);
    }

    public static (double X, double Y) ComputeDrag(
        double startX, double startY, double dx, double dy)
        => (startX + dx, startY + dy);

    public static (double X, double Y, double Width, double Height) ComputeResize(
        string direction,
        double startX, double startY, double startW, double startH,
        double dx, double dy,
        double minSize = MinSize,
        bool keepAspect = false)
    {
        // Aspekt-Lock (Alt-Modifier): Cursor-Delta auf die Frame-Diagonale
        // projizieren, sodass W/H-Verhältnis erhalten bleibt. Welche
        // Cursor-Komponente "dominant" ist, entscheidet die größere relative
        // Bewegung — Photoshop-Konvention. Wirkt nur an Eck-Handles, weil
        // an Edge-Handles ohnehin nur eine Achse aktiv ist.
        if (keepAspect && startW > 0 && startH > 0
            && direction.Length == 2)  // NW/NE/SE/SW
        {
            var aspect = startW / startH;
            // dragSign passt das Vorzeichen so an, dass beide Achsen in die
            // gleiche logische Richtung wachsen (bei N/W-Ecken ist die
            // "Wachstums-Richtung" gegenläufig zum Cursor-Delta).
            var sx = direction.Contains('W') ? -1.0 : 1.0;
            var sy = direction.Contains('N') ? -1.0 : 1.0;
            var growX = sx * dx;
            var growY = sy * dy;
            // Auf den größeren der beiden relativen Anteile abrunden.
            var dominant = Math.Abs(growX) > Math.Abs(growY * aspect)
                ? growX
                : growY * aspect;
            dx = sx * dominant;
            dy = sy * (dominant / aspect);
        }

        var newX = startX;
        var newY = startY;
        var newW = startW;
        var newH = startH;

        if (direction.Contains('E')) newW = startW + dx;
        if (direction.Contains('S')) newH = startH + dy;
        if (direction.Contains('W'))
        {
            newW = startW - dx;
            newX = startX + dx;
        }
        if (direction.Contains('N'))
        {
            newH = startH - dy;
            newY = startY + dy;
        }

        if (newW < minSize)
        {
            if (direction.Contains('W')) newX = startX + (startW - minSize);
            newW = minSize;
        }
        if (newH < minSize)
        {
            if (direction.Contains('N')) newY = startY + (startH - minSize);
            newH = minSize;
        }

        return (newX, newY, newW, newH);
    }
}
