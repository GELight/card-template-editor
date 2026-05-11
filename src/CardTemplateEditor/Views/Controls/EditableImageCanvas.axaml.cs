using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Views.Controls;

public partial class EditableImageCanvas : UserControl
{
    /// <summary>
    /// Hardcoded Akzentfarbe für den Fokus-Rahmen. Bewusst NICHT
    /// SystemAccentColorBrush, weil der je nach Theme/Plattform fast unsichtbar
    /// werden kann (transparent, hintergrundnah). Mit fester Farbe ist der
    /// fokussierte Slot zuverlässig erkennbar — Pink hebt sich auf dem
    /// dunklen Dracula-BG kräftig vom restlichen Lila-Akzent ab.
    /// </summary>
    private static readonly IBrush SelectedBorderBrush =
        new SolidColorBrush(Color.FromRgb(0xFF, 0x79, 0xC6)); // Dracula Pink

    private static readonly IBrush UnselectedBorderBrush = Brushes.Transparent;

    /// <summary>
    /// Aktueller Skalen-Faktor des LayoutTransform (Auto-Fit × User-Zoom).
    /// Wird in <see cref="RefreshEffectiveScale"/> gesetzt und auf alle
    /// TextField-VMs des gebundenen Slots gepusht, damit Borders + Handles
    /// in Screen-Pixeln konstant bleiben (1 / scale).
    /// </summary>
    public static readonly StyledProperty<double> EffectiveScaleProperty =
        AvaloniaProperty.Register<EditableImageCanvas, double>(
            nameof(EffectiveScale), defaultValue: 1.0);

    public double EffectiveScale
    {
        get => GetValue(EffectiveScaleProperty);
        set => SetValue(EffectiveScaleProperty, value);
    }

    private ImageSlotViewModel? _boundSlot;

    // Drag-Pan-State: Strg+Linksklick-Drag verschiebt den ScrollViewer-Offset.
    // _panStartPoint = Cursor-Position beim PointerDown im Koordinatensystem
    // des ScrollViewers; _panStartOffset = Offset zum selben Zeitpunkt.
    // PointerMoved während des Drags rechnet daraus den neuen Offset (siehe
    // ComputePannedScrollOffset). Strg ist die übliche Photoshop-/Editor-
    // Konvention für "Hand-Tool-Pan ohne Modus zu wechseln".
    private bool _isPanning;
    private Point _panStartPoint;
    private Vector _panStartOffset;

