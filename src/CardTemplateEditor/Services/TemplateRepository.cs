using System.IO;
using System.Text.Json;
using CardTemplateEditor.Models;

namespace CardTemplateEditor.Services;

/// <summary>
/// Persistiert Templates + Bilder im Dateisystem. Ab Iteration 9 unterstützt
/// das Repository einen *aktiven* Root und beliebig viele *Fallback*-Roots:
/// <list type="bullet">
///   <item>Neue Templates / Imports landen ausschließlich im aktiven Root.</item>
///   <item>Beim Listen / Laden werden alle Roots gescannt; ein Template
///         "merkt sich" den Root, in dem es gefunden wurde, und alle
///         folgenden Operationen auf demselben Template arbeiten dort
///         (Image-Pfade, Save, Delete).</item>
/// </list>
/// So kann der User sein Daten-Verzeichnis umstellen, ohne die alten
/// Templates / Bilder zu verlieren — sie bleiben am alten Ort und werden
/// transparent weiter geladen.
/// </summary>
public class TemplateRepository
{
    private readonly string _activeRoot;
    private readonly List<string> _allRoots;
    private readonly Dictionary<Guid, string> _templateRoots = new();

    public TemplateRepository(string dataDir, IEnumerable<string>? fallbackDirs = null)
    {
        _activeRoot = dataDir;
        _allRoots = new List<string> { dataDir };
        if (fallbackDirs != null)
        {
            foreach (var d in fallbackDirs)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                if (_allRoots.Any(r => string.Equals(r, d, StringComparison.OrdinalIgnoreCase))) continue;
                _allRoots.Add(d);
            }
        }
    }

    public static string DefaultDataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CardTemplateEditor");

    /// <summary>Aktiver Root: hier landen neue Templates und Bild-Imports.</summary>
    public string ActiveRoot => _activeRoot;

    /// <summary>Alle bekannten Roots (aktiver Root zuerst, dann Fallbacks).</summary>
    public IReadOnlyList<string> AllRoots => _allRoots;

    /// <summary>
    /// Liefert den Root, in dem das Template <paramref name="templateId"/>
    /// liegt. Für noch nicht gespeicherte / noch nicht gefundene Templates
    /// fällt der aktive Root als Default zurück — das ist der erwartete Ort
    /// für brandneue Templates.
    /// </summary>
    private string TemplateRootFor(Guid templateId) =>
        _templateRoots.TryGetValue(templateId, out var root) ? root : _activeRoot;

    public string TemplatesDir(string root) => Path.Combine(root, "templates");

    /// <summary>
    /// Verzeichnis aller Templates des aktiven Roots. Kompatibilität mit
    /// dem alten Single-Root-Layout: Tests lesen das, um zu prüfen, ob ein
    /// Template auf der Platte gelandet ist.
    /// </summary>
    public string TemplatesDir() => TemplatesDir(_activeRoot);

    public string TemplateDir(Guid templateId) =>
        Path.Combine(TemplateRootFor(templateId), "templates", templateId.ToString());

    public string TemplateFile(Guid templateId) =>
        Path.Combine(TemplateDir(templateId), "template.json");

    public string ImagesDir(Guid templateId) =>
        Path.Combine(TemplateDir(templateId), "images");

    public string GetImagePath(Guid templateId, string fileName) =>
        Path.Combine(ImagesDir(templateId), fileName);

    public IEnumerable<Template> ListTemplates()
    {
        // Alle Roots scannen; bei Doppel-Treffer (z. B. wenn jemand denselben
        // Ordner zweimal in der Settings-Liste hat oder ein altes Template
        // aus dem Default-Fallback im aktiven Root liegt) gewinnt der erste
        // Treffer — der aktive Root steht zuerst, also wins der aktuelle Stand.
        var listed = new HashSet<Guid>();
        foreach (var root in _allRoots)
        {
            var dir = TemplatesDir(root);
            if (!Directory.Exists(dir)) continue;

            foreach (var subDir in Directory.EnumerateDirectories(dir))
            {
                if (!Guid.TryParse(Path.GetFileName(subDir), out var id)) continue;
                if (!listed.Add(id)) continue;

                var template = LoadTemplateFrom(root, id);
                if (template == null) continue;
                _templateRoots[id] = root;
                yield return template;
            }
        }
    }

    private Template? LoadTemplateFrom(string root, Guid id)
    {
        var file = Path.Combine(TemplatesDir(root), id.ToString(), "template.json");
        if (!File.Exists(file)) return null;
        using var stream = File.OpenRead(file);
        return JsonSerializer.Deserialize<Template>(stream, JsonStorage.Options);
    }

    public Template? LoadTemplate(Guid id)
    {
        if (_templateRoots.TryGetValue(id, out var root))
            return LoadTemplateFrom(root, id);

        foreach (var r in _allRoots)
        {
            var t = LoadTemplateFrom(r, id);
            if (t != null)
            {
                _templateRoots[id] = r;
                return t;
            }
        }
        return null;
    }

    public void SaveTemplate(Template template)
    {
        // Brandneue Templates → aktiver Root. Bekannte Templates bleiben
        // dort, wo sie geladen wurden — sonst würde ein Save dem User
        // unbemerkt sein altes Daten-Verzeichnis räumen.
        if (!_templateRoots.ContainsKey(template.Id))
            _templateRoots[template.Id] = _activeRoot;

        Directory.CreateDirectory(TemplateDir(template.Id));
        Directory.CreateDirectory(ImagesDir(template.Id));
        var file = TemplateFile(template.Id);
        var tmp = file + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, template, JsonStorage.Options);
        }
        File.Move(tmp, file, overwrite: true);
    }

    public string ImportImage(Guid templateId, Guid slotId, string sourceFile)
    {
        var dir = ImagesDir(templateId);
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        var fileName = slotId + ext;
        var dest = Path.Combine(dir, fileName);
        File.Copy(sourceFile, dest, overwrite: true);
        return fileName;
    }

    public void DeleteTemplate(Guid id)
    {
        var dir = TemplateDir(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        _templateRoots.Remove(id);
    }

    /// <summary>
    /// Löscht eine einzelne Bilddatei eines Slots. Best-effort: wenn die Datei
    /// bereits weg ist (verschoben, manuell gelöscht), kein Fehler.
    /// </summary>
    public void DeleteImageFile(Guid templateId, string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        var path = GetImagePath(templateId, fileName);
        if (File.Exists(path))
            File.Delete(path);
    }
}
