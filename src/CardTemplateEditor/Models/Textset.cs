namespace CardTemplateEditor.Models;

public class Textset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = new();
}
