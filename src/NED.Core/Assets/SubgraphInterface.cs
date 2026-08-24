using System;
using System.Collections.Generic;

namespace NED.Core.Assets;

// Typy parametrů jsou type id z manifestu (string), ne CLR typy — editor doménu nezná.

/// <summary>Jeden parametr subgrafu (odvozený z GraphInputNode v jeho souboru).</summary>
public sealed class SubgraphInput
{
    public required string         Name        { get; init; }

    /// <summary>Type id parametru (z manifestu). <c>any</c> u neznámého.</summary>
    public required string         Type        { get; init; }
    public bool                    Multiple    { get; init; }
    public required InputExposure  Exposure    { get; init; }
    public string?                 Default     { get; init; }
    public int                     Order       { get; init; }

    /// <summary>Uživatelský popis parametru (z GraphInputNode.Description) — tooltip portu na instanci.</summary>
    public string?                 Description { get; init; }

    /// <summary>Hodnotové porovnání (AssetIndex staví po každém rescanu nové instance).</summary>
    public bool SameAs(SubgraphInput other) =>
        Name == other.Name
        && Type == other.Type
        && Multiple == other.Multiple
        && Exposure == other.Exposure
        && Default == other.Default
        && Order == other.Order
        && Description == other.Description;
}

/// <summary>Jedna návratová hodnota (odvozená z OutputNode v souboru subgrafu).</summary>
public sealed class SubgraphOutput
{
    /// <summary>Stabilní identita deklarace — podle ní páruje porty <c>SyncOutputPorts</c>,
    /// takže přejmenování výstupu neodpojí volajícího.</summary>
    public string Id { get; init; } = "";

    /// <summary>Jméno hodnoty. Zároveň jméno výstupního portu na instanci.</summary>
    public required string Name  { get; init; }

    /// <summary>Type id. <c>any</c>, když si uzel typ neurčuje a graf ho nemá.</summary>
    public required string Type  { get; init; }
    public bool            Multiple { get; init; }
    public int             Order { get; init; }

    public bool SameAs(SubgraphOutput other) =>
        Id == other.Id && Name == other.Name && Type == other.Type
        && Multiple == other.Multiple && Order == other.Order;
}

/// <summary>
/// Cachované rozhraní assetu — co potřebuje <c>SubgraphNode</c> ke stavbě portů
/// bez načítání celého těla: vstupy (z GraphInputNode) + výstupy (z OutputNode).
/// </summary>
public sealed class SubgraphInterface
{
    /// <summary>Parametry, seřazené dle Order. Prázdné u Graph kind / subgrafu bez vstupů.</summary>
    public IReadOnlyList<SubgraphInput> Inputs { get; init; } = Array.Empty<SubgraphInput>();

    /// <summary>
    /// Tok subgrafu. Exec subgraf je funkce — uzel dostane exec piny a export ho
    /// <b>nevlévá</b> do volajícího, ale zavolá.
    /// </summary>
    public GraphFlow Flow { get; init; } = GraphFlow.Data;

    /// <summary>Návratové hodnoty, seřazené dle Order. Prázdné = subgraf nic nevrací.</summary>
    public IReadOnlyList<SubgraphOutput> Outputs { get; init; } = Array.Empty<SubgraphOutput>();

    /// <summary>
    /// Typ jediné návratové hodnoty — zkratka pro místa, kde víc výstupů nedává smysl
    /// (šablony, filtr pickeru na datový výstup). null = subgraf nic nevrací.
    /// </summary>
    public string? OutputType => Outputs.Count > 0 ? Outputs[0].Type : null;

    /// <summary>
    /// Výstupy spárované se jménem portu na instanci.
    ///
    /// Jediný výstup si drží historické jméno <c>Out</c> bez ohledu na Label — uložené
    /// grafy mají v linkách <c>FromPort: "Out"</c> a přejmenování portu by je odpojilo.
    /// Teprve druhý výstup si vynutí pojmenování podle Labelu.
    /// </summary>
    public IEnumerable<(string PortName, SubgraphOutput Output)> PortOutputs() =>
        Outputs.Select(output =>
            (Outputs.Count == 1 ? Abstractions.Manifest.NodeOutputNames.Default : output.Name, output));

    /// <summary>
    /// Smí se tenhle subgraf vložit do grafu s daným tokem?
    ///
    /// Záměrně <b>asymetrické</b>: datový subgraf je výraz a spočítat hodnotu jde kdekoliv,
    /// takže do exec grafu patří (nakrmí vstup kroku). Exec subgraf je procedura — potřebuje
    /// exec hrany, které datový graf do exportu vůbec nedává, takže by tam jen tiše nic nedělal.
    /// </summary>
    public bool CanBePlacedIn(GraphFlow graphFlow) =>
        Flow == GraphFlow.Data || graphFlow == GraphFlow.Exec;

    /// <summary>
    /// Hodnotové porovnání rozhraní. Rescan staví AssetEntry (a tedy i interface) vždy znovu,
    /// takže referenční rovnost nefunguje — tohle rozhoduje, zda je potřeba přestavět porty
    /// SubgraphNode instancí (viz NedCanvas.RefreshSubgraphReferences).
    /// </summary>
    public bool SameAs(SubgraphInterface other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (Flow != other.Flow) return false;
        if (Inputs.Count != other.Inputs.Count) return false;
        if (Outputs.Count != other.Outputs.Count) return false;
        for (var i = 0; i < Inputs.Count; i++)
            if (!Inputs[i].SameAs(other.Inputs[i])) return false;
        for (var i = 0; i < Outputs.Count; i++)
            if (!Outputs[i].SameAs(other.Outputs[i])) return false;
        return true;
    }
}
