using Avalonia.Media;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;

namespace CardTemplateEditor.ViewModels;

public class TextFieldViewModel : ViewModelBase
{
    private readonly TextField _model;
    private WarpPreviewService.Layout? _warpPreviewCache;
    private bool _warpPreviewCached;

    public TextFieldViewModel(TextField model)
    {
        _model = model;
    }

    public TextField Model => _model;

    public Guid Id => _model.Id;

    public Guid ImageSlotId
    {
        get => _model.ImageSlotId;
        set
        {
            if (_model.ImageSlotId == value) return;
            _model.ImageSlotId = value;
            OnPropertyChanged();
        }
    }

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

    public double X
    {
        get => _model.X;
        set
        {
            if (_model.X == value) return;
            _model.X = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OuterX));
        }
    }

    public double Y
    {
        get => _model.Y;
        set
        {
            if (_model.Y == value) return;
            _model.Y = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OuterY));
        }
    }

    public double Width
    {
        get => _model.Width;
        set
        {
            if (_model.Width == value) return;
            _model.Width = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OuterWidth));
            NotifyWarpGeometryChanged();
        }
    }

    public double Height
    {
        get => _model.Height;
        set
        {
            if (_model.Height == value) return;
            _model.Height = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OuterHeight));
            NotifyWarpGeometryChanged();
        }
    }

    /// <summary>
    /// Drehung in Grad (Uhrzeigersinn). Drehzentrum ist der durch
    /// <see cref="RotationOriginPoint"/> definierte Punkt (default = Frame-Mitte).
    /// </summary>
    public double Rotation
    {
        get => _model.Rotation;
        set
        {
            // Modulo auf [-180, 180] mit kleiner Snap-Toleranz; eingehende NaNs
            // werden auf 0 normalisiert, sonst kippt das RenderTransform.
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            var normalized = NormalizeAngle(value);
            if (_model.Rotation == normalized) return;
            _model.Rotation = normalized;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Drehpunkt-Position relativ zur Frame-Breite (0 = links, 1 = rechts).
    /// Werte außerhalb [0, 1] sind erlaubt — User darf den Drehpunkt auch
    /// leicht außerhalb der Box platzieren. Default 0.5 = horizontale Mitte.
    /// </summary>
    public double RotationOriginRelX
    {
        get => _model.RotationOriginRelX;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.5;
            if (_model.RotationOriginRelX == value) return;
            _model.RotationOriginRelX = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RotationOriginPoint));
            OnPropertyChanged(nameof(RotationOriginAbsolute));
            OnPropertyChanged(nameof(RotationOriginCanvasX));
            OnPropertyChanged(nameof(RotationOriginCanvasY));
            OnPropertyChanged(nameof(RotationOriginRelative));
        }
    }

    /// <summary>Drehpunkt-Position relativ zur Frame-Höhe (0 = oben, 1 = unten).</summary>
    public double RotationOriginRelY
    {
        get => _model.RotationOriginRelY;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.5;
            if (_model.RotationOriginRelY == value) return;
            _model.RotationOriginRelY = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RotationOriginPoint));
            OnPropertyChanged(nameof(RotationOriginAbsolute));
            OnPropertyChanged(nameof(RotationOriginCanvasX));
            OnPropertyChanged(nameof(RotationOriginCanvasY));
            OnPropertyChanged(nameof(RotationOriginRelative));
        }
    }

    /// <summary>
    /// Absolute Position des Drehpunkts in Bild-Pixel-Coords (vor Rotation).
    /// Wird vom <see cref="Services.ExportService"/> und der Pointer-Mathematik
    /// genutzt; UI-Bindings für die Cross-Marker-Position siehe
    /// <see cref="RotationOriginCanvasX"/>/<see cref="RotationOriginCanvasY"/>.
    /// </summary>
    public Avalonia.Point RotationOriginAbsolute =>
        new(_model.X + _model.Width * RotationOriginRelX,
            _model.Y + _model.Height * RotationOriginRelY);

    /// <summary>
    /// Drehpunkt-Position im OuterRoot-Koordinatenraum des UserControls
    /// (Top-Left = (0,0), inkl. OuterPadding). Wird für Wireframe + Cross-
    /// Marker im Editor verwendet.
    /// </summary>
    public Avalonia.Point RotationOriginPoint =>
        new(Models.TextFieldGeometry.OuterPadding + _model.Width * RotationOriginRelX,
            Models.TextFieldGeometry.OuterPadding + _model.Height * RotationOriginRelY);

    /// <summary>Canvas.Left-Position für den Cross-Marker (Mitte des Markers liegt auf dem Origin).</summary>
    public double RotationOriginCanvasX =>
        RotationOriginPoint.X - EffectiveOriginCrossHalf;
    public double RotationOriginCanvasY =>
        RotationOriginPoint.Y - EffectiveOriginCrossHalf;

    /// <summary>
    /// RelativePoint für <c>UserControl.RenderTransformOrigin</c>. Da der
    /// UserControl-Bereich um <see cref="Models.TextFieldGeometry.OuterPadding"/>
    /// größer ist als die Modell-Box, mappen wir den Origin zuerst auf den
    /// gesamten OuterRoot-Raum (0..OuterWidth) und packen das in einen
    /// RelativePoint mit Unit=Relative, damit Avalonia ihn beim Rendern
    /// als Anteil von Bounds versteht.
    /// </summary>
    public Avalonia.RelativePoint RotationOriginRelative
    {
        get
        {
            var ow = OuterWidth;
            var oh = OuterHeight;
            if (ow <= 0 || oh <= 0) return new Avalonia.RelativePoint(0.5, 0.5,
                Avalonia.RelativeUnit.Relative);
            return new Avalonia.RelativePoint(
                RotationOriginPoint.X / ow,
                RotationOriginPoint.Y / oh,
                Avalonia.RelativeUnit.Relative);
        }
    }

    private static double NormalizeAngle(double deg)
    {
        // Halten in (-180, 180]: vereinfacht Diff-Berechnungen im Rotation-Drag.
        var x = deg % 360.0;
        if (x > 180) x -= 360;
        if (x <= -180) x += 360;
        return x;
    }

    /// <summary>X-Position des äußeren UserControl-Rechtecks (inkl. Hit-Test-Polster).</summary>
    public double OuterX => _model.X - Models.TextFieldGeometry.OuterPadding;

    /// <summary>Y-Position des äußeren UserControl-Rechtecks (inkl. Hit-Test-Polster).</summary>
    public double OuterY => _model.Y - Models.TextFieldGeometry.OuterPadding;

    /// <summary>Breite des äußeren UserControl-Rechtecks (inkl. Hit-Test-Polster).</summary>
    public double OuterWidth => _model.Width + 2 * Models.TextFieldGeometry.OuterPadding;

    /// <summary>Höhe des äußeren UserControl-Rechtecks (inkl. Hit-Test-Polster).</summary>
    public double OuterHeight => _model.Height + 2 * Models.TextFieldGeometry.OuterPadding;

    /// <summary>
    /// Eck-Offsets der projektiven Verzerrung (NW, NE, SE, SW).
    /// 0/0 = achsenparalleles Rechteck.
    /// </summary>
    public double CornerNWdx
    {
        get => _model.CornerNWdx;
        set
        {
            if (_model.CornerNWdx == value) return;
            _model.CornerNWdx = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWarped));
            NotifyWarpGeometryChanged();
        }
    }
    public double CornerNWdy
    {
        get => _model.CornerNWdy;
        set
        {
            if (_model.CornerNWdy == value) return;
            _model.CornerNWdy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWarped));
            NotifyWarpGeometryChanged();
        }
    }
    public double CornerNEdx
    {
        get => _model.CornerNEdx;
        set
        {
            if (_model.CornerNEdx == value) return;
            _model.CornerNEdx = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWarped));
            NotifyWarpGeometryChanged();
        }
    }
    public double CornerNEdy
    {
        get => _model.CornerNEdy;
        set
        {
            if (_model.CornerNEdy == value) return;
            _model.CornerNEdy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWarped));
            NotifyWarpGeometryChanged();
        }
    }
    public double CornerSEdx
    {
        get => _model.CornerSEdx;
        set
        {
            if (_model.CornerSEdx == value) return;
            _model.CornerSEdx = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWarped));
            NotifyWarpGeometryChanged();
        }
    }
    public double CornerSEdy
    {
        get => _model.CornerSEdy;
        set
        {
            if (_model.CornerSEdy == value) return;
            _model.CornerSEdy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWarped));
            NotifyWarpGeometryChanged();
        }
    }
    public double CornerSWdx
    {
        get => _model.CornerSWdx;
        set
        {
            if (_model.CornerSWdx == value) return;
            _model.CornerSWdx = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWarped));
            NotifyWarpGeometryChanged();
        }
    }
    public double CornerSWdy
    {
        get => _model.CornerSWdy;
        set
        {
            if (_model.CornerSWdy == value) return;
            _model.CornerSWdy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWarped));
            NotifyWarpGeometryChanged();
        }
    }

    /// <summary>
    /// Position der vier Eckpunkte des (eventuell verzerrten) Quadrilaterals
    /// im OuterRoot-Koordinatenraum (Bezugspunkt: Top-Left der UserControl).
    /// Wird vom Editor genutzt für Wireframe-Linien und Handle-Platzierung.
    /// </summary>
    public Avalonia.Point WarpNWPoint =>
        new(Models.TextFieldGeometry.OuterPadding + CornerNWdx,
            Models.TextFieldGeometry.OuterPadding + CornerNWdy);
    public Avalonia.Point WarpNEPoint =>
        new(Models.TextFieldGeometry.OuterPadding + Width + CornerNEdx,
            Models.TextFieldGeometry.OuterPadding + CornerNEdy);
    public Avalonia.Point WarpSEPoint =>
        new(Models.TextFieldGeometry.OuterPadding + Width + CornerSEdx,
            Models.TextFieldGeometry.OuterPadding + Height + CornerSEdy);
    public Avalonia.Point WarpSWPoint =>
        new(Models.TextFieldGeometry.OuterPadding + CornerSWdx,
            Models.TextFieldGeometry.OuterPadding + Height + CornerSWdy);

    // Unifizierter Handle-Satz (8 Stück: 4 Ecken + 4 Kantenmittelpunkte). Alle
    // Handles sitzen direkt am verzerrten Quad-Punkt — bei aktivem Warp folgt
    // der Handle dem Eckpunkt automatisch. Größe und Border-Dicke werden in
    // Screen-Pixeln konstant gehalten; daher dividieren wir die Basis-Werte
    // durch <see cref="EffectiveScale"/>, sodass z. B. ein 4000×3000-Bild,
    // das per Auto-Fit auf 0.2 verkleinert ist, weiterhin 8 px große Handles
    // auf dem Bildschirm zeigt.
    public const double HandleSize = 8.0;
    public const double HandleHalfSize = HandleSize / 2.0;
    public const double RotateHandleSize = 14.0;
    public const double BaseBorderThickness = 1.0;
    public const double OriginCrossSize = 12.0;

    private double _effectiveScale = 1.0;

    /// <summary>
    /// Zoom-Faktor des umgebenden EditableImageCanvas (Auto-Fit × User-Zoom).
    /// Wird vom Editor-Code-Behind beim Anhängen ans Visual-Tree gesetzt und
    /// bei Zoom-Änderungen synchronisiert. Default 1.0, sodass das VM auch
    /// ohne Editor (z. B. in Pure-Tests oder im Export) konsistent funktioniert.
    /// </summary>
    public double EffectiveScale
    {
        get => _effectiveScale;
        set
        {
            // Defensive Untergrenze, damit eine versehentlich auf 0 oder negativ
            // gesetzte Skalierung das Layout nicht zerstört.
            var clamped = value > 0.0001 ? value : 1.0;
            if (_effectiveScale == clamped) return;
            _effectiveScale = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveHandleSize));
            OnPropertyChanged(nameof(EffectiveHandleHalfSize));
            OnPropertyChanged(nameof(EffectiveRotateHandleSize));
            OnPropertyChanged(nameof(EffectiveBorderThickness));
            OnPropertyChanged(nameof(ChromeBorderThickness));
            // Handle-Positionen hängen über EffectiveHandleHalfSize indirekt
            // vom Scale ab — also müssen wir alle Position-Notifications
            // mitfeuern.
            NotifyWarpGeometryChanged();
        }
    }

    private bool _isSkewActive;

    /// <summary>
    /// True, solange der User den Skew-Modus hält (Shift+Alt). Wird vom
    /// MainWindow-Code-Behind als Reaktion auf Modifier-Tasten gesetzt;
    /// das XAML blendet daran die 4 Eck-Handles ein/aus, weil sie im
    /// Skew-Modus keine Wirkung haben (würden sonst nur verwirren).
    /// </summary>
    public bool IsSkewActive
    {
        get => _isSkewActive;
        set
        {
            if (_isSkewActive == value) return;
            _isSkewActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowCornerHandles));
        }
    }

    private bool _isSelected;

    /// <summary>
    /// True, wenn dieses TextField in MainWindowViewModel.SelectedTextField
    /// liegt — wird vom Owner gespiegelt (siehe OnSelectedTextFieldChanged).
    /// Steuert die Sichtbarkeit von Border, Resize-/Rotate-/Origin-Handles
    /// und Wireframe: ohne Fokus zeigt der Frame nur Text + Hit-Test-Fläche,
    /// damit das Layout nicht von Edit-Chrome verstellt wird.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowChrome));
            OnPropertyChanged(nameof(ChromeBorderThickness));
            OnPropertyChanged(nameof(ShowCornerHandles));
            OnPropertyChanged(nameof(ShowWireframe));
        }
    }

    /// <summary>
    /// Alle Edit-Chrome-Elemente (Border, Edge-Handles, Rotate-Handle,
    /// Origin-Marker, Wireframe) sind genau dann sichtbar, wenn das Feld
    /// selektiert ist.
    /// </summary>
    public bool ShowChrome => _isSelected;

    /// <summary>
    /// BorderThickness des DragBorders: 0 wenn nicht selektiert (= kein
    /// sichtbarer Rahmen), sonst der zoom-kompensierte Default. Wir binden
    /// hier statt auf <see cref="EffectiveBorderThickness"/>, damit unselektierte
    /// Felder keine Linie zeigen — Hit-Test bleibt über Background=Transparent
    /// + Padding=8 möglich.
    /// </summary>
    public double ChromeBorderThickness =>
        _isSelected ? EffectiveBorderThickness : 0.0;

    /// <summary>
    /// Sichtbarkeits-Flag für die 4 Eck-Handles. Sichtbar nur wenn das Feld
    /// selektiert ist UND nicht gerade der Skew-Modus aktiv ist (Skew wirkt
    /// nur an Edge-Handles, Eck-Handles würden sonst nur verwirren).
    /// </summary>
    public bool ShowCornerHandles => _isSelected && !_isSkewActive;

    /// <summary>Größe der 8 Resize-Handles in Bild-Pixel-Coords (= konstant in Screen-Pixeln).</summary>
    public double EffectiveHandleSize => HandleSize / _effectiveScale;
    public double EffectiveHandleHalfSize => HandleHalfSize / _effectiveScale;
    public double EffectiveRotateHandleSize => RotateHandleSize / _effectiveScale;
    public double EffectiveBorderThickness => BaseBorderThickness / _effectiveScale;
    public double EffectiveOriginCrossSize => OriginCrossSize / _effectiveScale;
    public double EffectiveOriginCrossHalf => (OriginCrossSize / 2.0) / _effectiveScale;

    // Canvas.Left/Top der 4 Eck-Handles am verzerrten Quad.
    public double HandleNWX => WarpNWPoint.X - EffectiveHandleHalfSize;
    public double HandleNWY => WarpNWPoint.Y - EffectiveHandleHalfSize;
    public double HandleNEX => WarpNEPoint.X - EffectiveHandleHalfSize;
    public double HandleNEY => WarpNEPoint.Y - EffectiveHandleHalfSize;
    public double HandleSEX => WarpSEPoint.X - EffectiveHandleHalfSize;
    public double HandleSEY => WarpSEPoint.Y - EffectiveHandleHalfSize;
    public double HandleSWX => WarpSWPoint.X - EffectiveHandleHalfSize;
    public double HandleSWY => WarpSWPoint.Y - EffectiveHandleHalfSize;

    // Canvas.Left/Top der 4 Edge-Handles (Mittelpunkte der verzerrten Kanten).
    public double HandleNX => (WarpNWPoint.X + WarpNEPoint.X) / 2.0 - EffectiveHandleHalfSize;
    public double HandleNY => (WarpNWPoint.Y + WarpNEPoint.Y) / 2.0 - EffectiveHandleHalfSize;
    public double HandleEX => (WarpNEPoint.X + WarpSEPoint.X) / 2.0 - EffectiveHandleHalfSize;
    public double HandleEY => (WarpNEPoint.Y + WarpSEPoint.Y) / 2.0 - EffectiveHandleHalfSize;
    public double HandleSX => (WarpSEPoint.X + WarpSWPoint.X) / 2.0 - EffectiveHandleHalfSize;
    public double HandleSY => (WarpSEPoint.Y + WarpSWPoint.Y) / 2.0 - EffectiveHandleHalfSize;
    public double HandleWX => (WarpSWPoint.X + WarpNWPoint.X) / 2.0 - EffectiveHandleHalfSize;
    public double HandleWY => (WarpSWPoint.Y + WarpNWPoint.Y) / 2.0 - EffectiveHandleHalfSize;

    private void NotifyWarpGeometryChanged()
    {
        OnPropertyChanged(nameof(WarpNWPoint));
        OnPropertyChanged(nameof(WarpNEPoint));
        OnPropertyChanged(nameof(WarpSEPoint));
        OnPropertyChanged(nameof(WarpSWPoint));
        // Eck-Handles
        OnPropertyChanged(nameof(HandleNWX)); OnPropertyChanged(nameof(HandleNWY));
        OnPropertyChanged(nameof(HandleNEX)); OnPropertyChanged(nameof(HandleNEY));
        OnPropertyChanged(nameof(HandleSEX)); OnPropertyChanged(nameof(HandleSEY));
        OnPropertyChanged(nameof(HandleSWX)); OnPropertyChanged(nameof(HandleSWY));
        // Edge-Handles
        OnPropertyChanged(nameof(HandleNX)); OnPropertyChanged(nameof(HandleNY));
        OnPropertyChanged(nameof(HandleEX)); OnPropertyChanged(nameof(HandleEY));
        OnPropertyChanged(nameof(HandleSX)); OnPropertyChanged(nameof(HandleSY));
        OnPropertyChanged(nameof(HandleWX)); OnPropertyChanged(nameof(HandleWY));
        // Origin-Marker (relative Position skaliert mit W/H)
        OnPropertyChanged(nameof(RotationOriginPoint));
        OnPropertyChanged(nameof(RotationOriginAbsolute));
        OnPropertyChanged(nameof(RotationOriginCanvasX));
        OnPropertyChanged(nameof(RotationOriginCanvasY));
        OnPropertyChanged(nameof(RotationOriginRelative));
        OnPropertyChanged(nameof(ShowWireframe));
        InvalidateWarpPreview();
    }

    /// <summary>
    /// Cache verwerfen und Re-Rendern beim nächsten Binding-Read auslösen.
    /// Wird aus jedem Setter aufgerufen, dessen Wert das gerasterte Vorschau-
    /// Bitmap beeinflusst (Geometrie, Text, Schrift, Farbe, Ausrichtung,
    /// Stretch, LineHeight, LetterSpacing).
    /// </summary>
    private void InvalidateWarpPreview()
    {
        _warpPreviewCache = null;
        _warpPreviewCached = false;
        OnPropertyChanged(nameof(WarpPreviewBitmap));
        OnPropertyChanged(nameof(WarpPreviewOffsetX));
        OnPropertyChanged(nameof(WarpPreviewOffsetY));
        OnPropertyChanged(nameof(HasNonEmptyText));
        OnPropertyChanged(nameof(ShouldShowPreview));
        OnPropertyChanged(nameof(EditableTextOpacity));
    }

    private bool _isInEditMode;

    /// <summary>
    /// True, wenn das Feld gerade per Doppelklick in den Inline-Edit-Modus
    /// geschaltet wurde. Während Edit-Mode ist die TextBox sichtbar (Cursor +
    /// Selektion), die gerasterte Vorschau-Bitmap blendet sich aus, damit der
    /// User keinen "Doppel-Text" sieht.
    /// </summary>
    public bool IsInEditMode
    {
        get => _isInEditMode;
        set
        {
            if (_isInEditMode == value) return;
            _isInEditMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShouldShowPreview));
            OnPropertyChanged(nameof(EditableTextOpacity));
        }
    }

    /// <summary>True, wenn das Feld nicht-leeren Text hat (Preview rendert dann).</summary>
    public bool HasNonEmptyText => !string.IsNullOrEmpty(CurrentText);

    /// <summary>
    /// Sichtbarkeit der Preview-Bitmap: gerendert wird sie, sobald Text da ist
    /// UND wir nicht im Edit-Modus sind. Sonst sieht der User die TextBox.
    /// </summary>
    public bool ShouldShowPreview => HasNonEmptyText && !IsInEditMode;

    private WarpPreviewService.Layout? GetWarpPreviewLayout()
    {
        if (!HasNonEmptyText) return null;
        if (_warpPreviewCached) return _warpPreviewCache;
        _warpPreviewCache = WarpPreviewService.RenderPreview(_model);
        _warpPreviewCached = true;
        return _warpPreviewCache;
    }

    /// <summary>
    /// Live-Vorschau-Bitmap der 4-Punkt-Verzerrung. null, wenn nicht verzerrt
    /// oder Text leer. Wird lazy berechnet (per Cache) und bei jeder Änderung
    /// invalidert, die das Rendering beeinflusst.
    /// </summary>
    public Bitmap? WarpPreviewBitmap => GetWarpPreviewLayout()?.Bitmap;

    /// <summary>
    /// Canvas.Left-Position (in OuterRoot-Coords) für das Vorschau-Bitmap.
    /// Kann negativ sein, wenn Eckpunkte über das UserControl hinausgezogen
    /// wurden — das ist Absicht und wird vom Editor-Canvas (ClipToBounds=False)
    /// korrekt dargestellt.
    /// </summary>
    public double WarpPreviewOffsetX => GetWarpPreviewLayout()?.OffsetX ?? 0;

    /// <summary>Canvas.Top-Position für das Vorschau-Bitmap (siehe OffsetX).</summary>
    public double WarpPreviewOffsetY => GetWarpPreviewLayout()?.OffsetY ?? 0;

    /// <summary>
    /// 1, wenn die TextBox sichtbar sein soll: entweder kein Text vorhanden
    /// (User soll tippen können) oder Edit-Mode aktiv (Cursor/Selektion
    /// sichtbar). Sonst 0 — der User sieht stattdessen die exakte Render-
    /// Pipeline-Vorschau, die auch der PNG-Export nutzt. Zugriff auf den
    /// TextBox-Cursor/Klicks bleibt vom Opacity-Wert unberührt.
    /// </summary>
    public double EditableTextOpacity => (!HasNonEmptyText || IsInEditMode) ? 1.0 : 0.0;

    /// <summary>True, sobald mindestens ein Eckpunkt-Offset ungleich 0 ist.</summary>
    public bool IsWarped =>
        CornerNWdx != 0 || CornerNWdy != 0 ||
        CornerNEdx != 0 || CornerNEdy != 0 ||
        CornerSEdx != 0 || CornerSEdy != 0 ||
        CornerSWdx != 0 || CornerSWdy != 0;

    /// <summary>
    /// Wireframe (gestricheltes Quad an den verzerrten Eckpunkten) zeigen wir
    /// nur, wenn das Feld gerade selektiert ist UND verzerrt ist — sonst
    /// flutet eine ganze Karte mit Hilfslinien.
    /// </summary>
    public bool ShowWireframe => _isSelected && IsWarped;

    public string FontFamily
    {
        get => _model.FontFamily;
        set
        {
            if (_model.FontFamily == value) return;
            _model.FontFamily = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FontFamilyValue));
            InvalidateWarpPreview();
        }
    }

    public double StretchX
    {
        get => _model.StretchX;
        set
        {
            if (_model.StretchX == value) return;
            _model.StretchX = value;
            OnPropertyChanged();
            InvalidateWarpPreview();
        }
    }

    public double StretchY
    {
        get => _model.StretchY;
        set
        {
            if (_model.StretchY == value) return;
            _model.StretchY = value;
            OnPropertyChanged();
            InvalidateWarpPreview();
        }
    }

    public bool AutoFit
    {
        get => _model.AutoFit;
        set
        {
            if (_model.AutoFit == value) return;
            _model.AutoFit = value;
            OnPropertyChanged();
            InvalidateWarpPreview();
        }
    }

    /// <summary>
    /// Zeilenhöhe für mehrzeiligen Text. NaN bedeutet "Default" und wird hier
    /// als 0 nach außen sichtbar gemacht, damit das NumericUpDown-Binding nicht
    /// auf NaN aufläuft. UI-Konvention: 0 ⇒ Auto/Default.
    /// </summary>
    public double LineHeight
    {
        get => double.IsNaN(_model.LineHeight) ? 0 : _model.LineHeight;
        set
        {
            var stored = value <= 0 ? double.NaN : value;
            if (double.IsNaN(_model.LineHeight) && double.IsNaN(stored)) return;
            if (_model.LineHeight == stored) return;
            _model.LineHeight = stored;
            OnPropertyChanged();
            InvalidateWarpPreview();
        }
    }

    public double LetterSpacing
    {
        get => _model.LetterSpacing;
        set
        {
            if (_model.LetterSpacing == value) return;
            _model.LetterSpacing = value;
            OnPropertyChanged();
            InvalidateWarpPreview();
        }
    }

    /// <summary>
    /// Setzt alle 4 Eckpunkt-Offsets auf 0 zurück. Notausgang im Properties-
    /// Panel für den Fall, dass ein Punkt versehentlich so weit nach außen
    /// gezogen wurde, dass er nicht mehr per Pointer-Hit-Test erreichbar
    /// ist (z. B. außerhalb des ScrollViewer-Viewports).
    /// </summary>
    public void ResetCornerOffsets()
    {
        CornerNWdx = 0; CornerNWdy = 0;
        CornerNEdx = 0; CornerNEdy = 0;
        CornerSEdx = 0; CornerSEdy = 0;
        CornerSWdx = 0; CornerSWdy = 0;
    }

    /// <summary>
    /// Setzt den Drehpunkt zurück auf die Frame-Mitte (0.5, 0.5). Notausgang,
    /// falls der User den Drehpunkt versehentlich aus dem sichtbaren Bereich
    /// gezogen hat.
    /// </summary>
    public void ResetRotationOrigin()
    {
        RotationOriginRelX = 0.5;
        RotationOriginRelY = 0.5;
    }

    /// <summary>
    /// Wendet einen Strg+B-Toggle auf den Selektions-Bereich [start, start+length)
    /// im Modell-Text an. Aktualisiert die <see cref="TextField.BoldRanges"/>-
    /// Liste und triggert ein Re-Rendering der Vorschau-Bitmap.
    /// </summary>
    public void ToggleBoldOnSelection(int start, int length)
    {
        if (length <= 0) return;
        _model.BoldRanges = Models.BoldRangeMath.ToggleBold(_model.BoldRanges, start, length);
        InvalidateWarpPreview();
    }

    /// <summary>
    /// Schriftart als Avalonia-Typ für TextBox.FontFamily-Bindings.
    /// Compiled Bindings konvertieren string → FontFamily nicht zur Laufzeit, daher
    /// dieser explizite Wrapper. Fällt bei ungültigem oder leerem Namen auf den
    /// Default-FontFamily zurück, statt zu werfen.
    /// </summary>
    public Avalonia.Media.FontFamily FontFamilyValue
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(FontFamily) ? null : FontFamily;
            if (name is null) return Avalonia.Media.FontFamily.Default;
            try { return new Avalonia.Media.FontFamily(name); }
            catch { return Avalonia.Media.FontFamily.Default; }
        }
    }

    public double FontSize
    {
        get => _model.FontSize;
        set
        {
            if (_model.FontSize == value) return;
            _model.FontSize = value;
            OnPropertyChanged();
            InvalidateWarpPreview();
        }
    }

    public string FontWeight
    {
        get => _model.FontWeight;
        set
        {
            if (_model.FontWeight == value) return;
            _model.FontWeight = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FontWeightValue));
            InvalidateWarpPreview();
        }
    }

    /// <summary>Schriftschnitt als Avalonia-Enum für TextBlock/TextBox-Bindings.</summary>
    public FontWeight FontWeightValue =>
        Enum.TryParse<FontWeight>(FontWeight, ignoreCase: true, out var w)
            ? w
            : Avalonia.Media.FontWeight.Normal;

    public string Color
    {
        get => _model.Color;
        set
        {
            if (_model.Color == value) return;
            _model.Color = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ColorValue));
            InvalidateWarpPreview();
        }
    }

    /// <summary>Farbe als Avalonia.Media.Color für den ColorPicker. TwoWay-tauglich.</summary>
    public Color ColorValue
    {
        get => Avalonia.Media.Color.TryParse(string.IsNullOrEmpty(Color) ? "#000000" : Color, out var c)
            ? c
            : Colors.Black;
        set
        {
            // Avalonia.Media.Color.ToString() liefert bereits "#aarrggbb" — geeignet für Color.Parse.
            var hex = value.ToString();
            if (Color == hex) return;
            Color = hex;
        }
    }

    public string CurrentText
    {
        get => _model.CurrentText;
        set
        {
            if (_model.CurrentText == value) return;
            _model.CurrentText = value;
            OnPropertyChanged();
            InvalidateWarpPreview();
        }
    }

    public string HorizontalTextAlignment
    {
        get => _model.HorizontalTextAlignment;
        set
        {
            if (_model.HorizontalTextAlignment == value) return;
            _model.HorizontalTextAlignment = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HorizontalAlignmentOption));
            OnPropertyChanged(nameof(TextAlignmentValue));
            InvalidateWarpPreview();
        }
    }

    public string VerticalTextAlignment
    {
        get => _model.VerticalTextAlignment;
        set
        {
            if (_model.VerticalTextAlignment == value) return;
            _model.VerticalTextAlignment = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VerticalAlignmentOption));
            OnPropertyChanged(nameof(VerticalContentAlignmentValue));
            InvalidateWarpPreview();
        }
    }

    /// <summary>Avalonia-Enum für TextBox.TextAlignment.</summary>
    public TextAlignment TextAlignmentValue => HorizontalTextAlignment switch
    {
        "Center" => TextAlignment.Center,
        "Right" => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    /// <summary>Avalonia-Enum für TextBox.VerticalContentAlignment.</summary>
    public Avalonia.Layout.VerticalAlignment VerticalContentAlignmentValue => VerticalTextAlignment switch
    {
        "Center" => Avalonia.Layout.VerticalAlignment.Center,
        "Bottom" => Avalonia.Layout.VerticalAlignment.Bottom,
        _ => Avalonia.Layout.VerticalAlignment.Top,
    };

    /// <summary>SelectedItem-fähiges Pendant zu HorizontalTextAlignment für den ComboBox-Bind.</summary>
    public AlignmentOption HorizontalAlignmentOption
    {
        get => FontResources.HorizontalAlignmentOptions
                   .FirstOrDefault(o => o.Value == HorizontalTextAlignment)
               ?? FontResources.HorizontalAlignmentOptions[0];
        set
        {
            if (value is null) return;
            HorizontalTextAlignment = value.Value;
        }
    }

    public AlignmentOption VerticalAlignmentOption
    {
        get => FontResources.VerticalAlignmentOptions
                   .FirstOrDefault(o => o.Value == VerticalTextAlignment)
               ?? FontResources.VerticalAlignmentOptions[0];
        set
        {
            if (value is null) return;
            VerticalTextAlignment = value.Value;
        }
    }

    /// <summary>
    /// Ein-Klick-Toggle-Buttons für H/V-Alignment im Properties-Panel: drei
    /// boolesche Properties pro Achse, von denen genau eine true ist (= das
    /// gewählte Alignment). Setzen auf false → Default ("Left" bzw. "Top"),
    /// damit immer eine Auswahl aktiv bleibt; setzen auf true setzt das
    /// jeweilige Alignment. Property-Notifications werden für die anderen
    /// zwei mitgefeuert, damit ihre IsChecked-Bindings entsprechend
    /// umschalten.
    /// </summary>
    public bool IsAlignLeft
    {
        get => HorizontalTextAlignment == "Left";
        set { if (value) SetHorizontal("Left"); }
    }
    public bool IsAlignCenter
    {
        get => HorizontalTextAlignment == "Center";
        set { if (value) SetHorizontal("Center"); }
    }
    public bool IsAlignRight
    {
        get => HorizontalTextAlignment == "Right";
        set { if (value) SetHorizontal("Right"); }
    }
    public bool IsAlignTop
    {
        get => VerticalTextAlignment == "Top";
        set { if (value) SetVertical("Top"); }
    }
    public bool IsAlignMiddle
    {
        get => VerticalTextAlignment == "Center";
        set { if (value) SetVertical("Center"); }
    }
    public bool IsAlignBottom
    {
        get => VerticalTextAlignment == "Bottom";
        set { if (value) SetVertical("Bottom"); }
    }

    private void SetHorizontal(string v)
    {
        HorizontalTextAlignment = v;
        OnPropertyChanged(nameof(IsAlignLeft));
        OnPropertyChanged(nameof(IsAlignCenter));
        OnPropertyChanged(nameof(IsAlignRight));
    }
    private void SetVertical(string v)
    {
        VerticalTextAlignment = v;
        OnPropertyChanged(nameof(IsAlignTop));
        OnPropertyChanged(nameof(IsAlignMiddle));
        OnPropertyChanged(nameof(IsAlignBottom));
    }
}
