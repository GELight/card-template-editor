using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;
using CardTemplateEditor.Views;
using CardTemplateEditor.Views.Controls;
using AvRectangle = Avalonia.Controls.Shapes.Rectangle;

namespace CardTemplateEditor.Tests;

/// <summary>
/// Iteration 14: EditMode wird aus den live gehaltenen Modifier-Tasten
/// abgeleitet (Strg = Distort, Alt = ScaleUniform, Shift+Alt = Skew). Die
/// Toolbar zeigt nur noch den Status; die Bool-Wrapper aus Iteration 13 sind
/// entfernt. Code-Behind synchronisiert Mode → Klassen-Liste der Handles
/// und IsSkewActive auf den TextField-VMs.
/// </summary>
public class EditModeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TemplateRepository _repo;

    public EditModeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EditMode_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _repo = new TemplateRepository(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void EditMode_DefaultsToScale()
    {
        var mw = new MainWindowViewModel(_repo);
        Assert.Equal(TextFieldEditMode.Scale, mw.EditMode);
        Assert.Equal("Skalieren", mw.EditModeDisplayName);
    }

    [Fact]
    public void EditModeDisplayName_ReflectsCurrentMode()
    {
        var mw = new MainWindowViewModel(_repo);

        mw.EditMode = TextFieldEditMode.Distort;
        Assert.Equal("Perspektive", mw.EditModeDisplayName);

        mw.EditMode = TextFieldEditMode.ScaleUniform;
        Assert.Equal("Skalieren (proportional)", mw.EditModeDisplayName);

        mw.EditMode = TextFieldEditMode.Skew;
        Assert.Equal("Schräg", mw.EditModeDisplayName);

        mw.EditMode = TextFieldEditMode.Rotate;
        Assert.Equal("Drehen", mw.EditModeDisplayName);
    }

    [Fact]
    public void EditModeIndicatorBrush_MatchesModeColor()
    {
        var mw = new MainWindowViewModel(_repo);

        Assert.Equal(Brushes.DodgerBlue, mw.EditModeIndicatorBrush);

        mw.EditMode = TextFieldEditMode.Distort;
        Assert.Equal(Brushes.Gold, mw.EditModeIndicatorBrush);

        mw.EditMode = TextFieldEditMode.Skew;
        Assert.Equal(Brushes.MediumSeaGreen, mw.EditModeIndicatorBrush);

        mw.EditMode = TextFieldEditMode.Rotate;
        Assert.Equal(Brushes.Orange, mw.EditModeIndicatorBrush);
    }

    [Fact]
    public void EditModeChanged_RaisesPropertyChangedForDisplayAndBrush()
    {
        var mw = new MainWindowViewModel(_repo);
        var changes = new List<string?>();
        mw.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        mw.EditMode = TextFieldEditMode.Distort;

        Assert.Contains(nameof(MainWindowViewModel.EditMode), changes);
        Assert.Contains(nameof(MainWindowViewModel.EditModeDisplayName), changes);
        Assert.Contains(nameof(MainWindowViewModel.EditModeIndicatorBrush), changes);
    }

    // --- Modifier-Resolution (pure, keine UI) --------------------------------

    [Fact]
    public void ResolveModeFromModifiers_NoModifier_ReturnsScale()
    {
        Assert.Equal(TextFieldEditMode.Scale,
            MainWindow.ResolveModeFromModifiers(KeyModifiers.None));
    }

    [Fact]
    public void ResolveModeFromModifiers_Ctrl_ReturnsDistort()
    {
        Assert.Equal(TextFieldEditMode.Distort,
            MainWindow.ResolveModeFromModifiers(KeyModifiers.Control));
    }

    [Fact]
    public void ResolveModeFromModifiers_AltAlone_ReturnsScaleUniform()
    {
        Assert.Equal(TextFieldEditMode.ScaleUniform,
            MainWindow.ResolveModeFromModifiers(KeyModifiers.Alt));
    }

    [Fact]
    public void ResolveModeFromModifiers_ShiftAlt_ReturnsSkew()
    {
        Assert.Equal(TextFieldEditMode.Skew,
            MainWindow.ResolveModeFromModifiers(KeyModifiers.Alt | KeyModifiers.Shift));
    }

    [Fact]
    public void ResolveModeFromModifiers_CtrlBeatsAlt()
    {
        // Strg gewinnt vor Alt — gleichzeitiger Ctrl+Alt-Druck ist
        // mehrdeutig, Distort ist die Photoshop-Konvention.
        Assert.Equal(TextFieldEditMode.Distort,
            MainWindow.ResolveModeFromModifiers(KeyModifiers.Control | KeyModifiers.Alt));
    }

    // --- UI-seitige Synchronisation ------------------------------------------

    [AvaloniaFact]
    public void HandleHover_SetsIsPointerOver_AndAppliesScaleTransform()
    {
        // Hover über ein Handle muss es sichtbar vergrößern, damit der User
        // das Handle leicht trifft. Style "Rectangle.handle:pointerover"
        // greift mit RenderTransform=scale(1.6).
        var owner = new MainWindowViewModel(_repo);
        var fieldVm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 });

        var frame = new TextFieldFrame { DataContext = fieldVm };
        var canvas = new Canvas { Width = 400, Height = 300, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);
        var window = new Window { Width = 400, Height = 300, Content = canvas, DataContext = owner };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var handleSE = frame.FindControl<AvRectangle>("HandleSE");
        Assert.NotNull(handleSE);
        Assert.False(handleSE!.IsPointerOver);

        Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new Point(232, 72));
        Dispatcher.UIThread.RunJobs();

        Assert.True(handleSE.IsPointerOver,
            "Pointer über SE-Handle muss IsPointerOver=true setzen.");
        Assert.NotNull(handleSE.RenderTransform);

        window.Close();
    }

    [AvaloniaFact]
    public void HandleClasses_ReflectActiveMode_AfterEditModeChange()
    {
        // Hover-Farbe folgt dem aktuellen Mode des Owners — das passiert
        // jetzt nicht mehr per User-Klick auf RadioButton, sondern weil das
        // MainWindow den EditMode aus den Modifier-Tasten ableitet.
        var owner = new MainWindowViewModel(_repo) { EditMode = TextFieldEditMode.Scale };
        var fieldVm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 });

        var frame = new TextFieldFrame { DataContext = fieldVm };
        var canvas = new Canvas { Width = 400, Height = 300, Children = { frame } };
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);
        var window = new Window { Width = 400, Height = 300, Content = canvas, DataContext = owner };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var handleSE = frame.FindControl<AvRectangle>("HandleSE");
        Assert.NotNull(handleSE);
        Assert.Contains("scale", handleSE!.Classes);

        owner.EditMode = TextFieldEditMode.Distort;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("scale", handleSE.Classes);
        Assert.Contains("distort", handleSE.Classes);

        owner.EditMode = TextFieldEditMode.Skew;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("distort", handleSE.Classes);
        Assert.Contains("skew", handleSE.Classes);

        window.Close();
    }

    [AvaloniaFact]
    public void OwnerEditMode_Skew_SetsIsSkewActiveOnTextFieldViewModel()
    {
        // Sub-Task C: bei Mode=Skew werden die Eck-Handles ausgeblendet —
        // der Code-Behind setzt vm.IsSkewActive = true, ShowCornerHandles
        // wird false, IsVisible-Bindings im XAML kümmern sich um den Rest.
        var owner = new MainWindowViewModel(_repo) { EditMode = TextFieldEditMode.Scale };
        var fieldVm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 });

        var frame = new TextFieldFrame { DataContext = fieldVm };
        var canvas = new Canvas { Width = 400, Height = 300, Children = { frame } };
        var window = new Window { Width = 400, Height = 300, Content = canvas, DataContext = owner };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(fieldVm.IsSkewActive);

        owner.EditMode = TextFieldEditMode.Skew;
        Dispatcher.UIThread.RunJobs();
        Assert.True(fieldVm.IsSkewActive);

        var handleNW = frame.FindControl<AvRectangle>("HandleNW");
        Assert.NotNull(handleNW);
        Assert.False(handleNW!.IsVisible,
            "NW-Eck-Handle muss im Skew-Modus unsichtbar sein.");

        owner.EditMode = TextFieldEditMode.Scale;
        Dispatcher.UIThread.RunJobs();
        Assert.False(fieldVm.IsSkewActive);
        Assert.True(handleNW.IsVisible);

        window.Close();
    }
}
