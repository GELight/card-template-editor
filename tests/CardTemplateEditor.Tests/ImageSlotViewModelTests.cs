using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CardTemplateEditor.Models;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

public class ImageSlotViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public ImageSlotViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ImgSlotVmTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ZoomFactor_DefaultsToOne_ClampsToValidRange_AndUpdatesDisplay()
    {
        var vm = new ImageSlotViewModel(new ImageSlot(), _ => null);
        Assert.Equal(1.0, vm.ZoomFactor);
        Assert.Equal("100 %", vm.ZoomDisplay);

        vm.ZoomFactor = 2.5;
        Assert.Equal(2.5, vm.ZoomFactor);
        Assert.Equal("250 %", vm.ZoomDisplay);

        // Maximum bei 10x.
        vm.ZoomFactor = 50;
        Assert.Equal(10.0, vm.ZoomFactor);
    }

    [Fact]
    public void ZoomFactor_AllowsBelowOne_ForZoomOutBeyondAutoFit()
    {
        // User-Wunsch: auch unter Auto-Fit zoomen können (= Bild kleiner als
        // sein natürlicher Anzeige-Bereich). Untergrenze nur als Schutz vor
        // entarteten Skalierungen (0.1 = 10% der Auto-Fit-Größe).
        var vm = new ImageSlotViewModel(new ImageSlot(), _ => null);

        vm.ZoomFactor = 0.5;
        Assert.Equal(0.5, vm.ZoomFactor);

        vm.ZoomFactor = 0.25;
        Assert.Equal(0.25, vm.ZoomFactor);

        // Unter dem Floor (0.1) → geclamped.
        vm.ZoomFactor = 0.001;
        Assert.Equal(0.1, vm.ZoomFactor);

        // Über 10x → geclamped.
        vm.ZoomFactor = 100;
        Assert.Equal(10.0, vm.ZoomFactor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestPng(string name)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(2, 2), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 2, 2));
        }
        bmp.Save(path);
        return path;
    }

    private string? Resolve(string fileName) => Path.Combine(_tempDir, fileName);

    [AvaloniaFact]
    public void Constructor_LoadsBitmap_WhenFileExists()
    {
        CreateTestPng("a.png");
        var slot = new ImageSlot { FileName = "a.png" };

        var vm = new ImageSlotViewModel(slot, Resolve);

        Assert.NotNull(vm.Bitmap);
    }

    [AvaloniaFact]
    public void Constructor_BitmapIsNull_WhenFileMissing()
    {
        var slot = new ImageSlot { FileName = "missing.png" };

        var vm = new ImageSlotViewModel(slot, Resolve);

        Assert.Null(vm.Bitmap);
    }

    [AvaloniaFact]
    public void Constructor_BitmapIsNull_WhenFileNameEmpty()
    {
        var slot = new ImageSlot { FileName = "" };

        var vm = new ImageSlotViewModel(slot, Resolve);

        Assert.Null(vm.Bitmap);
    }

    [AvaloniaFact]
    public void FileNameChange_LoadsBitmap_AndRaisesPropertyChanged()
    {
        CreateTestPng("a.png");
        var slot = new ImageSlot { FileName = "" };
        var vm = new ImageSlotViewModel(slot, Resolve);
        Assert.Null(vm.Bitmap);

        var bitmapChanges = 0;
        var fileNameChanges = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ImageSlotViewModel.Bitmap)) bitmapChanges++;
            if (e.PropertyName == nameof(ImageSlotViewModel.FileName)) fileNameChanges++;
        };

        vm.FileName = "a.png";

        Assert.NotNull(vm.Bitmap);
        Assert.Equal(1, fileNameChanges);
        Assert.Equal(1, bitmapChanges);
    }

    [AvaloniaFact]
    public void FileNameChange_SetsBitmapNull_WhenNewFileMissing()
    {
        CreateTestPng("a.png");
        var slot = new ImageSlot { FileName = "a.png" };
        var vm = new ImageSlotViewModel(slot, Resolve);
        Assert.NotNull(vm.Bitmap);

        vm.FileName = "missing.png";

        Assert.Null(vm.Bitmap);
    }
}
