using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;

namespace CardTemplateEditor.Views.Controls;

/// <summary>
/// Listenzeilen-freundliches Label: rendert standardmäßig nur einen TextBlock,
/// damit der Click den darunterliegenden ListBoxItem selektieren kann.
/// Erst ein Doppelklick wechselt in den Edit-Modus (TextBox); Enter, Escape oder
/// Fokus-Verlust schließen den Edit-Modus wieder.
/// </summary>
public partial class InlineEditableLabel : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<InlineEditableLabel, string?>(
            nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsEditingProperty =
        AvaloniaProperty.Register<InlineEditableLabel, bool>(nameof(IsEditing));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsEditing
    {
        get => GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public InlineEditableLabel()
    {
        InitializeComponent();

        DoubleTapped += OnDoubleTapped;
        EditPart.LostFocus += (_, _) => EndEdit();
        EditPart.KeyDown += OnEditKeyDown;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        BeginEdit();
        // Verhindern, dass das Doppelklick-Event den ListBoxItem doppelt selektiert
        // oder andere DoubleTapped-Handler triggert.
        e.Handled = true;
    }

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Escape)
        {
            EndEdit();
            e.Handled = true;
        }
    }

    public void BeginEdit()
    {
        if (IsEditing) return;
        IsEditing = true;
        // Fokus verlässt sonst beim Sichtbar-Schalten den Edit-Mode wieder; daher
        // den Focus-Aufruf auf den nächsten Dispatcher-Tick verschieben.
        Dispatcher.UIThread.Post(() =>
        {
            EditPart.Focus();
            EditPart.SelectAll();
        }, DispatcherPriority.Background);
    }

    public void EndEdit()
    {
        if (!IsEditing) return;
        IsEditing = false;
    }
}
