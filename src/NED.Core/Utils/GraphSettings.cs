namespace NED.Core;

/// <summary>
/// Nastavení celého grafu (ne jednotlivých nodů). Ukládá se do .nedgraph.json
/// i do exportu. Rozšiřitelné — sem patří budoucí graf-level konfigurace
/// (název, verze, popis, runtime hinty…).
/// </summary>
public sealed class GraphSettings
{
    /// <summary>
    /// Stabilní identita assetu. Vložená v souboru (ne .meta sidecar), přežije
    /// přejmenování i přesun. <see cref="Assets.AssetIndex"/> podle ní resolvuje reference.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Lidsky čitelný název assetu (zobrazí se v paletě, knihovnách). Prázdné = odvodit z názvu souboru.</summary>
    public string Name { get; set; } = "";

    /// <summary>Volitelný lidský popis grafu (účel, poznámky). Edituje se v Details panelu při prázdném výběru.</summary>
    public string Description { get; set; } = "";

    /// <summary>Čím po grafu teče řízení — jediná osa, kterou graf má. Viz <see cref="GraphFlow"/>.</summary>
    public GraphFlow Flow { get; set; } = GraphFlow.Data;

    /// <summary>Zkratka pro nejčastější dotaz „je to exec graf".</summary>
    public bool IsExec => Flow == GraphFlow.Exec;

    /// <summary>
    /// Deklarované návratové hodnoty. Pořadí je index v seznamu. Z nich staví porty
    /// Output uzel (datový tok) i všechny Return uzly (exec tok).
    /// </summary>
    public List<GraphOutput> Outputs { get; set; } = new();

    /// <summary>
    /// Smí se z grafu udělat instance (parametrizovaná kopie se zadanými hodnotami vstupů)?
    /// Nahrazuje bývalou roli <c>Template</c> — vkládat jde každý graf, instancovat jen ten,
    /// u kterého to autor zamýšlel. Výchozí hodnotu pro nový graf drží workspace config.
    /// </summary>
    public bool Instanceable { get; set; }

    /// <summary>
    /// Id zvoleného <see cref="Abstractions.IExportTranslator"/> (viz <see cref="NedCatalog.ExportTranslators"/>).
    /// null = vestavěný výchozí JSON formát.
    /// </summary>
    public string? ExportTranslator { get; set; }
}
