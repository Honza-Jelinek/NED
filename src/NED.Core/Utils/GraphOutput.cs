using NED.Abstractions.Manifest;

namespace NED.Core;

/// <summary>
/// Jedna deklarovaná návratová hodnota grafu. Bydlí na <see cref="GraphSettings"/>, ne na
/// uzlu — uzel je jen místo, kam se dráty scházejí, a jeden port na uzel by u funkce
/// se třemi návraty rozsel po plátně tři nesouvisející sinky.
///
/// Pořadí je <b>index v seznamu</b>; žádné vlastní <c>Order</c> pole, které by se dalo rozhodit.
/// </summary>
public sealed class GraphOutput
{
    /// <summary>
    /// Stabilní identita deklarace. Podle ní se párují živé porty při srovnávání
    /// (<c>DataNodeModel.SyncDeclaredInputs</c>, <c>SubgraphNodeModel.SyncOutputPorts</c>),
    /// takže přejmenování je přejmenování a ne „zrušit a založit znovu" — drát přežije.
    /// Do souboru se zapisuje; <see cref="Name"/> je jen popisek, byť zároveň jméno portu.
    /// </summary>
    public string Id { get; set; } = NewId();

    /// <summary>Jméno hodnoty. Zároveň jméno vstupního portu na Output/Return uzlu
    /// i výstupního portu na instanci subgrafu.</summary>
    public string Name { get; set; } = "Result";

    /// <summary>Type id hodnoty.</summary>
    public string Type { get; set; } = TypeIds.Any;

    /// <summary>Hodnota je ploché pole prvků <see cref="Type"/> (viz L6 arita portů).</summary>
    public bool Multiple { get; set; }

    public GraphOutput Clone() => new() { Id = Id, Name = Name, Type = Type, Multiple = Multiple };

    /// <summary>
    /// Osm hex znaků. Guid by soubor zaplevelil a unikátnost stačí v rámci jednoho grafu —
    /// kolizi v tak malém prostoru ošetřuje <see cref="EnsureUniqueIds"/>.
    /// </summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Doplní chybějící a rozhodí duplicitní id. Volá se po načtení ze souboru — ručně psaný
    /// nebo zkopírovaný graf id mít nemusí a dvě deklarace se stejným id by si přebíraly port.
    /// </summary>
    public static void EnsureUniqueIds(IEnumerable<GraphOutput> outputs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var output in outputs)
            if (string.IsNullOrWhiteSpace(output.Id) || !seen.Add(output.Id))
            {
                do { output.Id = NewId(); } while (!seen.Add(output.Id));
            }
    }
}
