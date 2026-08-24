using System.Collections.Generic;

namespace NED.Abstractions.Manifest;

/// <summary>
/// Popis jednoho node packu — vše, co editor potřebuje vědět o cizích typech uzlů.
///
/// Manifest nahrazuje reflexi nad doménovou assembly. Generuje se při buildu domény
/// (nebo se napíše ručně, třeba z Unity či TypeScriptu) a editor pak běží bez jediné
/// reference na doménu. Autorský zážitek se nemění — dál se píší anotované třídy.
///
/// POCO záměrně bez závislosti na serializátoru: <c>NED.Abstractions</c> cílí na
/// netstandard2.0 a nesmí nic táhnout. Čtení a zápis JSON dělá volající.
/// </summary>
public sealed class NodeManifest
{
    public const int Current = 1;

    /// <summary>Verze schématu manifestu (ne packu). Neznámá vyšší = editor varuje a načte, co umí.</summary>
    public int ManifestVersion { get; set; } = Current;

    public PackInfo Pack { get; set; } = new();

    /// <summary>Enumy použité v polích — widget z nich staví dropdown, bez hodnot není z čeho.</summary>
    public List<EnumDescriptor> Enums { get; set; } = new();

    public List<NodeTypeDescriptor> Types { get; set; } = new();
}

/// <summary>Identita packu. <see cref="Id"/> je prefix všech typů uvnitř.</summary>
public sealed class PackInfo
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Version { get; set; }
}

/// <summary>Enum jako výčet povolených řetězcových hodnot.</summary>
public sealed class EnumDescriptor
{
    public string Id { get; set; } = "";
    public List<string> Values { get; set; } = new();
}

/// <summary>Jeden typ uzlu — to, co v C# nesou atributy na třídě a jejích properties.</summary>
public sealed class NodeTypeDescriptor
{
    /// <summary>Stabilní identita <c>"pack/local"</c>. Nezávislá na CLR jménu — třídu lze přejmenovat.</summary>
    public string Id { get; set; } = "";

    /// <summary>Zobrazované jméno v paletě a v headeru uzlu.</summary>
    public string Name { get; set; } = "";

    public string Category { get; set; } = "General";
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Předkové a rozhraní (jako type id) — nese polymorfii portů.
    /// Bez tohohle by <c>PerkChild</c> nešel připojit do portu typu <c>Perk</c>.
    /// </summary>
    public List<string> Extends { get; set; } = new();

    public List<NodeOutputDescriptor> Outputs { get; set; } = new();

    public List<NodeInputDescriptor> Inputs { get; set; } = new();
}

/// <summary>Jeden pojmenovaný výstupní port uzlu.</summary>
public sealed class NodeOutputDescriptor
{
    public string Name { get; set; } = NodeOutputNames.Default;
    public string? Type { get; set; }
    public string? Description { get; set; }

    /// <summary>Pole prvků typu <see cref="Type"/> vydané jedním drátem.</summary>
    public bool Multiple { get; set; }

    /// <summary>
    /// Chování řízení za exec pinem. <c>null</c> = obyčejné pokračování.
    /// Nullable schválně: serializace vynechává null, takže manifesty bez řídicích
    /// uzlů zůstávají bajt na bajt stejné a drift testy nemusí přegenerovat.
    /// </summary>
    public ExecOutputRole? Role { get; set; }
}

public static class NodeOutputNames
{
    public const string Default = "Out";
}

/// <summary>Vstup uzlu — port (linkovatelný) nebo pole (inline konstanta).</summary>
public sealed class NodeInputDescriptor
{
    /// <summary>Klíč pro persistenci i linky. Stabilní; C# property se pod ním smí přejmenovat.</summary>
    public string Name { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>Výchozí režim expozice: <see cref="InputKind.Port"/> nebo <see cref="InputKind.Field"/>.</summary>
    public InputKind Kind { get; set; }

    /// <summary>Type id — vestavěný skalár (viz <see cref="TypeIds"/>) nebo <c>"pack/typ"</c>.</summary>
    public string Type { get; set; } = TypeIds.Any;

    /// <summary>Výchozí hodnota; null = typový default.</summary>
    public object? Default { get; set; }

    /// <summary>Autor uvedl výchozí hodnotu jinou než default(T) — vstup je pak nepovinný.</summary>
    public bool HasExplicitDefault { get; set; }

    public string? Description { get; set; }

    /// <summary>Pole hodnot — do jednoho slotu vede víc linků.</summary>
    public bool Multiple { get; set; }

    /// <summary>Graf smí nechat nepřipojené.</summary>
    public bool Optional { get; set; }

    /// <summary>Nerenderuje se inline na uzlu, jen v Details panelu. Nikdy port.</summary>
    public bool Details { get; set; }

    /// <summary>Dropdown typů místo textového pole. Nikdy port.</summary>
    public bool TypePicker { get; set; }
}

public enum InputKind
{
    Field,
    Port,
}
