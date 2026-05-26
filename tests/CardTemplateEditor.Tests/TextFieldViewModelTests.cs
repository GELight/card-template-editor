using CardTemplateEditor.Models;
using CardTemplateEditor.ViewModels;

namespace CardTemplateEditor.Tests;

public class TextFieldViewModelTests
{
    [Fact]
    public void Properties_AreReadFromModel_OnConstruction()
    {
        var slotId = Guid.NewGuid();
        var model = new TextField
        {
            ImageSlotId = slotId,
            Name = "titel",
            X = 10, Y = 20, Width = 100, Height = 40,
            FontFamily = "Arial",
            FontSize = 24,
            FontWeight = "Bold",
            Color = "#FF0000",
            CurrentText = "Hallo",
        };

        var vm = new TextFieldViewModel(model);

        Assert.Equal(slotId, vm.ImageSlotId);
        Assert.Equal("titel", vm.Name);
        Assert.Equal(10, vm.X);
        Assert.Equal(20, vm.Y);
        Assert.Equal(100, vm.Width);
        Assert.Equal(40, vm.Height);
        Assert.Equal("Arial", vm.FontFamily);
        Assert.Equal(24, vm.FontSize);
        Assert.Equal("Bold", vm.FontWeight);
        Assert.Equal("#FF0000", vm.Color);
        Assert.Equal("Hallo", vm.CurrentText);
    }

    [Fact]
    public void SettingProperty_PropagatesToModel_AndRaisesPropertyChanged()
    {
        var model = new TextField();
        var vm = new TextFieldViewModel(model);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.Name = "neu";
        vm.X = 5;
        vm.Y = 7;
        vm.Width = 250;
        vm.Height = 80;
        vm.FontFamily = "Verdana";
        vm.FontSize = 32;
        vm.FontWeight = "Bold";
        vm.Color = "#00FF00";
        vm.CurrentText = "Text";

        Assert.Equal("neu", model.Name);
        Assert.Equal(5, model.X);
        Assert.Equal(7, model.Y);
        Assert.Equal(250, model.Width);
        Assert.Equal(80, model.Height);
        Assert.Equal("Verdana", model.FontFamily);
        Assert.Equal(32, model.FontSize);
        Assert.Equal("Bold", model.FontWeight);
        Assert.Equal("#00FF00", model.Color);
        Assert.Equal("Text", model.CurrentText);

        Assert.Contains(nameof(TextFieldViewModel.Name), changes);
        Assert.Contains(nameof(TextFieldViewModel.X), changes);
        Assert.Contains(nameof(TextFieldViewModel.Y), changes);
        Assert.Contains(nameof(TextFieldViewModel.Width), changes);
        Assert.Contains(nameof(TextFieldViewModel.Height), changes);
        Assert.Contains(nameof(TextFieldViewModel.FontFamily), changes);
        Assert.Contains(nameof(TextFieldViewModel.FontSize), changes);
        Assert.Contains(nameof(TextFieldViewModel.FontWeight), changes);
        Assert.Contains(nameof(TextFieldViewModel.Color), changes);
        Assert.Contains(nameof(TextFieldViewModel.CurrentText), changes);
    }

    [Fact]
    public void SettingSameValue_DoesNotRaisePropertyChanged()
    {
        var model = new TextField { Name = "x", X = 10 };
        var vm = new TextFieldViewModel(model);

        var changes = 0;
        vm.PropertyChanged += (_, _) => changes++;

        vm.Name = "x";
        vm.X = 10;

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Id_IsExposed_FromModel()
    {
        var id = Guid.NewGuid();
        var model = new TextField { Id = id };
        var vm = new TextFieldViewModel(model);

        Assert.Equal(id, vm.Id);
    }

    [Fact]
    public void Alignments_DefaultToLeftTop_AndRoundtripThroughModel()
    {
        var model = new TextField();
        var vm = new TextFieldViewModel(model);

        Assert.Equal("Left", vm.HorizontalTextAlignment);
        Assert.Equal("Top", vm.VerticalTextAlignment);

        vm.HorizontalTextAlignment = "Right";
        vm.VerticalTextAlignment = "Bottom";

        Assert.Equal("Right", model.HorizontalTextAlignment);
        Assert.Equal("Bottom", model.VerticalTextAlignment);
        Assert.Equal(Avalonia.Media.TextAlignment.Right, vm.TextAlignmentValue);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Bottom, vm.VerticalContentAlignmentValue);
    }

