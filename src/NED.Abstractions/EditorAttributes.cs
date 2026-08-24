using System;

namespace NED.Abstractions;

/// <summary>
/// Identita node packu — prefix všech type id v manifestu. Dává se na assembly:
/// <c>[assembly: NodePack("sample.nodes", Version = "1.0")]</c>.
/// Chybí-li, generátor odvodí id z názvu assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class NodePackAttribute : Attribute
{
    public string Id { get; }
    public string? Name { get; init; }
    public string? Version { get; init; }

    public NodePackAttribute(string id) => Id = id;
}

/// <summary>Metadata třídy — název, kategorie v paletě, barva a ikona headeru nodu.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NodeInfoAttribute : Attribute
{
    public string Name { get; }
    public string Category { get; init; } = "General";

    /// <summary>
    /// Stabilní lokální id v rámci packu. Výchozí = jméno třídy.
    /// Nastav ho, než třídu přejmenuješ — uložené grafy pak přejmenování nepocítí.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Určuje životnost runtime instance uzlu. <see langword="false"/> (výchozí) znamená novou
    /// instanci při každém průchodu; <see langword="true"/> jednu instanci pro stejné <c>$id</c>
    /// v rámci jednoho běhu. Instance se nesdílí mezi souběžnými běhy stejného grafu.
    ///
    /// Run-scoped instance je sdílený měnitelný stav. Sekvenční interpret ji dnes používá bezpečně,
    /// ale případné paralelní větve budou muset přístup ke stavovému uzlu synchronizovat.
    /// </summary>
    public bool Stateful { get; init; }

    // null = „nepřepsáno" → vzhled se vezme z NedTheme podle Category.
    public string? Color { get; init; }
    public string? Icon { get; init; }

    public NodeInfoAttribute(string name) => Name = name;
}

/// <summary>Inline editovatelná vlastnost. Widget vykreslí input podle typu (number/text/select).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NodeFieldAttribute : Attribute
{
    public string Label { get; }
    public double Min { get; init; } = double.NaN;
    public double Max { get; init; } = double.NaN;

    /// <summary>
    /// true → nastavení se nerenderuje inline na nodu, ale jen v Details panelu (po výběru nodu).
    /// Takové pole je config-only — nelze ho přepnout na port. Tudy přidává jakýkoliv node typ
    /// vlastní nastavení do Details panelu bez UI kódu.
    /// </summary>
    public bool Details { get; init; }

    /// <summary>Stabilní klíč pro persistenci. Výchozí = jméno property. Viz <see cref="NodeInfoAttribute.Id"/>.</summary>
    public string? Id { get; init; }

    public NodeFieldAttribute(string label) => Label = label;
}

/// <summary>
/// Render hint pro <see cref="NodeFieldAttribute"/> string-vlastnost, která drží
/// plně kvalifikovaný název typu. Widget vykreslí dropdown typů (z katalogu)
/// místo textového pole. Vlastnost musí mít i [NodeField] (kvůli persistenci + popisku).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NodeTypePickerAttribute : Attribute { }

/// <summary>Linkovatelný vstup (levý port). Multiple = true → pole (více linků do jednoho slotu).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NodePortAttribute : Attribute
{
    public string Label { get; }

    /// <summary>Pole — více linků do jednoho slotu. Vizuálně: oválný/protáhlý port.</summary>
    public bool Multiple { get; init; }

    /// <summary>Nepovinný vstup — graf smí zůstat nepřipojený. Vizuálně: hranatý port.</summary>
    public bool Optional { get; init; }

    /// <summary>Stabilní klíč pro persistenci i linky. Výchozí = jméno property. Viz <see cref="NodeInfoAttribute.Id"/>.</summary>
    public string? Id { get; init; }

    public NodePortAttribute(string label) => Label = label;
}

/// <summary>Volný lidský popis (tooltip). Lze dát kamkoliv — třída, property…</summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
public sealed class NedDescriptionAttribute : Attribute
{
    public string Text { get; }
    public NedDescriptionAttribute(string text) => Text = text;
}

/// <summary>
/// Jak se chová řízení, když projde exec výstupem. Deklarace, ne popisek —
/// runtime ji vymáhá, editor podle ní pozná, co znamená nezapojený pin.
/// </summary>
public enum ExecOutputRole
{
    /// <summary>Skok dál. Když za tímhle pinem cesta skončí, skončil běh.</summary>
    Continue,

    /// <summary>
    /// Vnořená cesta; až doběhne, řízení se vrací vlastníkovi pinu, který rozhodne
    /// o dalším kroku. Smyčka je jen jeden případ užití — stejně se popíše
    /// transakce, handler výjimky nebo resource scope.
    /// </summary>
    Subflow,
}

/// <summary>
/// Řídí výstupní (pravý) port.
/// Type = typeof(double) → node outputuje double (computational node).
/// Type = null (default) → node outputuje sám sebe (container/domain node).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class NodeOutputAttribute : Attribute
{
    public string Name { get; } = Manifest.NodeOutputNames.Default;

    /// <summary>Typ výstupu. null = vlastní třída nodu.</summary>
    public Type? Type { get; }

    /// <summary>Pole prvků typu <see cref="Type"/> vydané jedním drátem.</summary>
    public bool Multiple { get; init; }

    /// <summary>Chování řízení za tímhle pinem. Dává smysl jen u <c>exec</c> výstupů.</summary>
    public ExecOutputRole ExecRole { get; init; }

    public NodeOutputAttribute() { }
    public NodeOutputAttribute(Type outputType) => Type = outputType;
    public NodeOutputAttribute(string name) => Name = name;
    public NodeOutputAttribute(string name, Type outputType)
    {
        Name = name;
        Type = outputType;
    }
}

/// <summary>Uzel bez výstupního portu.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class NodeSinkAttribute : Attribute;
