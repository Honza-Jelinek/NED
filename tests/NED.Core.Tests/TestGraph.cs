using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Options;
using NED.Abstractions.Manifest;
using NED.Core;
using NED.Core.Manifest;
using NED.Core.Persistence;
using NED.Manifest.Generator;

namespace NED.Core.Tests;

/// <summary>
/// Sdílené stavební bloky pro charakterizační testy. Grafy se staví headless —
/// <see cref="BlazorDiagram"/> nepotřebuje běžící Blazor (stejně to dělá
/// <c>GraphExporter.LoadSubgraphBody</c>).
/// </summary>
public static class TestGraph
{
    public const string Number = "sandbox/NumberConstant";
    public const string AddNode = "sandbox/Add";
    public const string Output = BuiltInIds.Output;

    /// <summary>Jméno výchozí deklarace — zároveň jméno portu na Output/Return uzlu.</summary>
    public const string Result = "Result";

    /// <summary>
    /// Katalog nad Sandboxem. Manifest se generuje v paměti ze stejné assembly, kterou
    /// používá i produkční generátor — testy tak nepotřebují soubor na disku a nemůžou
    /// běžet proti zastaralé kopii.
    /// </summary>
    public static NedCatalog Catalog() =>
        new(new[] { ManifestBuilder.Build(typeof(Sandbox.Add).Assembly, out _) });

    public static BlazorDiagram NewDiagram() => new(new BlazorDiagramOptions());

    /// <summary>Deklarace výstupů pro DTO — první se jmenuje <see cref="Result"/>, další Result2…</summary>
    public static List<GraphOutputDto> Declare(params string[] types) => types
        .Select((type, i) => new GraphOutputDto { Name = i == 0 ? Result : $"{Result}{i + 1}", Type = type })
        .ToList();

    /// <summary>Totéž pro živá <see cref="GraphSettings"/>.</summary>
    public static List<GraphOutput> Outputs(params string[] types) => types
        .Select((type, i) => new GraphOutput { Name = i == 0 ? Result : $"{Result}{i + 1}", Type = type })
        .ToList();

    /// <summary>Nastavení datového grafu s jednou deklarací daného typu.</summary>
    public static GraphSettings Settings(string outputType = TypeIds.Double) =>
        new() { Outputs = Outputs(outputType) };

    /// <summary>Vloží uzel daného typu; <paramref name="values"/> přepíší výchozí hodnoty vstupů.</summary>
    public static DataNodeModel Add(this BlazorDiagram d, NedCatalog catalog, string typeId,
                                    double x = 0, double y = 0,
                                    IReadOnlyList<GraphOutput>? declared = null,
                                    params (string Name, object? Value)[] values)
    {
        var descriptor = catalog.Resolve(typeId)
            ?? throw new InvalidOperationException($"Typ {typeId} není v katalogu.");
        var node = new DataNodeModel(descriptor, new Point(x, y), id: null, catalog);
        foreach (var (name, value) in values) node.Values[name] = value;
        // Sink bez deklarací nemá porty, což v testu nikdy není záměr — pokud si volající
        // neřekne jinak, dostane jednu deklaraci „Result: any". Explicitní prázdný seznam
        // se respektuje (test na graf bez deklarací).
        node.SyncDeclaredInputs(
            declared ?? (node.DeclaresGraphOutputs ? Outputs(TypeIds.Any) : (IReadOnlyList<GraphOutput>)Array.Empty<GraphOutput>()));
        d.Nodes.Add(node);
        return node;
    }

    /// <summary>Sink s porty podle deklarací.</summary>
    public static DataNodeModel AddSink(this BlazorDiagram d, NedCatalog catalog,
                                        IReadOnlyList<GraphOutput> declared, double x = 600, double y = 75) =>
        d.Add(catalog, BuiltInIds.Output, x, y, declared);

    /// <summary>Vstupní port uzlu podle jména vstupu.</summary>
    public static PortModel Input(this DataNodeModel node, string inputName) =>
        node.Ports.First(p => node.Inputs.TryGetValue(p.Id, out var input) && input.Name == inputName);

    public static void Link(this BlazorDiagram d, DataNodeModel from, DataNodeModel to, string toPort) =>
        d.Links.Add(new LinkModel(from.Outputs[BuiltInIds.DefaultOutput], to.Input(toPort)));

    /// <summary>
    /// Kanonický testovací graf: <c>Number(2) → Add.A</c>, <c>Number(3) → Add.B</c>,
    /// <c>Add → Output.Result</c>. Pokrývá field hodnoty, porty, typované linky i sink.
    /// </summary>
    public static (BlazorDiagram diagram, GraphSettings settings) AddTwoNumbers(NedCatalog catalog)
    {
        var d = NewDiagram();
        var settings = new GraphSettings
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Two Numbers",
            Outputs = Outputs(TypeIds.Double),
        };

        var two = d.Add(catalog, Number, 0, 0, values: ("Value", 2d));
        var three = d.Add(catalog, Number, 0, 150, values: ("Value", 3d));
        var add = d.Add(catalog, AddNode, 300, 75);
        var output = d.AddSink(catalog, settings.Outputs);

        d.Link(two, add, "A");
        d.Link(three, add, "B");
        d.Link(add, output, Result);

        return (d, settings);
    }
}