    [Fact]
    public void AlignmentOption_ReflectsStringValue_AndRaisesNotifications()
    {
        var vm = new TextFieldViewModel(new TextField());
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.HorizontalAlignmentOption = FontResources.HorizontalAlignmentOptions
            .Single(o => o.Value == "Center");

        Assert.Equal("Center", vm.HorizontalTextAlignment);
        Assert.Same(FontResources.HorizontalAlignmentOptions.Single(o => o.Value == "Center"),
            vm.HorizontalAlignmentOption);
        Assert.Contains(nameof(TextFieldViewModel.HorizontalTextAlignment), changes);
        Assert.Contains(nameof(TextFieldViewModel.HorizontalAlignmentOption), changes);
    }

    [Fact]
    public void ColorValue_ParsesAndSerializesHex()
    {
        var vm = new TextFieldViewModel(new TextField { Color = "#FF112233" });

        Assert.Equal(Avalonia.Media.Color.FromArgb(0xFF, 0x11, 0x22, 0x33), vm.ColorValue);

        vm.ColorValue = Avalonia.Media.Color.FromArgb(0xFF, 0xAB, 0xCD, 0xEF);
        // Color.ToString() liefert "#FFAABBCC" — case-insensitive vergleichen.
        Assert.Equal("#FFABCDEF", vm.Color, ignoreCase: true);
    }

    [Fact]
    public void FontWeightValue_ParsesEnum()
    {
        var vm = new TextFieldViewModel(new TextField { FontWeight = "Bold" });
        Assert.Equal(Avalonia.Media.FontWeight.Bold, vm.FontWeightValue);

        vm.FontWeight = "Light";
        Assert.Equal(Avalonia.Media.FontWeight.Light, vm.FontWeightValue);
    }

    [Fact]
    public void Rotation_DefaultsToZero_AndRoundtripsThroughModel()
    {
        var model = new TextField();
        var vm = new TextFieldViewModel(model);

        Assert.Equal(0, vm.Rotation);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.Rotation = 45;
        Assert.Equal(45, vm.Rotation);
        Assert.Equal(45, model.Rotation);
        Assert.Contains(nameof(TextFieldViewModel.Rotation), changes);
    }

    [Fact]
    public void Rotation_NormalizesTo_MinusPi_To_Pi_Range()
    {
        var vm = new TextFieldViewModel(new TextField());

        vm.Rotation = 270;
        // 270 → -90 (kürzester Weg).
        Assert.Equal(-90, vm.Rotation);

        vm.Rotation = -200;
        // -200 → 160.
        Assert.Equal(160, vm.Rotation);

        vm.Rotation = 360;
        // 360 → 0.
        Assert.Equal(0, vm.Rotation);
    }

    [Fact]
    public void Rotation_NaNOrInfinity_NormalizesToZero()
    {
        var vm = new TextFieldViewModel(new TextField { Rotation = 30 });

        vm.Rotation = double.NaN;
        Assert.Equal(0, vm.Rotation);

        vm.Rotation = double.PositiveInfinity;
        Assert.Equal(0, vm.Rotation);
    }

    [Fact]
    public void OuterRect_ReflectsXywhPlusOuterPadding()
    {
        var vm = new TextFieldViewModel(new TextField
        {
            X = 100, Y = 200, Width = 80, Height = 40,
        });

        var pad = Models.TextFieldGeometry.OuterPadding;
        Assert.Equal(100 - pad, vm.OuterX);
        Assert.Equal(200 - pad, vm.OuterY);
        Assert.Equal(80 + 2 * pad, vm.OuterWidth);
        Assert.Equal(40 + 2 * pad, vm.OuterHeight);
    }

