using NED.Abstractions;
using NED.Abstractions.Manifest;

namespace NED.Core;

/// <summary>Jak se vstup subgrafu vystaví na rodičovském SubgraphNode.</summary>
public enum InputExposure
{
    /// <summary>Vstupní port — připojí se k němu producer v rodiči.</summary>
    Port,

    /// <summary>Inline editovatelné pole — konstanta zadaná na nodu (jen primitivy/enumy).</summary>
    Field,
}

/// <summary>
/// Parametr subgrafu. Dostupný jen v Subgraph kind. Uvnitř subgrafu má jen
/// <b>výstupní</b> port (zdroj hodnoty pro tělo), žádné vstupy. Typ výstupu
/// volí uživatel (type-picker).
///
/// Tahle třída se za běhu <b>neinstanciuje</b> — je to jen deklarace metadat pro generátor
/// vestavěného manifestu. Volbu type-pickeru manifest vyjádřit neumí, dopočítá ji
/// <see cref="DataNodeModel.RefreshDynamicTypes"/>.
/// </summary>
[NodeInfo("Input", Category = "Interface", Color = "#2F958E", Icon = "▸")]
public sealed class GraphInputNode : IGraphData
{
    // Všechna nastavení jsou Details = true → na plátně zůstane jen header + output port,
    // editují se v Details panelu.

    [NodeField("Name")]
    public string Name { get; set; } = "In";

    [NodeField("Type", Details = true), NodeTypePicker]
    public string InputTypeName { get; set; } = TypeIds.Double;

    [NodeField("List", Details = true)]
    public bool Multiple { get; set; }

    [NodeField("Expose", Details = true)]
    public InputExposure Exposure { get; set; } = InputExposure.Port;

    /// <summary>Default při nepřipojení / Field režimu. Raw string, interpretuje se dle typu při exportu.</summary>
    [NodeField("Default", Details = true)]
    public string DefaultValue { get; set; } = "";

    [NodeField("Order", Details = true)]
    public int Order { get; set; }

    /// <summary>Uživatelský popis parametru. Propisuje se na output port (tooltip, rozhraní subgrafu).</summary>
    [NodeField("Description", Details = true)]
    public string Description { get; set; } = "";
}
