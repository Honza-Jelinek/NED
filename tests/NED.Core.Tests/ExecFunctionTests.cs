using System.Text.Json;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Assets;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core.Tests;

/// <summary>
/// Exec subgraf je funkce: neinlinuje se, ale volá. Datový subgraf se pořád vlévá —
/// obojí musí platit vedle sebe.
/// </summary>
public sealed class ExecFunctionTests
{
    [Fact]
    public void ExecSubgraph_IsCalledNotInlined()
    {
        using var lib = new TempLibrary();
        var fnId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
        lib.Write("review-fn", ExecFunction(fnId));

        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(lib.Catalog, BuiltInIds.ExecEntry);
        var call = new SubgraphNodeModel(lib.Index.Resolve(fnId)!, new Point(0, 0));
        diagram.Nodes.Add(call);
        diagram.Links.Add(new LinkModel(
            entry.Outputs[BuiltInIds.ExecEntryOutput], call.ExecInput!));

        using var export = Export(diagram, lib);
        var root = export.RootElement;
        var nodes = root.GetProperty("nodes").EnumerateArray().ToList();
        var functions = root.GetProperty("functions").EnumerateArray().ToList();

        var callNode = nodes.Single(node => node.TryGetProperty("$call", out _));
        Assert.Equal(functions[0].GetProperty("id").GetString(), callNode.GetProperty("$call").GetString());

        // Tělo je ve functions, ne rozpuštěné mezi uzly volajícího.
        Assert.DoesNotContain(nodes, node =>
            node.TryGetProperty("$type", out var type) && type.GetString() == "sandbox/Add");
        Assert.Contains(functions[0].GetProperty("nodes").EnumerateArray(), node =>
            node.GetProperty("$type").GetString() == BuiltInIds.ExecEntry);
    }

    /// <summary>
    /// Uzel volané funkce má exec vstup a link do něj musí přežít uložení. Ten pin nežije
    /// v <c>InputPorts</c>, takže ho persistence musí znát zvlášť — jinak se link tiše ztratí.
    /// </summary>
    [Fact]
    public void ExecLinkIntoFunction_SurvivesRoundTrip()
    {
        using var lib = new TempLibrary();
        var fnId = Guid.Parse("cccccccc-0000-0000-0000-000000000005");
        lib.Write("saved-fn", ExecFunction(fnId));

        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(lib.Catalog, BuiltInIds.ExecEntry);
        var call = new SubgraphNodeModel(lib.Index.Resolve(fnId)!, new Point(0, 0));
        diagram.Nodes.Add(call);
        Assert.NotNull(call.ExecInput);
        diagram.Links.Add(new LinkModel(entry.Outputs[BuiltInIds.ExecEntryOutput], call.ExecInput!));

        var settings = new GraphSettings { Flow = GraphFlow.Exec };
        var saved = GraphPersistence.ToDocument(diagram, settings);
        var linkDto = Assert.Single(saved.Links);
        Assert.Equal(BuiltInIds.ExecInput, linkDto.ToPort);

        var reloaded = TestGraph.NewDiagram();
        GraphPersistence.LoadInto(
            reloaded, GraphPersistence.Deserialize(GraphPersistence.Serialize(saved))!,
            lib.Catalog, lib.Index);

        var reloadedCall = Assert.Single(reloaded.Nodes.OfType<SubgraphNodeModel>());
        Assert.Single(reloadedCall.ExecInput!.Links);
    }

