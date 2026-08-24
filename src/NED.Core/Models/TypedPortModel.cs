using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using NED.Abstractions.Manifest;

namespace NED.Core;

/// <summary>
/// Port s datovým typem. Vstupní porty nesou typ, který přijímají, výstupní ten, který
/// produkují. Používá se pro barvu portů a validaci kompatibility linků.
/// </summary>
public sealed class TypedPortModel : PortModel
{
    /// <summary>
    /// Type id, který tímto portem teče. Settable kvůli uzlům, jejichž typ se mění za běhu
    /// (parametr subgrafu přes type-picker, vstup Output uzlu podle nastavení grafu).
    /// </summary>
    public string DataType { get; set; }

    /// <summary>
    /// Předkové <see cref="DataType"/> — nese polymorfii. Drží si je port sám, takže
    /// <see cref="CanAttachTo"/> nepotřebuje katalog (ZBD ho volá bez jakéhokoliv kontextu).
    /// Relevantní jen na výstupním portu; na vstupu zůstává prázdné.
    /// </summary>
    public IReadOnlyList<string> Extends { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Arita portu. Hodnota na <c>Multiple</c> portu je pole prvků typu
    /// <see cref="DataType"/>; vstup navíc přijímá víc linků.
    /// </summary>
    public bool Multiple { get; set; }

    /// <summary>
    /// Exec výstup, za kterým leží vnořená cesta — až doběhne, řízení se vrátí sem.
    /// Validátor podle toho pozná, jestli je nezapojený pin konec běhu, nebo konec těla.
    /// </summary>
    public bool Subflow { get; set; }

    /// <summary>Popisek portu (pro tooltip). Výstupní port ho má null.</summary>
    public string? Label { get; set; }

    /// <summary>Má explicitní výchozí hodnotu (pro tooltip).</summary>
    public bool HasExplicitDefault { get; set; }

    /// <summary>Výchozí hodnota (pro tooltip).</summary>
    public object? DefaultValue { get; set; }

    /// <summary>Lidský popis z manifestu (pro tooltip).</summary>
    public string? Description { get; set; }

    /// <summary>Technický řádek tooltipu — typ + default (bez popisu).</summary>
    public string TypeLine
    {
        get
        {
            var prefix = Label is not null ? $"{Label}: " : "output: ";
            var tip = $"{prefix}{TypeIds.Friendly(DataType)}{(Multiple ? "[]" : "")}";
            if (HasExplicitDefault) tip += $"  •  default: {ValueFormat.ToDisplayValue(DefaultValue)}";
            return tip;
        }
    }

    public TypedPortModel(NodeModel parent, PortAlignment alignment, string dataType)
        : base(parent, alignment, new Point(0, 0), new Size(10, 10))
    {
        DataType = dataType;
    }

    public override bool CanAttachTo(ILinkable other)
    {
        if (!base.CanAttachTo(other)) return false;
        if (other is not TypedPortModel otherPort) return true;

        // Musí být jeden input (Left) a jeden output (Right).
        if (Alignment == otherPort.Alignment) return false;

        var (input, output) = Alignment == PortAlignment.Left
            ? (this, otherPort)
            : (otherPort, this);

        return IsCompatible(output, input);
    }

    /// <summary>Smí výstup <paramref name="output"/> téct do vstupu <paramref name="input"/>?</summary>
    public static bool IsCompatible(TypedPortModel output, TypedPortModel input) =>
        ArityFits(output.Multiple, input.Multiple)
        && TypeIds.IsCompatible(output.DataType, output.Extends, input.DataType);

    /// <summary>
    /// Pole smí téct jen do pole; skalár do obojího (do pole přispěje sebou).
    /// Jediné pravidlo, které se ptá na aritu — filtr pickeru sahá sem, ať se to nerozejde.
    /// </summary>
    public static bool ArityFits(bool outputMultiple, bool inputMultiple) =>
        !outputMultiple || inputMultiple;
}
