using System.Text.Json;
using System.Text.Json.Serialization;
using NED.Abstractions.Manifest;

namespace NED.Manifest.Generator;

/// <summary>
/// Zápis manifestu. Serializátor záměrně nežije v <c>NED.Abstractions</c> — ta cílí na
/// netstandard2.0 a nesmí táhnout žádnou závislost. Čtecí protějšek je v <c>NED.Core</c>.
/// </summary>
public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(NodeManifest manifest) => JsonSerializer.Serialize(manifest, Options);
}
