namespace CardTemplateEditor.Models;

public class AppSettings
{
    public string? LastExportDirectory { get; set; }
    public string FileNamePattern { get; set; } = "{template}_{set}_{image}";
    public Guid? LastTemplateId { get; set; }

    /// <summary>
    /// Vom User gewählter Ablageort für Templates und ihre Bilder. Null/leer
    /// = Default verwenden (%APPDATA%/CardTemplateEditor). Settings.json
    /// selbst bleibt immer am Default-Pfad, damit dieser Override beim Start
    /// auffindbar ist.
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>
    /// Frühere Ablageorte. Templates aus diesen Verzeichnissen werden beim
    /// Start zusätzlich gelistet, neue Templates landen aber im aktuellen
    /// <see cref="DataDirectory"/>. Der Default-Pfad ist hier NICHT
    /// enthalten — er wird automatisch als Fallback ergänzt, sobald ein
    /// Override aktiv ist.
    /// </summary>
    public List<string> PreviousDataDirectories { get; set; } = new();
}
