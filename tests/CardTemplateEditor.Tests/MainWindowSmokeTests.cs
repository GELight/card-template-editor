using Avalonia.Headless.XUnit;
using CardTemplateEditor.Services;
using CardTemplateEditor.ViewModels;
using CardTemplateEditor.Views;

namespace CardTemplateEditor.Tests;

public class MainWindowSmokeTests : IDisposable
{
    private readonly string _tempDir;

    public MainWindowSmokeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MainWindowSmoke_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [AvaloniaFact]
    public void MainWindow_Opens_WithoutException()
    {
        var repo = new TemplateRepository(_tempDir);
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(repo),
        };
        window.Show();
        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_Opens_WithEmptyDataContext()
    {
        var window = new MainWindow();
        window.Show();
        window.Close();
    }
}