    [Fact]
    public void OuterRect_FollowsXywh_PropertyChanges_AndNotifies()
    {
        var vm = new TextFieldViewModel(new TextField
        {
            X = 0, Y = 0, Width = 50, Height = 50,
        });

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.X = 10;
        vm.Y = 20;
        vm.Width = 100;
        vm.Height = 60;

        var pad = Models.TextFieldGeometry.OuterPadding;
        Assert.Equal(10 - pad, vm.OuterX);
        Assert.Equal(20 - pad, vm.OuterY);
        Assert.Equal(100 + 2 * pad, vm.OuterWidth);
        Assert.Equal(60 + 2 * pad, vm.OuterHeight);
        Assert.Contains(nameof(TextFieldViewModel.OuterX), changes);
        Assert.Contains(nameof(TextFieldViewModel.OuterY), changes);
        Assert.Contains(nameof(TextFieldViewModel.OuterWidth), changes);
        Assert.Contains(nameof(TextFieldViewModel.OuterHeight), changes);
    }

    [Fact]
    public void IsWarped_FlipsWhenAnyCornerOffsetIsNonZero()
    {
        var vm = new TextFieldViewModel(new TextField());
        Assert.False(vm.IsWarped);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.CornerSEdx = 5;
        Assert.True(vm.IsWarped);
        Assert.Contains(nameof(TextFieldViewModel.IsWarped), changes);

        vm.CornerSEdx = 0;
        Assert.False(vm.IsWarped);

        vm.CornerNWdy = -3;
        Assert.True(vm.IsWarped);
    }

    [Fact]
    public void FontFamilyValue_TracksFontFamilyString_AndRaisesNotification()
    {
        var vm = new TextFieldViewModel(new TextField { FontFamily = "Arial" });
        Assert.Equal("Arial", vm.FontFamilyValue.Name);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.FontFamily = "Verdana";

        Assert.Equal("Verdana", vm.FontFamilyValue.Name);
        Assert.Contains(nameof(TextFieldViewModel.FontFamily), changes);
        Assert.Contains(nameof(TextFieldViewModel.FontFamilyValue), changes);
    }

    [Fact]
    public void StretchAndSpacing_DefaultValues_RoundtripThroughModel()
    {
        var vm = new TextFieldViewModel(new TextField());
        Assert.Equal(1.0, vm.StretchX);
        Assert.Equal(1.0, vm.StretchY);
        Assert.False(vm.AutoFit);
        // Default-LineHeight im Modell ist NaN; UI-Sicht ist 0 ("Auto").
        Assert.Equal(0.0, vm.LineHeight);
        Assert.Equal(0.0, vm.LetterSpacing);

        vm.StretchX = 2.0;
        vm.StretchY = 0.5;
        vm.AutoFit = true;
        vm.LineHeight = 24;
        vm.LetterSpacing = 3;

        Assert.Equal(2.0, vm.Model.StretchX);
        Assert.Equal(0.5, vm.Model.StretchY);
        Assert.True(vm.Model.AutoFit);
        Assert.Equal(24, vm.Model.LineHeight);
        Assert.Equal(3, vm.Model.LetterSpacing);

        // 0 oder negativ ⇒ NaN im Modell (= "Auto-Default").
        vm.LineHeight = 0;
        Assert.True(double.IsNaN(vm.Model.LineHeight));
        Assert.Equal(0.0, vm.LineHeight);
    }

    [Fact]
    public void EditableTextOpacity_FollowsTextPresenceAndEditMode()
    {
        // Plain Field ohne Text → TextBox sichtbar (User soll tippen können).
        var vm = new TextFieldViewModel(new TextField { CurrentText = "" });
        Assert.Equal(1.0, vm.EditableTextOpacity);
        Assert.False(vm.ShouldShowPreview);

        // Text dazu → Preview übernimmt, TextBox unsichtbar.
        vm.CurrentText = "Hallo";
        Assert.Equal(0.0, vm.EditableTextOpacity);
        Assert.True(vm.ShouldShowPreview);

        // Edit-Mode aktiviert → TextBox wieder sichtbar (Cursor!), Preview aus.
        vm.IsInEditMode = true;
        Assert.Equal(1.0, vm.EditableTextOpacity);
        Assert.False(vm.ShouldShowPreview);

        // Edit-Mode beendet → zurück zu Preview-Anzeige.
        vm.IsInEditMode = false;
        Assert.Equal(0.0, vm.EditableTextOpacity);
        Assert.True(vm.ShouldShowPreview);
    }

