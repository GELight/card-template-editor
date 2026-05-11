namespace CardTemplateEditor.Models;

public class Template
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<ImageSlot> ImageSlots { get; set; } = new();
    public List<TextField> TextFields { get; set; } = new();
    public List<Textset> Textsets { get; set; } = new();
}
