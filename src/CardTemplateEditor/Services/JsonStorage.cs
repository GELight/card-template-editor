using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardTemplateEditor.Services;

internal static class JsonStorage
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        // TextField.LineHeight = NaN als Default-Marker für "Auto" — System.Text.Json
        // serialisiert NaN/Infinity nur mit dieser Option, sonst ArgumentException.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
}