    [Fact]
    public void ToggleBoldOnSelection_AddsAndRemovesBoldRange_AndInvalidatesPreview()
    {
        var model = new TextField { CurrentText = "Hello World" };
        var vm = new TextFieldViewModel(model);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.ToggleBoldOnSelection(start: 0, length: 5); // "Hello"
        Assert.Single(model.BoldRanges);
        Assert.Equal(0, model.BoldRanges[0].Start);
        Assert.Equal(5, model.BoldRanges[0].Length);
        Assert.Contains(nameof(TextFieldViewModel.WarpPreviewBitmap), changes);

        // Toggle erneut über denselben Bereich → entfernt.
        vm.ToggleBoldOnSelection(start: 0, length: 5);
        Assert.Empty(model.BoldRanges);
    }

    [Fact]
    public void ToggleBoldOnSelection_LengthZero_NoOp()
    {
        var vm = new TextFieldViewModel(new TextField { CurrentText = "Hi" });
        vm.ToggleBoldOnSelection(0, 0);
        Assert.Empty(vm.Model.BoldRanges);
    }

    [Fact]
    public void IsInEditMode_TogglesAndRaisesNotifications()
    {
        var vm = new TextFieldViewModel(new TextField { CurrentText = "x" });
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.IsInEditMode = true;
        Assert.True(vm.IsInEditMode);
        Assert.Contains(nameof(TextFieldViewModel.IsInEditMode), changes);
        Assert.Contains(nameof(TextFieldViewModel.ShouldShowPreview), changes);
        Assert.Contains(nameof(TextFieldViewModel.EditableTextOpacity), changes);

        // Idempotent: gleicher Wert → keine erneute Notification.
        changes.Clear();
        vm.IsInEditMode = true;
        Assert.Empty(changes);
    }

    [Fact]
    public void WarpPreviewBitmap_IsNull_WhenTextEmpty()
    {
        // Contract umgedreht: das Bitmap wird IMMER gerendert sobald Text da
        // ist (für Editor-↔-Export-Konsistenz). Nur leerer Text → null.
        var vm = new TextFieldViewModel(new TextField
        {
            Width = 100, Height = 40, CurrentText = "",
        });
        Assert.Null(vm.WarpPreviewBitmap);
    }

    [Fact]
    public void InvalidatingProperties_RaiseWarpPreviewBitmap_AndEditableTextOpacity()
    {
        // Sanity: alle Setter, die das Vorschau-Rendering beeinflussen, müssen
        // PropertyChanged für WarpPreviewBitmap auslösen (der View-Layer bindet
        // ein Image darauf, ohne diese Notifications gibt's keinen Refresh).
        var vm = new TextFieldViewModel(new TextField
        {
            Width = 100, Height = 40,
            FontFamily = "Arial", FontSize = 18,
            FontWeight = "Normal", Color = "#000000",
            CurrentText = "Hi",
            HorizontalTextAlignment = "Left",
            VerticalTextAlignment = "Top",
        });

        var actions = new (string Name, Action<TextFieldViewModel> Apply)[]
        {
            ("Width",                   v => v.Width = 150),
            ("Height",                  v => v.Height = 60),
            ("FontFamily",              v => v.FontFamily = "Verdana"),
            ("FontSize",                v => v.FontSize = 24),
            ("FontWeight",              v => v.FontWeight = "Bold"),
            ("Color",                   v => v.Color = "#FF0000"),
            ("CurrentText",             v => v.CurrentText = "Hallo Welt"),
            ("HorizontalTextAlignment", v => v.HorizontalTextAlignment = "Center"),
            ("VerticalTextAlignment",   v => v.VerticalTextAlignment = "Bottom"),
            ("StretchX",                v => v.StretchX = 1.5),
            ("StretchY",                v => v.StretchY = 0.7),
            ("AutoFit",                 v => v.AutoFit = true),
            ("LineHeight",              v => v.LineHeight = 32),
            ("LetterSpacing",           v => v.LetterSpacing = 4),
            ("CornerNWdx",              v => v.CornerNWdx = 4),
            ("CornerNWdy",              v => v.CornerNWdy = 4),
            ("CornerNEdx",              v => v.CornerNEdx = 5),
            ("CornerNEdy",              v => v.CornerNEdy = 5),
            ("CornerSEdx",              v => v.CornerSEdx = 6),
            ("CornerSEdy",              v => v.CornerSEdy = 6),
            ("CornerSWdx",              v => v.CornerSWdx = 7),
            ("CornerSWdy",              v => v.CornerSWdy = 7),
        };

        foreach (var (name, apply) in actions)
        {
            var changes = new List<string?>();
            void Handler(object? _, System.ComponentModel.PropertyChangedEventArgs e) => changes.Add(e.PropertyName);
            vm.PropertyChanged += Handler;
            apply(vm);
            vm.PropertyChanged -= Handler;
            Assert.True(changes.Contains(nameof(TextFieldViewModel.WarpPreviewBitmap)),
                $"Setter '{name}' hat PropertyChanged für WarpPreviewBitmap nicht ausgelöst.");
            Assert.True(changes.Contains(nameof(TextFieldViewModel.EditableTextOpacity)),
                $"Setter '{name}' hat PropertyChanged für EditableTextOpacity nicht ausgelöst.");
        }
    }