    [Fact]
    public void TwoCallsOfSameFunction_ShareOneBody()
    {
        using var lib = new TempLibrary();
        var fnId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
        lib.Write("shared-fn", ExecFunction(fnId));
        var asset = lib.Index.Resolve(fnId)!;

        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(lib.Catalog, BuiltInIds.ExecEntry);
        var first = new SubgraphNodeModel(asset, new Point(0, 0));
        var second = new SubgraphNodeModel(asset, new Point(0, 200));
        diagram.Nodes.Add(first);
        diagram.Nodes.Add(second);
        diagram.Links.Add(new LinkModel(entry.Outputs[BuiltInIds.ExecEntryOutput], first.ExecInput!));
        diagram.Links.Add(new LinkModel(first.Outputs[BuiltInIds.ExecEntryOutput], second.ExecInput!));

        using var export = Export(diagram, lib);
        var root = export.RootElement;
        var calls = root.GetProperty("nodes").EnumerateArray()
            .Where(node => node.TryGetProperty("$call", out _)).ToList();

        Assert.Equal(2, calls.Count);
        Assert.Single(root.GetProperty("functions").EnumerateArray());
        Assert.Equal(calls[0].GetProperty("$call").GetString(), calls[1].GetProperty("$call").GetString());
    }

    /// <summary>
    /// Funkce volající sama sebe je právě to, kvůli čemu se neinlinuje. Nesmí skončit
    /// jako <c>$error: cycle</c> ani nekonečnou expanzí.
    /// </summary>
    [Fact]
    public void RecursiveFunction_ExportsWithoutCycleError()
    {
        using var lib = new TempLibrary();
        var fnId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        lib.Write("recursive-fn", RecursiveExecFunction(fnId));

        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(lib.Catalog, BuiltInIds.ExecEntry);
        var call = new SubgraphNodeModel(lib.Index.Resolve(fnId)!, new Point(0, 0));
        diagram.Nodes.Add(call);
        diagram.Links.Add(new LinkModel(entry.Outputs[BuiltInIds.ExecEntryOutput], call.ExecInput!));

        using var export = Export(diagram, lib);

        Assert.DoesNotContain("\"$error\"", export.RootElement.GetRawText());
        Assert.Single(export.RootElement.GetProperty("functions").EnumerateArray());
    }

    /// <summary>
    /// Pravidlo vkládání je asymetrické: hodnotu spočítat jde kdekoliv, proceduru ne.
    /// Datový subgraf tedy do exec grafu patří, exec subgraf do datového ne.
    /// </summary>
    [Fact]
    public void DataSubgraphFitsExecGraph_ButNotTheOtherWayAround()
    {
        var data = new SubgraphInterface { Flow = GraphFlow.Data };
        var exec = new SubgraphInterface { Flow = GraphFlow.Exec };

        Assert.True(data.CanBePlacedIn(GraphFlow.Data));
        Assert.True(data.CanBePlacedIn(GraphFlow.Exec));
        Assert.True(exec.CanBePlacedIn(GraphFlow.Exec));
        Assert.False(exec.CanBePlacedIn(GraphFlow.Data));
    }

    /// <summary>Datový subgraf uvnitř exec grafu se pořád vlévá — nestane se z něj volání.</summary>
    [Fact]
    public void DataSubgraphInsideExecGraph_IsInlinedNotCalled()
    {
        using var lib = new TempLibrary();
        var innerId = Guid.Parse("cccccccc-0000-0000-0000-000000000006");
        lib.Write("data-in-exec", DataSubgraph(innerId));

        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(lib.Catalog, BuiltInIds.ExecEntry);
        var branch = diagram.Add(lib.Catalog, "sandbox/Branch");
        var subgraph = new SubgraphNodeModel(lib.Index.Resolve(innerId)!, new Point(0, 0));
        diagram.Nodes.Add(subgraph);

        diagram.Links.Add(new LinkModel(entry.Outputs[BuiltInIds.ExecEntryOutput], branch.Input("In")));
        diagram.Links.Add(new LinkModel(
            subgraph.Outputs[BuiltInIds.DefaultOutput], branch.Input("Cond")));

        using var export = Export(diagram, lib);
        var root = export.RootElement;
        var branchNode = root.GetProperty("nodes").EnumerateArray()
            .Single(node => node.TryGetProperty("$type", out var t) && t.GetString() == "sandbox/Branch");

        Assert.False(root.TryGetProperty("functions", out _));
        Assert.Equal(TestGraph.Number, branchNode.GetProperty("Cond").GetProperty("$type").GetString());
    }

