using System.IO;
using System.Linq;

namespace CardTemplateEditor.Services;

public readonly record struct FileNameContext(
    string Template,
    string Set,
    string Image,
    int Index);

public static class FileNamePattern
{
    private static readonly char[] Invalid =
        Path.GetInvalidFileNameChars()
            .Concat(new[] { '/', '\\', ':' })
            .Distinct()
            .ToArray();

    public const string Default = "{template}_{set}_{image}";

    public static string Format(string pattern, FileNameContext ctx)
    {
        if (string.IsNullOrWhiteSpace(pattern)) pattern = Default;
        var s = pattern
            .Replace("{template}", Sanitize(ctx.Template))
            .Replace("{set}", Sanitize(ctx.Set))
            .Replace("{image}", Sanitize(ctx.Image))
            .Replace("{index}", ctx.Index.ToString());
        return Sanitize(s);
    }

    public static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return "_";
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (System.Array.IndexOf(Invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars);
    }
}