    /// <summary>
    /// Die IsAlign{Left|Center|Right}- und IsAlign{Top|Middle|Bottom}-Properties
    /// sind ein-Klick-Toggle-Bindings für die kompakten Icon-Buttons im
    /// Properties-Panel: ein "true"-Setter setzt das jeweilige Alignment;
    /// die anderen zwei Properties auf der gleichen Achse werden automatisch
    /// false. Setter mit "false" ist no-op (würde sonst eine "keine Auswahl"-
    /// Lücke erlauben), weil ToggleButton beim erneuten Klick auf den
    /// aktiven Button auf false togglen würde.
    /// </summary>
    [Fact]
    public void IsAlignSetters_SetUnderlyingAlignment_AndFireNotificationsForSiblings()
    {
        var vm = new TextFieldViewModel(new TextField
        {
            HorizontalTextAlignment = "Left",
            VerticalTextAlignment = "Top",
        });

        Assert.True(vm.IsAlignLeft);
        Assert.False(vm.IsAlignCenter);
        Assert.False(vm.IsAlignRight);
        Assert.True(vm.IsAlignTop);
        Assert.False(vm.IsAlignMiddle);
        Assert.False(vm.IsAlignBottom);

        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        // Klick auf "Center"-Icon: nur diese eine Property bekommt true,
        // PropertyChanged feuert für ALLE drei H-Alignment-Bool-Properties
        // damit die anderen IsChecked-Bindings ihren Visual-State aktualisieren.
        vm.IsAlignCenter = true;
        Assert.Equal("Center", vm.HorizontalTextAlignment);
        Assert.False(vm.IsAlignLeft);
        Assert.True(vm.IsAlignCenter);
        Assert.False(vm.IsAlignRight);
        Assert.Contains(nameof(TextFieldViewModel.IsAlignLeft), changes);
        Assert.Contains(nameof(TextFieldViewModel.IsAlignCenter), changes);
        Assert.Contains(nameof(TextFieldViewModel.IsAlignRight), changes);

        // Vertikal-Achse analog.
        changes.Clear();
        vm.IsAlignBottom = true;
        Assert.Equal("Bottom", vm.VerticalTextAlignment);
        Assert.False(vm.IsAlignTop);
        Assert.False(vm.IsAlignMiddle);
        Assert.True(vm.IsAlignBottom);
        Assert.Contains(nameof(TextFieldViewModel.IsAlignTop), changes);
        Assert.Contains(nameof(TextFieldViewModel.IsAlignMiddle), changes);
        Assert.Contains(nameof(TextFieldViewModel.IsAlignBottom), changes);

        // false-Setter ist no-op: ToggleButton kann durch erneuten Klick
        // auf den aktiven Button false setzen wollen, das soll aber nicht
        // alle Bool-Properties auf false bringen (sonst gäbe es einen
        // "keine Achsen-Auswahl"-Zustand, der weder UI-konsistent noch
        // semantisch sinnvoll ist).
        vm.IsAlignBottom = false;
        Assert.Equal("Bottom", vm.VerticalTextAlignment);
        Assert.True(vm.IsAlignBottom);
    }

    // --- EffectiveScale: Borders + Handles in Screen-Pixeln konstant ---------

