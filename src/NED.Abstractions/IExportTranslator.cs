using System;
using System.Collections.Generic;

namespace NED.Abstractions;

/// <summary>
/// Jazykově neutrální model exportu předávaný translatorům. Obálka verzuje veřejnou smlouvu,
/// uvádí použité packy a nese stromy s explicitními $type/$error markery.
/// </summary>
public sealed class ExportModel
{
    public const int CurrentVersion = 1;

    /// <summary>Verze veřejného exportního formátu.</summary>
    public int ExportVersion { get; set; } = CurrentVersion;

    /// <summary>Packy použité exportovaným stromem, včetně verzí známých katalogu.</summary>
    public IReadOnlyList<ExportPack> Packs { get; set; } = Array.Empty<ExportPack>();

    /// <summary>
    /// Deklarované návratové hodnoty grafu. V datovém toku nese každá i <c>Value</c>;
    /// v exec toku jsou to jen deklarace a hodnoty nesou Return uzly v tabulce.
    /// </summary>
    public IReadOnlyList<ExportGraphOutput> Outputs { get; set; } = Array.Empty<ExportGraphOutput>();

    /// <summary>Veřejné parametry grafu v pořadí určeném editorem.</summary>
    public IReadOnlyList<ExportGraphInput> Inputs { get; set; } = Array.Empty<ExportGraphInput>();

    public string? GraphKind { get; set; }
    public string? Entry { get; set; }
    public IReadOnlyList<IReadOnlyDictionary<string, object?>>? Nodes { get; set; }
    public IReadOnlyList<ExportExecEdge>? ExecEdges { get; set; }

    /// <summary>
    /// Volané exec funkce. Na rozdíl od datového subgrafu se exec subgraf do volajícího
    /// <b>nevlévá</b> — v <see cref="Nodes"/> po něm zůstane uzel s <c>$call</c>.
    /// </summary>
    public IReadOnlyList<ExportFunction> Functions { get; set; } = Array.Empty<ExportFunction>();
}

/// <summary>Jedno tělo volané funkce. Tvar je stejný jako u kořenového exec grafu.</summary>
public sealed class ExportFunction
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public IReadOnlyList<ExportGraphInput> Inputs { get; set; } = Array.Empty<ExportGraphInput>();
    public string? Entry { get; set; }
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Nodes { get; set; }
        = Array.Empty<IReadOnlyDictionary<string, object?>>();
    public IReadOnlyList<ExportExecEdge> ExecEdges { get; set; } = Array.Empty<ExportExecEdge>();

    /// <summary>Deklarované návratové hodnoty; konkrétní hodnoty nesou Return uzly.</summary>
    public IReadOnlyList<ExportGraphOutput> Outputs { get; set; } = Array.Empty<ExportGraphOutput>();
}

/// <summary>
/// Deklarovaná návratová hodnota. <see cref="Value"/> je vyplněná jen tam, kde hodnotu
/// nese datový tah (kořen datového grafu); v exec toku zůstává null a hodnotu vydá
/// ten Return uzel, na kterém běh skončil.
/// </summary>
public sealed class ExportGraphOutput
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Multiple { get; set; }
    public object? Value { get; set; }

    /// <summary>
    /// Nese tahle deklarace hodnotu? Odlišuje „nezapojený vstup, tedy null" od
    /// „hodnota sem nepatří" — obojí má <see cref="Value"/> null.
    /// </summary>
    public bool HasValue { get; set; }
}

public sealed class ExportGraphInput
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Multiple { get; set; }
    public string? Default { get; set; }
    public string? Description { get; set; }
}

public sealed class ExportExecEdge
{
    public string From { get; set; } = "";
    public string Pin { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class ExportPack
{
    public string Id { get; set; } = "";
    public string? Version { get; set; }
}

/// <summary>
/// Pluggable cílový formát exportu. Discovery funguje stejně jako u nodů — registruje se
/// přes host (RegisterAssemblyOf), NedCatalog typy skenuje a nabízí je v Details panelu.
/// </summary>
public interface IExportTranslator
{
    /// <summary>Stabilní identifikátor — persistuje se v GraphSettings.ExportTranslator.</summary>
    string Id { get; }

    /// <summary>Zobrazované jméno v Details panelu.</summary>
    string DisplayName { get; }

    /// <summary>Výsledný text exportu (formát libovolný — JSON, XML, skript…).</summary>
    string Translate(ExportModel model);
}
