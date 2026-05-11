using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CardTemplateEditor.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public bool Result { get; private set; }

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message, string confirmLabel = "Löschen", string cancelLabel = "Abbrechen")
        : this()
    {
        Title = title;
        MessageBlock.Text = message;
        ConfirmButton.Content = confirmLabel;
        CancelButton.Content = cancelLabel;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Result = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }

    public static async Task<bool> ConfirmAsync(
        Window owner, string title, string message,
        string confirmLabel = "Löschen", string cancelLabel = "Abbrechen")
    {
        var dlg = new ConfirmDialog(title, message, confirmLabel, cancelLabel);
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }
}