    [Fact]
    public void EffectiveScale_DefaultsToOne_AndYieldsBaseSizes()
    {
        var vm = new TextFieldViewModel(new TextField { X = 0, Y = 0, Width = 200, Height = 30 });

        Assert.Equal(1.0, vm.EffectiveScale);
        Assert.Equal(TextFieldViewModel.HandleSize, vm.EffectiveHandleSize);
        Assert.Equal(TextFieldViewModel.HandleHalfSize, vm.EffectiveHandleHalfSize);
        Assert.Equal(TextFieldViewModel.RotateHandleSize, vm.EffectiveRotateHandleSize);
        Assert.Equal(TextFieldViewModel.BaseBorderThickness, vm.EffectiveBorderThickness);
    }

    [Fact]
    public void EffectiveScale_HalfScale_DoublesEffectiveSizes()
    {
        // Auto-Fit eines hochauflösenden Bilds (z. B. 0.5x) muss die Handle-
        // und Border-Größen in Bild-Pixel-Coords verdoppeln, sodass sie nach
        // dem Skalieren wieder bei 1 px / 8 px auf dem Screen landen.
        var vm = new TextFieldViewModel(new TextField { X = 0, Y = 0, Width = 200, Height = 30 });

        vm.EffectiveScale = 0.5;

        Assert.Equal(16, vm.EffectiveHandleSize);
        Assert.Equal(8, vm.EffectiveHandleHalfSize);
        Assert.Equal(28, vm.EffectiveRotateHandleSize);
        Assert.Equal(2, vm.EffectiveBorderThickness);
    }

    [Fact]
    public void EffectiveScale_NonPositive_FallsBackToOne()
    {
        // Defensive Untergrenze: ein versehentliches Setzen auf 0 darf
        // keinen NaN/∞-Layout-Crash auslösen.
        var vm = new TextFieldViewModel(new TextField());

        vm.EffectiveScale = 0;
        Assert.Equal(1.0, vm.EffectiveScale);

        vm.EffectiveScale = -2;
        Assert.Equal(1.0, vm.EffectiveScale);
    }

    [Fact]
    public void EffectiveScale_Set_RefreshesHandlePositions()
    {
        // Handle-Positionen hängen über EffectiveHandleHalfSize von der
        // Skalierung ab — bei Scale=0.5 verdoppelt sich der Half-Size, also
        // verschiebt sich HandleNWX entsprechend nach links.
        var vm = new TextFieldViewModel(new TextField { X = 100, Y = 50, Width = 200, Height = 40 });

        // Default: HandleNWX = OuterPadding + 0 - 4 = 32 - 4 = 28.
        var defaultNWX = vm.HandleNWX;

        vm.EffectiveScale = 0.5;

        // Scale=0.5: half-size = 8, also HandleNWX = 32 - 8 = 24.
        Assert.Equal(defaultNWX - 4, vm.HandleNWX);
    }

