using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CardTemplateEditor.Models;
using CardTemplateEditor.Services;

namespace CardTemplateEditor.Tests;

public class BatchExportServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _exportDir;

    public BatchExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BatchExp_" + Guid.NewGuid());
        _exportDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestPng(string name)
    {
        var path = Path.Combine(_tempDir, name);
        var bmp = new RenderTargetBitmap(new PixelSize(20, 20), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, 20, 20));
        }
        bmp.Save(path);
        return path;
    }

    private (Template t, Func<string, string> resolve) MakeTemplate(int slots, int sets)
    {
        var template = new Template { Name = "T" };
        var slotPaths = new Dictionary<string, string>();
        for (var i = 0; i < slots; i++)
        {
            var slotId = Guid.NewGuid();
            var pathName = $"slot{i}.png";
            template.ImageSlots.Add(new ImageSlot
            {
                Id = slotId,
                Name = $"slot{i}",
                FileName = pathName,
            });
            slotPaths[pathName] = CreateTestPng(pathName);

            template.TextFields.Add(new TextField
            {
                ImageSlotId = slotId,
                Name = "titel",
                X = 2, Y = 2, Width = 10, Height = 10,
                FontSize = 10, Color = "#000000",
                CurrentText = "default",
            });
        }
        for (var s = 0; s < sets; s++)
        {
            template.Textsets.Add(new Textset
            {
                Name = $"set{s}",
                Values = { ["titel"] = $"value{s}" },
            });
        }
        return (template, n => slotPaths[n]);
    }

    [AvaloniaFact]
    public async Task Run_ExportsMxN_FilesWithCorrectNames()
    {
        var (template, resolve) = MakeTemplate(slots: 2, sets: 3);
        var svc = new BatchExportService();

        var written = await svc.RunAsync(new BatchExportRequest(
            template, template.Textsets, resolve, _exportDir, FileNamePattern.Default));

        Assert.Equal(6, written.Count);
        var names = written.Select(Path.GetFileName).ToHashSet();
        for (var s = 0; s < 3; s++)
        for (var i = 0; i < 2; i++)
            Assert.Contains($"T_set{s}_slot{i}.png", names);
    }

    [AvaloniaFact]
    public async Task Run_ProgressCallback_FiresExactlyMxN_Times()
    {
        var (template, resolve) = MakeTemplate(slots: 2, sets: 3);
        var svc = new BatchExportService();
        var reports = new List<BatchExportProgress>();
        var progress = new Progress<BatchExportProgress>(p => reports.Add(p));

        await svc.RunAsync(new BatchExportRequest(
            template, template.Textsets, resolve, _exportDir, FileNamePattern.Default),
            progress);

        // Progress wird im UI-Thread eingespielt; warte kurz auf Drain.
        for (var attempts = 0; attempts < 100 && reports.Count < 6; attempts++)
            await Task.Delay(10);

        Assert.Equal(6, reports.Count);
        Assert.Equal(6, reports[^1].Done);
        Assert.Equal(6, reports[^1].Total);
    }

    [AvaloniaFact]
    public async Task Run_EmptyTextsets_ReturnsEmpty_NoException()
    {
        var (template, resolve) = MakeTemplate(slots: 2, sets: 0);
        var svc = new BatchExportService();

        var written = await svc.RunAsync(new BatchExportRequest(
            template, template.Textsets, resolve, _exportDir, FileNamePattern.Default));

        Assert.Empty(written);
    }

    [AvaloniaFact]
    public async Task Run_EmptyImageSlots_ReturnsEmpty_NoException()
    {
        var template = new Template
        {
            Name = "T",
            Textsets = { new Textset { Name = "x" } },
        };
        var svc = new BatchExportService();

        var written = await svc.RunAsync(new BatchExportRequest(
            template, template.Textsets, _ => "ignore", _exportDir, FileNamePattern.Default));

        Assert.Empty(written);
    }

    [AvaloniaFact]
    public async Task Run_DoesNotMutate_OriginalTemplateTextFields()
    {
        var (template, resolve) = MakeTemplate(slots: 1, sets: 2);
        var originalText = template.TextFields[0].CurrentText;
        var svc = new BatchExportService();

        await svc.RunAsync(new BatchExportRequest(
            template, template.Textsets, resolve, _exportDir, FileNamePattern.Default));

        Assert.Equal(originalText, template.TextFields[0].CurrentText);
    }

    [AvaloniaFact]
    public async Task Run_CancellationToken_AbortsCleanly_DuringBatch()
    {
        var (template, resolve) = MakeTemplate(slots: 4, sets: 4); // 16 Tasks
        var svc = new BatchExportService();
        var cts = new CancellationTokenSource();
        var reports = 0;
        var progress = new Progress<BatchExportProgress>(p =>
        {
            Interlocked.Increment(ref reports);
            if (Volatile.Read(ref reports) >= 2) cts.Cancel();
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            svc.RunAsync(new BatchExportRequest(
                template, template.Textsets, resolve, _exportDir, FileNamePattern.Default),
                progress, cts.Token));

        // Es wurden nicht alle 16 Dateien geschrieben (Cancel hat zwischendurch zugeschlagen).
        var produced = Directory.Exists(_exportDir)
            ? Directory.GetFiles(_exportDir, "*.png").Length
            : 0;
        Assert.True(produced < 16, $"Erwartet weniger als 16 Dateien, aber {produced} wurden geschrieben.");
    }

    [Fact]
    public async Task Run_AppliesTextsetValues_PerSet_OnClonedTemplate_NotOriginal()
    {
        var (template, resolve) = MakeTemplate(slots: 1, sets: 2);
        var capturing = new CapturingExportService();
        var svc = new BatchExportService(capturing);

        await svc.RunAsync(new BatchExportRequest(
            template, template.Textsets, resolve, _exportDir, FileNamePattern.Default));

        // Pro Set sehen wir genau einen Aufruf mit dem dort gesetzten Wert.
        Assert.Equal(2, capturing.Observed.Count);
        Assert.Equal("value0", capturing.Observed[0]);
        Assert.Equal("value1", capturing.Observed[1]);
        // Original bleibt unverändert.
        Assert.Equal("default", template.TextFields[0].CurrentText);
    }

    private sealed class CapturingExportService : ExportService
    {
        public List<string> Observed { get; } = new();

        public override void ExportSlot(Template template, ImageSlot slot, string sourceImagePath, string destPath)
        {
            // Nimmt die geklonten TextField-Werte des aktuellen Snapshots auf.
            var f = template.TextFields.First(x => x.ImageSlotId == slot.Id);
            Observed.Add(f.CurrentText);
            // Datei trotzdem anlegen, damit Folge-Asserts (z. B. Dateizählung) nicht stören.
            File.WriteAllBytes(destPath, Array.Empty<byte>());
        }
    }
}
