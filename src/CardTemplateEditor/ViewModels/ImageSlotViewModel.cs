using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CardTemplateEditor.ViewModels;

public partial class ImageSlotViewModel : ViewModelBase
{
    private readonly ImageSlot _model;
    private readonly Func<string, string?> _resolvePath;

    [ObservableProperty]
    private Bitmap? _bitmap;

    /// <summary>
    /// True, wenn dieser Slot der aktuelle "Drop-Target"-Slot des MainWindowViewModel
    /// ist. Dient nur der visuellen Hervorhebung (Highlight-Rahmen + Indikator,
    /// in welchen ImageSlot ein neues Textfeld eingefügt würde). Wird vom
    /// MainWindowViewModel synchron mit SelectedImageSlot gesetzt.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Multiplikator auf die Auto-Fit-Skalierung des Viewbox: 1.0 = "fit"
    /// (Default-Verhalten), 2.0 = doppelte Größe (Bild wird größer als der
    /// verfügbare Slot, ScrollViewer wird aktiv und der User kann scrollen,
    /// um Eckpunkte außerhalb des Sichtbereichs zu erreichen). Untergrenze
    /// = 1.0 (nicht kleiner als Auto-Fit, sonst verrutscht der Inhalt im
    /// LayoutTransformControl/Viewbox-Stack nach rechts), Obergrenze = 10.
    /// </summary>
    [ObservableProperty]
    private double _zoomFactor = 1.0;

    /// <summary>
    /// Anzeigetext für den Zoom-Indikator (z. B. "100 %"). Wird automatisch
    /// aktualisiert, wenn <see cref="ZoomFactor"/> sich ändert.
    /// </summary>
    public string ZoomDisplay => $"{Math.Round(ZoomFactor * 100)} %";

    partial void OnZoomFactorChanged(double value)
    {
        // [0.1, 10] als Sicherheits-Bounds. Werte unter 1 = unter Auto-Fit
        // (User kann das Bild kleiner als seinen Slot machen) — der Editor
        // zentriert den verkleinerten Inhalt via ScrollViewer.HorizontalContent
        // Alignment=Center, deshalb verrutscht hier nichts mehr.
        var clamped = Math.Max(0.1, Math.Min(10.0, value));
        if (Math.Abs(clamped - value) > 1e-9)
        {
            ZoomFactor = clamped;
            return;
        }
        OnPropertyChanged(nameof(ZoomDisplay));
    }

    public ImageSlotViewModel(ImageSlot model, Func<string, string?> resolvePath)
    {
        _model = model;
        _resolvePath = resolvePath;
        TextFields = new ObservableCollection<TextFieldViewModel>();
        ReloadBitmap();
    }

    public ImageSlot Model => _model;

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

    public string FileName
    {
        get => _model.FileName;
        set
        {
            if (_model.FileName == value) return;
            _model.FileName = value;
            OnPropertyChanged();
            ReloadBitmap();
        }
    }

    public ObservableCollection<TextFieldViewModel> TextFields { get; }

    /// <summary>
    /// Pixelbreite des geladenen Bitmaps. Wird im EditableImageCanvas als feste Breite
    /// des Layout-Containers verwendet, damit TextField-Koordinaten in Bildpixeln
    /// 1:1 in den ungezoomten Layout-Raum mappen.
    /// </summary>
    public double PixelWidth => Bitmap?.PixelSize.Width ?? 0;

    public double PixelHeight => Bitmap?.PixelSize.Height ?? 0;

    public bool HasBitmap => Bitmap is not null;

    partial void OnBitmapChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(PixelWidth));
        OnPropertyChanged(nameof(PixelHeight));
        OnPropertyChanged(nameof(HasBitmap));
    }

    public void ReloadBitmap()
    {
        var path = string.IsNullOrEmpty(_model.FileName)
            ? null
            : _resolvePath(_model.FileName);
        if (path is null || !File.Exists(path))
        {
            Bitmap = null;
            return;
        }
        var old = Bitmap;
        using (var stream = File.OpenRead(path))
        {
            Bitmap = new Bitmap(stream);
        }
        old?.Dispose();
    }
}