    [Fact]
    public void EffectiveScale_Set_RaisesPropertyChangedForDerivedSizes()
    {
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 });
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.EffectiveScale = 0.5;

        Assert.Contains(nameof(TextFieldViewModel.EffectiveScale), changes);
        Assert.Contains(nameof(TextFieldViewModel.EffectiveHandleSize), changes);
        Assert.Contains(nameof(TextFieldViewModel.EffectiveHandleHalfSize), changes);
        Assert.Contains(nameof(TextFieldViewModel.EffectiveBorderThickness), changes);
        Assert.Contains(nameof(TextFieldViewModel.HandleNWX), changes);
        Assert.Contains(nameof(TextFieldViewModel.HandleSEY), changes);
    }

    // --- IsSkewActive: blendet Eck-Handles aus -------------------------------

    [Fact]
    public void IsSkewActive_DefaultsFalse_ShowsCornerHandles()
    {
        // Voraussetzung ist Selektion — sonst sind Eck-Handles ohnehin still.
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 })
        {
            IsSelected = true,
        };
        Assert.False(vm.IsSkewActive);
        Assert.True(vm.ShowCornerHandles);
    }

    [Fact]
    public void IsSkewActive_True_HidesCornerHandles_AndRaisesPropertyChanged()
    {
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 })
        {
            IsSelected = true,
        };
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.IsSkewActive = true;

        Assert.True(vm.IsSkewActive);
        Assert.False(vm.ShowCornerHandles);
        Assert.Contains(nameof(TextFieldViewModel.IsSkewActive), changes);
        Assert.Contains(nameof(TextFieldViewModel.ShowCornerHandles), changes);
    }

    // --- RotationOrigin (Sub-Task D) -----------------------------------------

    [Fact]
    public void RotationOrigin_Defaults_ToCenter()
    {
        var vm = new TextFieldViewModel(new TextField { X = 100, Y = 200, Width = 80, Height = 60 });

        Assert.Equal(0.5, vm.RotationOriginRelX);
        Assert.Equal(0.5, vm.RotationOriginRelY);
        Assert.Equal(140, vm.RotationOriginAbsolute.X);
        Assert.Equal(230, vm.RotationOriginAbsolute.Y);
    }

    [Fact]
    public void RotationOriginAbsolute_FollowsRelXY_AndFrameSize()
    {
        var vm = new TextFieldViewModel(new TextField { X = 0, Y = 0, Width = 100, Height = 50 });

        vm.RotationOriginRelX = 0; // links
        vm.RotationOriginRelY = 0; // oben
        Assert.Equal(0, vm.RotationOriginAbsolute.X);
        Assert.Equal(0, vm.RotationOriginAbsolute.Y);

        vm.RotationOriginRelX = 1; // rechts
        vm.RotationOriginRelY = 1; // unten
        Assert.Equal(100, vm.RotationOriginAbsolute.X);
        Assert.Equal(50, vm.RotationOriginAbsolute.Y);
    }

    [Fact]
    public void RotationOrigin_Setter_RaisesPropertyChangedForDerived()
    {
        var vm = new TextFieldViewModel(new TextField { Width = 100, Height = 50 });
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.RotationOriginRelX = 0.25;

        Assert.Contains(nameof(TextFieldViewModel.RotationOriginRelX), changes);
        Assert.Contains(nameof(TextFieldViewModel.RotationOriginAbsolute), changes);
        Assert.Contains(nameof(TextFieldViewModel.RotationOriginPoint), changes);
        Assert.Contains(nameof(TextFieldViewModel.RotationOriginCanvasX), changes);
        Assert.Contains(nameof(TextFieldViewModel.RotationOriginRelative), changes);
    }

    [Fact]
    public void ResetCornerOffsets_SetsAllEightOffsetsToZero()
    {
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 100 })
        {
            CornerNWdx = 30, CornerNWdy = -20,
            CornerNEdx = -10, CornerNEdy = 40,
            CornerSEdx = 50, CornerSEdy = 50,
            CornerSWdx = -50, CornerSWdy = -50,
        };
        Assert.True(vm.IsWarped);

        vm.ResetCornerOffsets();

        Assert.Equal(0, vm.CornerNWdx);
        Assert.Equal(0, vm.CornerNWdy);
        Assert.Equal(0, vm.CornerNEdx);
        Assert.Equal(0, vm.CornerNEdy);
        Assert.Equal(0, vm.CornerSEdx);
        Assert.Equal(0, vm.CornerSEdy);
        Assert.Equal(0, vm.CornerSWdx);
        Assert.Equal(0, vm.CornerSWdy);
        Assert.False(vm.IsWarped);
    }

    [Fact]
    public void ResetRotationOrigin_SetsBackToCenter()
    {
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 100 })
        {
            RotationOriginRelX = 0,
            RotationOriginRelY = 1,
        };

        vm.ResetRotationOrigin();

        Assert.Equal(0.5, vm.RotationOriginRelX);
        Assert.Equal(0.5, vm.RotationOriginRelY);
    }

    [Fact]
    public void RotationOriginRelative_IsRelativePoint_OnUserControlBounds()
    {
        // RelativePoint mappt OuterRoot-Coords (Frame inkl. OuterPadding) auf
        // 0..1, damit XAML-RenderTransformOrigin den Drehpunkt korrekt setzt.
        // Bei Origin=Mitte (0.5/0.5 relativ zum Frame, OuterPadding=32 außen)
        // entspricht das (32 + W*0.5) / (W + 64), bei W=200 → 132/264 ≈ 0.5.
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 100 });
        var rp = vm.RotationOriginRelative;
        Assert.Equal(Avalonia.RelativeUnit.Relative, rp.Unit);
        Assert.Equal(0.5, rp.Point.X, precision: 4);
        Assert.Equal(0.5, rp.Point.Y, precision: 4);

        // Origin auf NW-Ecke: relativ zur Frame-Box (0, 0). In OuterRoot
        // entspricht das (32, 32). Mit OuterWidth = 264, OuterHeight = 164:
        // 32/264 ≈ 0.1212, 32/164 ≈ 0.1951.
        vm.RotationOriginRelX = 0;
        vm.RotationOriginRelY = 0;
        var rpNw = vm.RotationOriginRelative;
        Assert.Equal(32.0 / 264.0, rpNw.Point.X, precision: 4);
        Assert.Equal(32.0 / 164.0, rpNw.Point.Y, precision: 4);
    }

    // --- IsSelected / Chrome-Sichtbarkeit -----------------------------------

    [Fact]
    public void IsSelected_DefaultsFalse_HidesAllChrome()
    {
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 });
        Assert.False(vm.IsSelected);
        Assert.False(vm.ShowChrome);
        Assert.False(vm.ShowCornerHandles);
        Assert.Equal(0.0, vm.ChromeBorderThickness);
    }

    [Fact]
    public void IsSelected_True_RevealsBorderAndCornerHandles_AndRaisesNotifications()
    {
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 });
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.IsSelected = true;

        Assert.True(vm.ShowChrome);
        Assert.True(vm.ShowCornerHandles);
        Assert.True(vm.ChromeBorderThickness > 0);

        // Bindings müssen alle relevanten Property-Notifications sehen, sonst
        // updaten IsVisible/BorderThickness im XAML nicht live.
        Assert.Contains(nameof(TextFieldViewModel.IsSelected), changes);
        Assert.Contains(nameof(TextFieldViewModel.ShowChrome), changes);
        Assert.Contains(nameof(TextFieldViewModel.ChromeBorderThickness), changes);
        Assert.Contains(nameof(TextFieldViewModel.ShowCornerHandles), changes);
        Assert.Contains(nameof(TextFieldViewModel.ShowWireframe), changes);
    }

    [Fact]
    public void IsSelected_True_ButSkewActive_HidesCornerHandles_KeepsChrome()
    {
        // Skew-Mode blendet Eck-Handles aus, der restliche Chrome (Edge-
        // Handles, Border, Rotate-Handle) bleibt sichtbar — wie bisher in
        // Iteration 14 für selektierte Felder.
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 })
        {
            IsSelected = true,
            IsSkewActive = true,
        };

        Assert.True(vm.ShowChrome);
        Assert.False(vm.ShowCornerHandles);
    }

    [Fact]
    public void ShowWireframe_NeedsBothWarpedAndSelected()
    {
        var vm = new TextFieldViewModel(new TextField
        {
            Width = 200, Height = 40, CornerSEdx = 12,
        });

        // Warped, aber nicht selektiert → Wireframe versteckt (sonst flutet eine
        // ganze Karte mit Hilfslinien).
        Assert.True(vm.IsWarped);
        Assert.False(vm.IsSelected);
        Assert.False(vm.ShowWireframe);

        vm.IsSelected = true;
        Assert.True(vm.ShowWireframe);

        // Wenn der Warp wieder auf 0 geht, ist Wireframe trotz Selektion still.
        vm.CornerSEdx = 0;
        Assert.False(vm.ShowWireframe);
    }

    [Fact]
    public void ChromeBorderThickness_FollowsEffectiveScale_WhenSelected()
    {
        // Zoom-Kompensation aus Iteration 13 muss auch für den jetzt
        // konditional sichtbaren Border greifen — sonst wird die Linie auf
        // verkleinerten Bildern unsichtbar dünn.
        var vm = new TextFieldViewModel(new TextField { Width = 200, Height = 40 })
        {
            IsSelected = true,
        };
        Assert.Equal(TextFieldViewModel.BaseBorderThickness, vm.ChromeBorderThickness);

        vm.EffectiveScale = 0.25;  // Auto-Fit verkleinert das Bild stark.
        Assert.Equal(TextFieldViewModel.BaseBorderThickness / 0.25, vm.ChromeBorderThickness);
    }
}