    public EditableImageCanvas()
    {
        InitializeComponent();
        // Klick auf den Bild-Container (außerhalb eines TextFieldFrame) markiert
        // diesen Slot als aktiven Drop-Target im MainWindowViewModel.
        AddHandler(PointerPressedEvent, OnContainerPressed, handledEventsToo: false);

        // Strg+Mausrad zoomt — handledEventsToo=true, weil der ScrollViewer das
        // Wheel-Event sonst vorab als Scroll konsumiert.
        AddHandler(PointerWheelChangedEvent, OnPointerWheelZoom,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        // Drag-Pan: Strg+Linksklick startet einen Pan, der Cursor folgt
        // dem Bild. Tunnel-Phase, damit wir VOR dem TextFieldFrame den Drag
        // beanspruchen können — sonst würde der TextFieldFrame.DragBorder
        // das PointerPressed konsumieren und sein eigenes Move-Drag starten.
        AddHandler(PointerPressedEvent, OnPanPointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPanPointerMoved,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPanPointerReleased,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => DetachSlot();
        // EffectiveScale neu berechnen, sobald sich die Slot-Größe ändert
        // (Resize, Splitter-Drag) oder das Bitmap geladen wird.
        ZoomScroller.SizeChanged += (_, _) => RefreshEffectiveScale();
    }

    /// <summary>
    /// Startet einen Drag-Pan ausschließlich beim Middle-Mouse-Button.
    /// Strg ist seit Iteration 14 für den Distort-Modifier reserviert; eine
    /// Strg+Linksklick-Kombo würde sonst auf einem TextFieldFrame nie als
    /// Distort ankommen (Tunnel-Phase + Pointer-Capture im Pan würden die
    /// Frame-Handler überschreiben).
    /// </summary>
    private void OnPanPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsMiddleButtonPressed) return;

        _isPanning = true;
        _panStartPoint = e.GetPosition(ZoomScroller);
        _panStartOffset = ZoomScroller.Offset;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPanPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning) return;
        var current = e.GetPosition(ZoomScroller);
        var newOffset = ComputePannedScrollOffset(
            _panStartOffset, _panStartPoint, current,
            ZoomScroller.Extent, ZoomScroller.Viewport);
        ZoomScroller.Offset = newOffset;
        e.Handled = true;
    }

    private void OnPanPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        Cursor = Cursor.Default;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// Reine Pan-Mathematik: aus Start-Offset, Start-Cursor und aktuellem
    /// Cursor folgt der neue Offset = StartOffset − (Cursor − Start).
    /// Wird auf den scrollbaren Bereich [0, Extent − Viewport] geclamped,
    /// damit der ScrollViewer keinen Negativ-Offset zeigt.
    /// </summary>
    public static Vector ComputePannedScrollOffset(
        Vector startOffset, Point startCursor, Point currentCursor,
        Size extent, Size viewport)
    {
        var deltaX = currentCursor.X - startCursor.X;
        var deltaY = currentCursor.Y - startCursor.Y;
        var rawX = startOffset.X - deltaX;
        var rawY = startOffset.Y - deltaY;
        var maxX = Math.Max(0, extent.Width - viewport.Width);
        var maxY = Math.Max(0, extent.Height - viewport.Height);
        return new Vector(
            Math.Max(0, Math.Min(maxX, rawX)),
            Math.Max(0, Math.Min(maxY, rawY)));
    }

    private const double ZoomStep = 1.25;

    private void OnZoomInClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ImageSlotViewModel vm) vm.ZoomFactor *= ZoomStep;
    }

    private void OnZoomOutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ImageSlotViewModel vm) vm.ZoomFactor /= ZoomStep;
    }

    private void OnZoomResetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ImageSlotViewModel vm) vm.ZoomFactor = 1.0;
    }

    /// <summary>
    /// Strg+Mausrad: zoomt mit Maus-Anker — der Punkt unter dem Cursor bleibt
    /// nach dem Zoom an derselben Bildschirm-Position. Mathematik in
    /// <see cref="ComputeMouseAnchoredScrollOffset"/> getrennt, damit sie
    /// ohne UI-Kontext testbar ist.
    /// </summary>
    private void OnPointerWheelZoom(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        if (DataContext is not ImageSlotViewModel vm) return;

        // VOR dem Zoom: cursorInContent direkt in Bild-Pixel-Koordinaten
        // (Avalonia rechnet die Layout-Transform automatisch zurück, sodass
        // GetPosition(ContentRoot) den unter dem Cursor liegenden Bild-Pixel
        // liefert — robuster als manuelle Scroll-Offset-Mathe).
        var cursorInContent = e.GetPosition(ContentRoot);
        var cursorInViewport = e.GetPosition(ZoomScroller);

        var factor = e.Delta.Y > 0 ? ZoomStep : 1.0 / ZoomStep;
        vm.ZoomFactor *= factor;

        // Layout zwingen: neue EffectiveScale → neue Content-Größe →
        // ScrollViewer kennt die neue Scrollable-Extent.
        RefreshEffectiveScale();
        ZoomLayoutHost.UpdateLayout();
        ZoomScroller.UpdateLayout();
        var newScale = GetEffectiveScale().ScaleX;
        if (newScale <= 0) newScale = 1;

        var newOffset = ComputeMouseAnchorFromContentPos(
            cursorInContent, cursorInViewport, newScale);
        ZoomScroller.Offset = new Vector(
            Math.Max(0, newOffset.X), Math.Max(0, newOffset.Y));

        e.Handled = true;
    }

    /// <summary>
    /// Robuster Maus-Anker: Pixel-Position unter dem Cursor wird vor dem
    /// Zoom direkt aus <c>GetPosition(ContentRoot)</c> ausgelesen. Nach dem
    /// Zoom muss derselbe Pixel unter derselben Viewport-Position landen:
    /// pixelPos × newScale − cursorInViewport = neuer Scroll-Offset.
    /// </summary>
    public static Point ComputeMouseAnchorFromContentPos(
        Point cursorInContent,
        Point cursorInViewport,
        double newScale)
    {
        if (newScale <= 0) newScale = 1;
        return new Point(
            cursorInContent.X * newScale - cursorInViewport.X,
            cursorInContent.Y * newScale - cursorInViewport.Y);
    }

    /// <summary>
    /// Berechnet den ScrollViewer-Offset nach einem Zoom-Wechsel so, dass der
    /// Bildpunkt unter dem Cursor seine Position behält.
    ///
    /// Vor dem Zoom: contentPoint = (oldOffset + cursor) / oldScale
    /// Nach dem Zoom muss derselbe contentPoint wieder unter dem Cursor liegen:
    /// newOffset = contentPoint * newScale - cursor
    /// </summary>
    public static Point ComputeMouseAnchoredScrollOffset(
        Point oldOffset,
        Point cursorInViewport,
        double oldScale,
        double newScale)
    {
        if (oldScale <= 0) oldScale = 1;
        if (newScale <= 0) newScale = 1;
        var contentX = (oldOffset.X + cursorInViewport.X) / oldScale;
        var contentY = (oldOffset.Y + cursorInViewport.Y) / oldScale;
        return new Point(
            contentX * newScale - cursorInViewport.X,
            contentY * newScale - cursorInViewport.Y);
    }

    /// <summary>
    /// Auto-Fit-Faktor (= so, dass das Bild komplett in den ScrollViewer
    /// passt) multipliziert mit dem User-ZoomFactor. Wird auf das LayoutTransform
    /// gepusht. Berechnung in <see cref="ComputeEffectiveScale"/> getrennt für
    /// Tests.
    /// </summary>
    private void RefreshEffectiveScale()
    {
        if (DataContext is not ImageSlotViewModel vm) return;
        var pw = vm.PixelWidth;
        var ph = vm.PixelHeight;
        var bounds = ZoomScroller.Bounds;
        var scale = ComputeEffectiveScale(bounds.Width, bounds.Height, pw, ph, vm.ZoomFactor);
        var st = GetEffectiveScale();
        st.ScaleX = scale;
        st.ScaleY = scale;
        EffectiveScale = scale;
        // Borders + Handles der TextFields skalieren in Screen-Pixeln konstant
        // — wir pushen den aktuellen Faktor in jedes TextField-VM dieses Slots.
        foreach (var f in vm.TextFields)
            f.EffectiveScale = scale;
        // Avalonia invalidiert Measure beim Setzen einer ScaleTransform-Property
        // nicht zuverlässig, wenn dieselbe Transform-Instanz mutiert wird —
        // ohne expliziten Aufruf bleibt die transformierte Größe beim Resize
        // hängen, das Bild verschwindet aus dem Sichtbereich. Ein
        // InvalidateMeasure auf dem LayoutTransformControl erzwingt die
        // erneute Größenberechnung im nächsten Layout-Pass.
        ZoomLayoutHost.InvalidateMeasure();
    }

    /// <summary>
    /// Liefert den ScaleTransform aus dem LayoutTransform — der ist als
    /// Setter-Sub-Element im XAML deklariert und bekommt deshalb keinen
    /// generierten Feld-Reference, aber wir können ihn jederzeit aus
    /// <see cref="LayoutTransformControl.LayoutTransform"/> casten.
    /// </summary>
    private ScaleTransform GetEffectiveScale()
    {
        if (ZoomLayoutHost.LayoutTransform is ScaleTransform st) return st;
        var fallback = new ScaleTransform();
        ZoomLayoutHost.LayoutTransform = fallback;
        return fallback;
    }

    /// <summary>
    /// Reine Skalierungs-Mathematik: Auto-Fit (Uniform-Stretch in den
    /// Viewport) × User-ZoomFactor. Defensive Defaults verhindern Division
    /// durch 0 und liefern 1.0 ab, solange Layout noch nicht gemessen ist.
    /// </summary>
    public static double ComputeEffectiveScale(
        double viewportWidth, double viewportHeight,
        double pixelWidth, double pixelHeight,
        double userZoomFactor)
    {
        if (pixelWidth < 1 || pixelHeight < 1) return Math.Max(0.01, userZoomFactor);
        if (viewportWidth < 1 || viewportHeight < 1) return Math.Max(0.01, userZoomFactor);
        var fit = Math.Min(viewportWidth / pixelWidth, viewportHeight / pixelHeight);
        if (fit <= 0) fit = 1;
        return fit * userZoomFactor;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachSlot();
        _boundSlot = DataContext as ImageSlotViewModel;
        if (_boundSlot is not null)
        {
            _boundSlot.PropertyChanged += OnSlotPropertyChanged;
            // Neue TextFields müssen den aktuellen Scale erben.
            _boundSlot.TextFields.CollectionChanged += OnTextFieldsChanged;
        }
        UpdateSelectionVisual();
        RefreshEffectiveScale();
    }

    private void OnTextFieldsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        var scale = EffectiveScale;
        foreach (TextFieldViewModel f in e.NewItems)
            f.EffectiveScale = scale;
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImageSlotViewModel.IsSelected))
            UpdateSelectionVisual();
        if (e.PropertyName == nameof(ImageSlotViewModel.ZoomFactor) ||
            e.PropertyName == nameof(ImageSlotViewModel.PixelWidth) ||
            e.PropertyName == nameof(ImageSlotViewModel.PixelHeight))
            RefreshEffectiveScale();
    }

    private void DetachSlot()
    {
        if (_boundSlot is not null)
        {
            _boundSlot.PropertyChanged -= OnSlotPropertyChanged;
            _boundSlot.TextFields.CollectionChanged -= OnTextFieldsChanged;
        }
        _boundSlot = null;
    }

    /// <summary>
    /// Setzt den BorderBrush deterministisch — kein Style-Class-Roundtrip,
    /// weil das im Compiled-Bindings-Setup hier nicht zuverlässig griff.
    /// </summary>
    private void UpdateSelectionVisual()
    {
        OuterBorder.BorderBrush = _boundSlot?.IsSelected == true
            ? SelectedBorderBrush
            : UnselectedBorderBrush;
    }

    private void OnContainerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ImageSlotViewModel slot) return;
        var window = this.FindAncestorOfType<Window>();
        if (window?.DataContext is MainWindowViewModel owner)
            owner.SelectedImageSlot = slot;
    }
}
