using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using NED.Abstractions.Manifest;

namespace NED.Core.Manifest;

/// <summary>
/// Čtení manifestů. Protějšek zapisovací poloviny v <c>NED.Manifest.Generator</c> —
/// serializátor záměrně nežije v <c>NED.Abstractions</c>, aby ta zůstala netstandard2.0
/// bez jediné závislosti.
/// </summary>
public static class ManifestJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static NodeManifest? Read(string json) =>
        JsonSerializer.Deserialize<NodeManifest>(json, Options);

    public static NodeManifest? ReadFile(string path) => Read(File.ReadAllText(path));

    /// <summary>True, pokud manifest používá novější schéma, než tato verze editoru zná.</summary>
    public static bool IsNewerThanSupported(NodeManifest manifest) =>
        manifest.ManifestVersion > NodeManifest.Current;

    /// <summary>Vestavěné uzly editoru (Output, Input) — embedded resource, generovaný stejným nástrojem.</summary>
    public static NodeManifest BuiltIn()
    {
        using var stream = typeof(ManifestJson).Assembly
            .GetManifestResourceStream("NED.Core.ned.builtin.nodes.json")
            ?? throw new InvalidOperationException("Vestavěný manifest chybí v resources.");
        using var reader = new StreamReader(stream);
        return Read(reader.ReadToEnd())
            ?? throw new InvalidOperationException("Vestavěný manifest se nepodařilo rozparsovat.");
    }
}
