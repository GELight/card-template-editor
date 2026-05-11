using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CardTemplateEditor.Views.Controls;

namespace CardTemplateEditor.Tests;

public class InlineEditableLabelTests
{
    [AvaloniaFact]
    public void DefaultState_IsLabelMode_NotEditing()
    {
        var ctrl = new InlineEditableLabel { Text = "Hallo" };
        var window = new Window { Content = ctrl };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(ctrl.IsEditing);
        window.Close();
    }

    [AvaloniaFact]
    public void BeginEdit_SwitchesToTextBoxMode()
    {
        var ctrl = new InlineEditableLabel { Text = "Hallo" };
        var window = new Window { Content = ctrl };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ctrl.BeginEdit();
        Assert.True(ctrl.IsEditing);

        ctrl.EndEdit();
        Assert.False(ctrl.IsEditing);
        window.Close();
    }

    [AvaloniaFact]
    public void TextProperty_TwoWayBindable_RoundtripsViaTextBox()
    {
        var ctrl = new InlineEditableLabel { Text = "alt" };
        var window = new Window { Content = ctrl };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ctrl.Text = "neu";
        Assert.Equal("neu", ctrl.Text);

        window.Close();
    }
}
