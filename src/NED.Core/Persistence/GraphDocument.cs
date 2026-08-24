namespace NED.Core.Persistence;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// DTO pro uložení celého grafu (.nedgraph.json).
/// Settings (graf-level), uzly (hodnoty + pozice) i linky (propojení portů).
/// </summary>
public sealed class GraphDocument
{
    /// <summary>Aktuální verze schématu, kterou tenhle build zapisuje.</summary>
    public const int CurrentSchemaVersion = 4;

    /// <summary>
    /// Verze schématu souboru. Chybí-li (soubory před zavedením pole), zůstane 1.
    /// Vyšší než <see cref="CurrentSchemaVersion"/> = soubor z novější verze editoru;
    /// načte se, ale uživatel dostane varování, že o něco může přijít.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    public GraphSettingsDto Settings { get; set; } = new();
    public List<GraphNodeDto> Nodes { get; set; } = new();
    public List<SubgraphNodeDto> SubgraphNodes { get; set; } = new();
    public List<GraphLinkDto> Links { get; set; } = new();
}

/// <summary>Graf-level nastavení. Rozšiřitelné — sem patří budoucí graf konfigurace.</summary>
public sealed class GraphSettingsDto
{
    /// <summary>
    /// Stabilní GUID assetu (identita pro reference). Prázdné u starých souborů → vygeneruje se.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>Lidsky čitelný název. Prázdné = odvodit z názvu souboru.</summary>
    public string? Name { get; set; }

    /// <summary>Volitelný popis grafu.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Tok řízení (<c>"Exec"</c>). Datové grafy ho <b>nezapisují</b> — chybějící hodnota
    /// znamená Data.
    /// </summary>
    public string? Flow { get; set; }

    /// <summary>
    /// Deklarované návratové hodnoty; pořadí je pořadí v poli. Prázdné/chybějící =
    /// graf nic nevrací.
    /// </summary>
    public List<GraphOutputDto>? Outputs { get; set; }

    /// <summary>Smí se z grafu udělat instance. Nezapisuje se, když je false.</summary>
    public bool Instanceable { get; set; }

    /// <summary>Id zvoleného export translatoru. null = vestavěný JSON formát.</summary>
    public string? ExportTranslator { get; set; }

    /// <summary>
    /// Node packy, které graf potřebuje (odvozené z použitých uzlů při uložení).
    /// Chybí-li pack při otevření, editor to řekne — místo aby se všechny uzly tiše
    /// proměnily v placeholdery a uživatel nevěděl proč.
    /// </summary>
    public List<PackRequirement>? RequiredPacks { get; set; }
}

/// <summary>Deklarace jedné návratové hodnoty grafu.</summary>
public sealed class GraphOutputDto
{
    /// <summary>Stabilní identita deklarace — párování portů přežije přejmenování.
    /// Prázdné u ručně psaného souboru; načtení ho doplní.</summary>
    public string? Id { get; set; }

    public string Name { get; set; } = "";
    public string Type { get; set; } = "";

    /// <summary>Arita — hodnota je pole prvků <see cref="Type"/>. Nezapisuje se, když je false.</summary>
    public bool Multiple { get; set; }
}

[JsonConverter(typeof(PackRequirementJsonConverter))]
public sealed class PackRequirement
{
    public string Id { get; set; } = "";
    public string? Version { get; set; }
}

/// <summary>Čte nový objekt i starý řetězec z dokumentů schématu 2.</summary>
internal sealed class PackRequirementJsonConverter : JsonConverter<PackRequirement>
{
    public override PackRequirement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new PackRequirement { Id = reader.GetString() ?? "" };

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        return new PackRequirement
        {
            Id = Property(root, "Id")?.GetString() ?? "",
            Version = Property(root, "Version")?.GetString(),
        };
    }

    public override void Write(Utf8JsonWriter writer, PackRequirement value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Id", value.Id);
        if (value.Version is not null) writer.WriteString("Version", value.Version);
        writer.WriteEndObject();
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return null;
    }
}

public sealed class GraphNodeDto
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Type id z manifestu (<c>"pack/typ"</c>).</summary>
    public string TypeName { get; set; } = "";

    /// <summary>Hodnoty vstupů uzlu. Klíč = jméno vstupu z manifestu.</summary>
    public Dictionary<string, object?> Fields { get; set; } = new();

    /// <summary>
    /// Override expozice přepínatelných vstupů (jméno vstupu → je port?).
    /// Ukládají se jen vstupy, jejichž režim se liší od výchozího. null = vše default.
    /// </summary>
    public Dictionary<string, bool>? PortModes { get; set; }
}

/// <summary>DTO pro SubgraphNode referenci (GUID + hodnoty Field-exposed vstupů).</summary>
public sealed class SubgraphNodeDto
{
    public string Id { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>GUID referencovaného subgrafu.</summary>
    public string SubgraphId { get; set; } = "";

    /// <summary>Hodnoty Field-exposed vstupů (klíč = input name).</summary>
    public Dictionary<string, string> FieldValues { get; set; } = new();

    /// <summary>Per-instance override expozice vstupů (name → je port?). null = vše dle rozhraní.</summary>
    public Dictionary<string, bool>? PortModes { get; set; }
}

public sealed class GraphLinkDto
{
    public string FromNode { get; set; } = "";
    public string FromPort { get; set; } = "";
    public string ToNode { get; set; } = "";

    /// <summary>Jméno vstupu na cílovém uzlu (stabilní, ne port GUID).</summary>
    public string ToPort { get; set; } = "";
}
