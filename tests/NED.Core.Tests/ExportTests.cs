using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using NED.Core.Persistence;
using System.Text.Json;

namespace NED.Core.Tests;

/// <summary>
/// Export je smlouva s runtimem, který data konzumuje. Snapshot proto musí zůstat
/// byte-identický i po přechodu na manifest — mění se způsob čtení metadat, ne výstup.
/// </summary>
public class ExportTests
{
    [Fact]
    public void AddTwoNumbers_MatchesSnapshot()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);

        var json = GraphExporter.Export(diagram, settings, catalog: catalog);

        Snapshot.Match(json, "export-add-two-numbers");
    }

    [Fact]
    public void SharedNode_IsDefinedOnceAndThenReferenced()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var number = diagram.Add(catalog, TestGraph.Number, values: ("Value", 3d));
        var add = diagram.Add(catalog, TestGraph.AddNode);
        diagram.Links.Add(new LinkModel(number.Outputs[BuiltInIds.DefaultOutput], add.Input("A")));
        diagram.Links.Add(new LinkModel(number.Outputs[BuiltInIds.DefaultOutput], add.Input("B")));
        var output = diagram.Add(catalog, TestGraph.Output);
        diagram.Link(add, output, TestGraph.Result);

        using var export = ParseExport(diagram, catalog);
        var exportedAdd = ExportedValue(export);
        var first = exportedAdd.GetProperty("A");
        var second = exportedAdd.GetProperty("B");

        Assert.Equal(first.GetProperty("$id").GetString(), second.GetProperty("$ref").GetString());
        Assert.False(second.TryGetProperty("$type", out _));
    }

    [Fact]
    public void SequentialIds_AreDeterministicAcrossExports()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);

        var first = GraphExporter.Export(diagram, settings, catalog: catalog);
        var second = GraphExporter.Export(diagram, settings, catalog: catalog);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentOutputsOfSharedNode_KeepOutputSelectionOnReference()
    {
        var catalog = MultiOutputExportCatalog();
        var diagram = TestGraph.NewDiagram();
        var producer = diagram.Add(catalog, "flow/Multi");
        var add = diagram.Add(catalog, TestGraph.AddNode);
        diagram.Links.Add(new LinkModel(producer.Outputs["Count"], add.Input("A")));
        diagram.Links.Add(new LinkModel(producer.Outputs[BuiltInIds.DefaultOutput], add.Input("B")));
        var output = diagram.Add(catalog, TestGraph.Output);
        diagram.Link(add, output, TestGraph.Result);

        using var export = ParseExport(diagram, catalog);
        var exportedAdd = ExportedValue(export);
        var definition = exportedAdd.GetProperty("A");
        var reference = exportedAdd.GetProperty("B");

        Assert.Equal("Count", definition.GetProperty("$output").GetString());
        Assert.Equal(definition.GetProperty("$id").GetString(), reference.GetProperty("$ref").GetString());
        Assert.False(reference.TryGetProperty("$output", out _));
    }

    [Fact]
    public void DataCycle_UsesReferenceToVisitingNode()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var first = diagram.Add(catalog, TestGraph.AddNode);
        var second = diagram.Add(catalog, TestGraph.AddNode);
        diagram.Links.Add(new LinkModel(first.Outputs[BuiltInIds.DefaultOutput], second.Input("A")));
        diagram.Links.Add(new LinkModel(second.Outputs[BuiltInIds.DefaultOutput], first.Input("A")));
        var output = diagram.Add(catalog, TestGraph.Output);
        diagram.Link(first, output, TestGraph.Result);

        using var export = ParseExport(diagram, catalog);
        var root = ExportedValue(export);
        var cycle = root.GetProperty("A").GetProperty("A");

        Assert.Equal(root.GetProperty("$id").GetString(), cycle.GetProperty("$ref").GetString());
        Assert.True(cycle.GetProperty("$cycle").GetBoolean());
        Assert.False(cycle.TryGetProperty("$type", out _));
    }

    [Fact]
    public void Literal_DoesNotReceiveIdentity()
    {
        var catalog = LiteralExportCatalog();
        var diagram = TestGraph.NewDiagram();
        var producer = diagram.Add(catalog, "flow/LiteralHolder", values: ("Value", "choice"));
        var output = diagram.Add(catalog, TestGraph.Output);
        diagram.Link(producer, output, TestGraph.Result);

        using var export = ParseExport(diagram, catalog);
        var literal = ExportedValue(export).GetProperty("Value");

        Assert.Equal("$literal", literal.GetProperty("$type").GetString());
        Assert.False(literal.TryGetProperty("$id", out _));
    }

    [Fact]
    public void SameLocalNameFromTwoPacks_ExportsFullIdsAndPackVersions()
    {
        var catalog = new NedCatalog(new[]
        {
            Pack("alpha", "1.2.0", "alpha/Add"),
            Pack("beta", "2.0.0", "beta/Add"),
        });
        var diagram = TestGraph.NewDiagram();
        var alpha = diagram.Add(catalog, "alpha/Add");
        var beta = diagram.Add(catalog, "beta/Add");
        var output = diagram.Add(catalog, TestGraph.Output);
        diagram.Link(beta, alpha, "Value");
        diagram.Link(alpha, output, TestGraph.Result);

        using var export = JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, catalog: catalog));

        Assert.Equal(ExportModel.CurrentVersion, export.RootElement.GetProperty("exportVersion").GetInt32());
        var root = export.RootElement.GetProperty("outputs")[0].GetProperty("value");
        Assert.Equal("alpha/Add", root.GetProperty("$type").GetString());
        Assert.Equal("beta/Add", root.GetProperty("Value").GetProperty("$type").GetString());

        var packs = export.RootElement.GetProperty("packs").EnumerateArray().ToList();
        Assert.Collection(packs,
            alpha =>
            {
                Assert.Equal("alpha", alpha.GetProperty("id").GetString());
                Assert.Equal("1.2.0", alpha.GetProperty("version").GetString());
            },
            beta =>
            {
                Assert.Equal("beta", beta.GetProperty("id").GetString());
                Assert.Equal("2.0.0", beta.GetProperty("version").GetString());
            });
    }

    private static NodeManifest Pack(string id, string version, string typeId) => new()
    {
        Pack = new PackInfo { Id = id, Version = version },
        Types =
        {
            new NodeTypeDescriptor
            {
                Id = typeId,
                Name = "Add",
                Outputs = { new NodeOutputDescriptor { Type = TypeIds.Double } },
                Inputs =
                {
                    new NodeInputDescriptor
                    {
                        Name = "Value", Label = "Value", Kind = InputKind.Port,
                        Type = TypeIds.Double, Optional = true,
                    },
                },
            },
        },
    };

    /// <summary>Subgraf se při exportu inlinuje — hranice zmizí, výstup je plochý strom.</summary>
    [Fact]
    public void Subgraph_IsInlined()
    {
        using var lib = new TempLibrary();

        var innerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var inner = TempLibrary.SubgraphReferencing(innerId, null);
        // Tělo subgrafu: Number(7) → Output
        inner.Nodes.Add(new GraphNodeDto
        {
            Id = "num",
            TypeName = TestGraph.Number,
            Fields = { ["Value"] = 7d },
        });
        inner.Links.Add(new GraphLinkDto
        {
            FromNode = "num", FromPort = BuiltInIds.DefaultOutput,
            ToNode = "out", ToPort = TestGraph.Result,
        });
        lib.Write("inner", inner);

        var diagram = TestGraph.NewDiagram();
        var sgNode = new SubgraphNodeModel(lib.Index.Resolve(innerId)!, new Point(0, 0));
        diagram.Nodes.Add(sgNode);
        var output = diagram.Add(lib.Catalog, TestGraph.Output, 400, 0);
        diagram.Links.Add(new LinkModel(
            sgNode.Outputs[BuiltInIds.DefaultOutput], output.Input(TestGraph.Result)));

        var settings = new GraphSettings
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Outputs = TestGraph.Outputs(TypeIds.Double),
        };

        var json = GraphExporter.Export(diagram, settings, lib.Index, lib.Catalog);

        Snapshot.Match(json, "export-subgraph-inlined");
    }

    [Fact]
    public void SameSubgraphNodeUsedTwice_IsDefinedOnceAndReferenced()
    {
        using var lib = new TempLibrary();
        var innerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var inner = ConstantSubgraph(innerId, 7d);
        lib.Write("shared-inner", inner);

        var diagram = TestGraph.NewDiagram();
        var subgraph = new SubgraphNodeModel(lib.Index.Resolve(innerId)!, new Point(0, 0));
        diagram.Nodes.Add(subgraph);
        var add = diagram.Add(lib.Catalog, TestGraph.AddNode);
        diagram.Links.Add(new LinkModel(subgraph.Outputs[BuiltInIds.DefaultOutput], add.Input("A")));
        diagram.Links.Add(new LinkModel(subgraph.Outputs[BuiltInIds.DefaultOutput], add.Input("B")));
        var output = diagram.Add(lib.Catalog, TestGraph.Output);
        diagram.Link(add, output, TestGraph.Result);

        using var export = JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, lib.Index, lib.Catalog));
        var exportedAdd = ExportedValue(export);

        Assert.Equal(exportedAdd.GetProperty("A").GetProperty("$id").GetString(),
            exportedAdd.GetProperty("B").GetProperty("$ref").GetString());
    }

    /// <summary>
    /// Parametr subgrafu navázaný na vnořený subgraf drží hotový strom (PrecomputedTree).
    /// Konzumují-li ho uvnitř těla dva vstupy, nesmí se ten strom vydat dvakrát — jinak
    /// je v exportu jedno $id na dvou místech.
    /// </summary>
    [Fact]
    public void ParameterBoundToSubgraph_IsDefinedOnceEvenWhenUsedTwice()
    {
        using var lib = new TempLibrary();
        var constantId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
        var hostId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");
        lib.Write("constant", ConstantSubgraph(constantId, 7d));
        lib.Write("param-twice", ParameterUsedTwiceSubgraph(hostId));

        var diagram = TestGraph.NewDiagram();
        var subgraph = new SubgraphNodeModel(lib.Index.Resolve(hostId)!, new Point(0, 0));
        subgraph.FieldValues["X"] = "sg:" + constantId;
        diagram.Nodes.Add(subgraph);
        var output = diagram.Add(lib.Catalog, TestGraph.Output);
        diagram.Links.Add(new LinkModel(
            subgraph.Outputs[BuiltInIds.DefaultOutput], output.Input(TestGraph.Result)));

        using var export = JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, lib.Index, lib.Catalog));
        var exportedAdd = ExportedValue(export);

        Assert.Equal(exportedAdd.GetProperty("A").GetProperty("$id").GetString(),
            exportedAdd.GetProperty("B").GetProperty("$ref").GetString());
        AssertUniqueIds(export.RootElement);
    }

    /// <summary>
    /// Parametr grafu → port SubgraphNode → GraphInput uvnitř těla. Skutečný binding
    /// (producent v rodiči) musí vyhrát nad vysypáním builtin uzlu a doputovat až na $param.
    /// </summary>
    [Fact]
    public void GraphParameterFeedingSubgraph_ArrivesAsParamMarker()
    {
        using var lib = new TempLibrary();
        var innerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006");
        lib.Write("port-bound", PortBoundSubgraph(innerId));

        var diagram = TestGraph.NewDiagram();
        var subgraph = new SubgraphNodeModel(lib.Index.Resolve(innerId)!, new Point(0, 0));
        diagram.Nodes.Add(subgraph);
        var parameter = diagram.Add(lib.Catalog, BuiltInIds.GraphInput, 0, 0, null,
            (BuiltInIds.GraphInputName, "Ticket"),
            (BuiltInIds.GraphInputTypeName, TypeIds.Double));
        diagram.Links.Add(new LinkModel(
            parameter.Outputs[BuiltInIds.DefaultOutput], subgraph.InputPorts["X"]));
        var output = diagram.Add(lib.Catalog, TestGraph.Output);
        diagram.Links.Add(new LinkModel(
            subgraph.Outputs[BuiltInIds.DefaultOutput], output.Input(TestGraph.Result)));

        using var export = JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, lib.Index, lib.Catalog));
        var inlinedAdd = ExportedValue(export);

        Assert.Equal("Ticket", inlinedAdd.GetProperty("A").GetProperty("$param").GetString());
        Assert.Equal(TestGraph.Number, inlinedAdd.GetProperty("B").GetProperty("$type").GetString());
        Assert.Equal("Ticket", export.RootElement.GetProperty("inputs")[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// Subgraf se dvěma Output uzly vystaví dva porty a inline vrátí strom TOHO výstupu,
    /// který volající konzumuje. Jméno portu je Label uzlu.
    /// </summary>
    [Fact]
    public void SubgraphWithTwoOutputs_ExposesBothAndInlinesTheConsumedOne()
    {
        using var lib = new TempLibrary();
        var innerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007");
        lib.Write("two-outputs", TwoOutputSubgraph(innerId));
        var asset = lib.Index.Resolve(innerId)!;

        Assert.Equal(new[] { "Doubled", "Raw" }, asset.Interface.Outputs.Select(o => o.Name));

        var diagram = TestGraph.NewDiagram();
        var subgraph = new SubgraphNodeModel(asset, new Point(0, 0));
        diagram.Nodes.Add(subgraph);
        Assert.Equal(new[] { "Doubled", "Raw" }, subgraph.Outputs.Keys);

        var output = diagram.Add(lib.Catalog, TestGraph.Output);
        diagram.Links.Add(new LinkModel(
            subgraph.Outputs["Raw"], output.Input(TestGraph.Result)));

        using var export = JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, lib.Index, lib.Catalog));

        // Raw vede na konstantu 5; Doubled na Add. Kdyby se vzal špatný výstup, byl by tu Add.
        Assert.Equal(TestGraph.Number, ExportedValue(export).GetProperty("$type").GetString());
        Assert.Equal(5d, ExportedValue(export).GetProperty("Value").GetDouble());
    }

    [Fact]
    public void DifferentInstancesOfSameSubgraph_KeepSeparateBindingContexts()
    {
        using var lib = new TempLibrary();
        var innerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
        lib.Write("bound-inner", BoundSubgraph(innerId));
        var asset = lib.Index.Resolve(innerId)!;

        var diagram = TestGraph.NewDiagram();
        var first = new SubgraphNodeModel(asset, new Point(0, 0));
        var second = new SubgraphNodeModel(asset, new Point(0, 100));
        first.FieldValues["X"] = "2";
        second.FieldValues["X"] = "3";
        diagram.Nodes.Add(first);
        diagram.Nodes.Add(second);
        var add = diagram.Add(lib.Catalog, TestGraph.AddNode);
        diagram.Links.Add(new LinkModel(first.Outputs[BuiltInIds.DefaultOutput], add.Input("A")));
        diagram.Links.Add(new LinkModel(second.Outputs[BuiltInIds.DefaultOutput], add.Input("B")));
        var output = diagram.Add(lib.Catalog, TestGraph.Output);
        diagram.Link(add, output, TestGraph.Result);

        using var export = JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, lib.Index, lib.Catalog));
        var exportedAdd = ExportedValue(export);
        var firstTree = exportedAdd.GetProperty("A");
        var secondTree = exportedAdd.GetProperty("B");

        Assert.NotEqual(firstTree.GetProperty("$id").GetString(), secondTree.GetProperty("$id").GetString());
        Assert.Equal(2d, firstTree.GetProperty("A").GetProperty("value").GetDouble());
        Assert.Equal(3d, secondTree.GetProperty("A").GetProperty("value").GetDouble());
    }

    /// <summary>
    /// Vzájemně se odkazující subgrafy (A→B→A) nesmí export zacyklit ani přetéct zásobník —
    /// guardStack cyklus označí a vrátí se.
    /// </summary>
    [Fact]
    public void SubgraphCycle_IsReportedNotHung()
    {
        using var lib = new TempLibrary();

        var idA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000a");
        var idB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
        lib.Write("a", TempLibrary.SubgraphReferencing(idA, idB));
        lib.Write("b", TempLibrary.SubgraphReferencing(idB, idA));

        var diagram = TestGraph.NewDiagram();
        var sgNode = new SubgraphNodeModel(lib.Index.Resolve(idA)!, new Point(0, 0));
        diagram.Nodes.Add(sgNode);
        var output = diagram.Add(lib.Catalog, TestGraph.Output, 400, 0);
        diagram.Links.Add(new LinkModel(
            sgNode.Outputs[BuiltInIds.DefaultOutput], output.Input(TestGraph.Result)));

        var settings = new GraphSettings { Id = Guid.NewGuid(), Outputs = TestGraph.Outputs(TypeIds.Double) };

        var json = GraphExporter.Export(diagram, settings, lib.Index, lib.Catalog);

        Assert.Contains("cycle", json);
    }

    private static JsonDocument ParseExport(Blazor.Diagrams.BlazorDiagram diagram, NedCatalog catalog) =>
        JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, catalog: catalog));

    private static JsonElement ExportedValue(JsonDocument export) =>
        export.RootElement.GetProperty("outputs")[0].GetProperty("value");

    private static NedCatalog MultiOutputExportCatalog()
    {
        var sandbox = NED.Manifest.Generator.ManifestBuilder.Build(typeof(Sandbox.Add).Assembly, out _);
        var flow = new NodeManifest
        {
            Pack = new PackInfo { Id = "flow" },
            Types =
            {
                new NodeTypeDescriptor
                {
                    Id = "flow/Multi", Name = "Multi",
                    Outputs =
                    {
                        new NodeOutputDescriptor { Name = "Count", Type = TypeIds.Double },
                        new NodeOutputDescriptor { Name = BuiltInIds.DefaultOutput, Type = TypeIds.Double },
                    },
                },
            },
        };
        return new NedCatalog(new[] { sandbox, flow });
    }

    private static NedCatalog LiteralExportCatalog()
    {
        var sandbox = NED.Manifest.Generator.ManifestBuilder.Build(typeof(Sandbox.Add).Assembly, out _);
        var flow = new NodeManifest
        {
            Pack = new PackInfo { Id = "flow" },
            Types =
            {
                new NodeTypeDescriptor { Id = "flow/Choice", Name = "Choice" },
                new NodeTypeDescriptor
                {
                    Id = "flow/LiteralHolder", Name = "Literal holder",
                    Inputs =
                    {
                        new NodeInputDescriptor
                        {
                            Name = "Value", Label = "Value", Kind = InputKind.Field,
                            Type = "flow/Choice",
                        },
                    },
                    Outputs =
                    {
                        new NodeOutputDescriptor { Name = BuiltInIds.DefaultOutput, Type = TypeIds.Double },
                    },
                },
            },
        };
        return new NedCatalog(new[] { sandbox, flow });
    }

    private static GraphDocument ConstantSubgraph(Guid id, double value)
    {
        var doc = TempLibrary.SubgraphReferencing(id, null);
        doc.Nodes.Add(new GraphNodeDto
        {
            Id = "number", TypeName = TestGraph.Number, Fields = { ["Value"] = value },
        });
        doc.Links.Add(new GraphLinkDto
        {
            FromNode = "number", FromPort = BuiltInIds.DefaultOutput,
            ToNode = "out", ToPort = TestGraph.Result,
        });
        return doc;
    }

    /// <summary>Subgraf, jehož jediný parametr X pohání oba vstupy jednoho Add.</summary>
    private static GraphDocument ParameterUsedTwiceSubgraph(Guid id)
    {
        var doc = TempLibrary.SubgraphReferencing(id, null);
        doc.Nodes.AddRange(new[]
        {
            new GraphNodeDto
            {
                Id = "input", TypeName = BuiltInIds.GraphInput,
                Fields =
                {
                    [BuiltInIds.GraphInputName] = "X",
                    [BuiltInIds.GraphInputTypeName] = TypeIds.Double,
                    [BuiltInIds.GraphInputExposure] = nameof(InputExposure.Field),
                    [BuiltInIds.GraphInputDefault] = "0",
                },
            },
            new GraphNodeDto { Id = "add", TypeName = TestGraph.AddNode },
        });
        doc.Links.AddRange(new[]
        {
            new GraphLinkDto { FromNode = "input", FromPort = BuiltInIds.DefaultOutput, ToNode = "add", ToPort = "A" },
            new GraphLinkDto { FromNode = "input", FromPort = BuiltInIds.DefaultOutput, ToNode = "add", ToPort = "B" },
            new GraphLinkDto
            {
                FromNode = "add", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "out", ToPort = TestGraph.Result,
            },
        });
        return doc;
    }

    /// <summary>Žádné $id se v exportu nesmí objevit dvakrát — jinak $ref míří na dvě místa.</summary>
    private static void AssertUniqueIds(JsonElement root)
    {
        var ids = new List<string>();
        Collect(root, ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        static void Collect(JsonElement element, List<string> ids)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (element.TryGetProperty("$id", out var id) && id.GetString() is { } value) ids.Add(value);
                    foreach (var property in element.EnumerateObject()) Collect(property.Value, ids);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Collect(item, ids);
                    break;
            }
        }
    }

    /// <summary>Subgraf vracející dvě hodnoty: „Raw" konstantu a „Doubled" její součet.</summary>
    private static GraphDocument TwoOutputSubgraph(Guid id)
    {
        var doc = TempLibrary.SubgraphReferencing(id, null);
        // Dvě deklarace, jeden sink — porty na něm se jmenují po nich.
        doc.Settings.Outputs = new List<GraphOutputDto>
        {
            new() { Name = "Doubled", Type = TypeIds.Double },
            new() { Name = "Raw", Type = TypeIds.Double },
        };
        doc.Nodes.AddRange(new[]
        {
            new GraphNodeDto { Id = "number", TypeName = TestGraph.Number, Fields = { ["Value"] = 5d } },
            new GraphNodeDto { Id = "add", TypeName = TestGraph.AddNode },
        });
        doc.Links.AddRange(new[]
        {
            new GraphLinkDto { FromNode = "number", FromPort = BuiltInIds.DefaultOutput, ToNode = "add", ToPort = "A" },
            new GraphLinkDto { FromNode = "number", FromPort = BuiltInIds.DefaultOutput, ToNode = "add", ToPort = "B" },
            new GraphLinkDto
            {
                FromNode = "add", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "out", ToPort = "Doubled",
            },
            new GraphLinkDto
            {
                FromNode = "number", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "out", ToPort = "Raw",
            },
        });
        return doc;
    }

    /// <summary>Jako <see cref="BoundSubgraph"/>, ale parametr je vystavený jako port.</summary>
    private static GraphDocument PortBoundSubgraph(Guid id)
    {
        var doc = BoundSubgraph(id);
        doc.Nodes.Single(node => node.Id == "input")
            .Fields[BuiltInIds.GraphInputExposure] = nameof(InputExposure.Port);
        return doc;
    }

    private static GraphDocument BoundSubgraph(Guid id)
    {
        var doc = TempLibrary.SubgraphReferencing(id, null);
        doc.Nodes.AddRange(new[]
        {
            new GraphNodeDto
            {
                Id = "input", TypeName = BuiltInIds.GraphInput,
                Fields =
                {
                    [BuiltInIds.GraphInputName] = "X",
                    [BuiltInIds.GraphInputTypeName] = TypeIds.Double,
                    [BuiltInIds.GraphInputExposure] = nameof(InputExposure.Field),
                    [BuiltInIds.GraphInputDefault] = "0",
                },
            },
            new GraphNodeDto { Id = "number", TypeName = TestGraph.Number, Fields = { ["Value"] = 1d } },
            new GraphNodeDto { Id = "add", TypeName = TestGraph.AddNode },
        });
        doc.Links.AddRange(new[]
        {
            new GraphLinkDto
            {
                FromNode = "input", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "add", ToPort = "A",
            },
            new GraphLinkDto
            {
                FromNode = "number", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "add", ToPort = "B",
            },
            new GraphLinkDto
            {
                FromNode = "add", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "out", ToPort = TestGraph.Result,
            },
        });
        return doc;
    }
}
