using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CardTemplateEditor.Models;

namespace CardTemplateEditor.Services;

public record BatchExportRequest(
    Template Template,
    IReadOnlyList<Textset> Textsets,
    Func<string, string> ResolveSourcePath,
    string TargetDir,
    string FileNamePattern);

public record BatchExportProgress(int Done, int Total, string CurrentFile);

/// <summary>
/// Exportiert alle Textsets × alle ImageSlots eines Templates. Rendert jedes Set
/// in einen geklonten Template-Snapshot, sodass das Original-Modell unberührt bleibt.
/// </summary>
public class BatchExportService
{
    private readonly ExportService _export;

    public BatchExportService(ExportService? export = null)
    {
        _export = export ?? new ExportService();
    }

    public Task<IReadOnlyList<string>> RunAsync(
        BatchExportRequest req,
        IProgress<BatchExportProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() => Run(req, progress, ct), ct);
    }

    private IReadOnlyList<string> Run(
        BatchExportRequest req,
        IProgress<BatchExportProgress>? progress,
        CancellationToken ct)
    {
        var written = new List<string>();
        if (req.Textsets.Count == 0 || req.Template.ImageSlots.Count == 0)
            return written;

        Directory.CreateDirectory(req.TargetDir);

        var total = req.Textsets.Count * req.Template.ImageSlots.Count;
        var done = 0;

        foreach (var set in req.Textsets)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = CloneWithApplied(req.Template, set);

            var index = 1;
            foreach (var slot in req.Template.ImageSlots)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(slot.FileName))
                {
                    done++;
                    index++;
                    progress?.Report(new BatchExportProgress(done, total, ""));
                    continue;
                }

                var sourcePath = req.ResolveSourcePath(slot.FileName);
                if (!File.Exists(sourcePath))
                {
                    done++;
                    index++;
                    progress?.Report(new BatchExportProgress(done, total, ""));
                    continue;
                }

                var fileName = Services.FileNamePattern.Format(
                    req.FileNamePattern,
                    new FileNameContext(req.Template.Name, set.Name, slot.Name, index));
                var destPath = Path.Combine(req.TargetDir, fileName + ".png");

                _export.ExportSlot(snapshot, slot, sourcePath, destPath);
                written.Add(destPath);
                done++;
                index++;
                progress?.Report(new BatchExportProgress(done, total, destPath));
            }
        }

        return written;
    }

    private static Template CloneWithApplied(Template src, Textset set)
    {
        return new Template
        {
            Id = src.Id,
            Name = src.Name,
            ImageSlots = src.ImageSlots,
            TextFields = src.TextFields.Select(f => new TextField
            {
                Id = f.Id,
                ImageSlotId = f.ImageSlotId,
                Name = f.Name,
                X = f.X, Y = f.Y, Width = f.Width, Height = f.Height,
                FontFamily = f.FontFamily,
                FontSize = f.FontSize,
                FontWeight = f.FontWeight,
                Color = f.Color,
                HorizontalTextAlignment = f.HorizontalTextAlignment,
                VerticalTextAlignment = f.VerticalTextAlignment,
                Rotation = f.Rotation,
                CornerNWdx = f.CornerNWdx, CornerNWdy = f.CornerNWdy,
                CornerNEdx = f.CornerNEdx, CornerNEdy = f.CornerNEdy,
                CornerSEdx = f.CornerSEdx, CornerSEdy = f.CornerSEdy,
                CornerSWdx = f.CornerSWdx, CornerSWdy = f.CornerSWdy,
                StretchX = f.StretchX, StretchY = f.StretchY,
                AutoFit = f.AutoFit,
                LineHeight = f.LineHeight,
                LetterSpacing = f.LetterSpacing,
                BoldRanges = f.BoldRanges.Select(b => new BoldRange { Start = b.Start, Length = b.Length }).ToList(),
                CurrentText = !string.IsNullOrEmpty(f.Name)
                              && set.Values.TryGetValue(f.Name, out var v)
                    ? v
                    : f.CurrentText,
            }).ToList(),
            Textsets = src.Textsets,
        };
    }
}
