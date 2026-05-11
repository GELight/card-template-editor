using Avalonia;
using Avalonia.Headless;
using CardTemplateEditor;
using CardTemplateEditor.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace CardTemplateEditor.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
}