    /// <summary>Datový subgraf se pořád vlévá — volání je jen pro exec.</summary>
    [Fact]
    public void DataSubgraph_IsStillInlined()
    {
        using var lib = new TempLibrary();
        var innerId = Guid.Parse("cccccccc-0000-0000-0000-000000000004");
        lib.Write("data-sub", DataSubgraph(innerId));

        var diagram = TestGraph.NewDiagram();
        var subgraph = new SubgraphNodeModel(lib.Index.Resolve(innerId)!, new Point(0, 0));
        diagram.Nodes.Add(subgraph);
        Assert.Null(subgraph.ExecInput);

        var output = diagram.Add(lib.Catalog, TestGraph.Output);
        diagram.Links.Add(new LinkModel(
            subgraph.Outputs[BuiltInIds.DefaultOutput], output.Input(TestGraph.Result)));

        var json = GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, lib.Index, lib.Catalog);

        Assert.DoesNotContain("$call", json, StringComparison.Ordinal);
        Assert.DoesNotContain("functions", json, StringComparison.Ordinal);
        Assert.Contains("sandbox/NumberConstant", json, StringComparison.Ordinal);
    }

    private static JsonDocument Export(BlazorDiagram diagram, TempLibrary lib) =>
        JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Flow = GraphFlow.Exec }, lib.Index, lib.Catalog));

    /// <summary>Exec funkce: Entry → Branch, plus datový výstup „Passed".</summary>
    private static GraphDocument ExecFunction(Guid id)
    {
        var doc = ExecFunctionShell(id);
        doc.Nodes.Add(new GraphNodeDto { Id = "add", TypeName = TestGraph.AddNode });
        doc.Links.Add(new GraphLinkDto
        {
            FromNode = "add", FromPort = BuiltInIds.DefaultOutput,
            ToNode = "out", ToPort = TestGraph.Result,
        });
        return doc;
    }

    /// <summary>Táž funkce, ale její exec řetěz volá sám sebe.</summary>
    private static GraphDocument RecursiveExecFunction(Guid id)
    {
        var doc = ExecFunctionShell(id);
        doc.SubgraphNodes.Add(new SubgraphNodeDto { Id = "self", X = 200, Y = 0, SubgraphId = id.ToString() });
        doc.Links.Add(new GraphLinkDto
        {
            FromNode = "entry", FromPort = BuiltInIds.ExecEntryOutput,
            ToNode = "self", ToPort = BuiltInIds.ExecInput,
        });
        return doc;
    }

    private static GraphDocument ExecFunctionShell(Guid id) => new()
    {
        Settings = new GraphSettingsDto
        {
            Id = id.ToString(),
            Name = "fn-" + id.ToString("N")[..4],
            Flow = nameof(GraphFlow.Exec),
            Outputs = TestGraph.Declare(TypeIds.Double),
        },
        Nodes =
        {
            new GraphNodeDto { Id = "entry", TypeName = BuiltInIds.ExecEntry },
            new GraphNodeDto
            {
                Id = "out", X = 400, TypeName = BuiltInIds.Output,
            },
        },
    };

    private static GraphDocument DataSubgraph(Guid id)
    {
        var doc = TempLibrary.SubgraphReferencing(id, null);
        doc.Nodes.Add(new GraphNodeDto
        {
            Id = "number", TypeName = TestGraph.Number, Fields = { ["Value"] = 4d },
        });
        doc.Links.Add(new GraphLinkDto
        {
            FromNode = "number", FromPort = BuiltInIds.DefaultOutput,
            ToNode = "out", ToPort = TestGraph.Result,
        });
        return doc;
    }
}
