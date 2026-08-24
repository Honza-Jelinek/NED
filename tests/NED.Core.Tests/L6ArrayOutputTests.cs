using System.Text.Json;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Assets;
using NED.Core.Manifest;
using NED.Core.Persistence;
using NED.Manifest.Generator;

namespace NED.Core.Tests;

public sealed class L6ArrayOutputTests
{
    [Fact]
    public void GraphInput_ListSettingUpdatesItsOutputPort()
    {
        var catalog = ArrayCatalog();
        var node = new DataNodeModel(catalog.Resolve(BuiltInIds.GraphInput)!, catalog: catalog);
        var setting = node.InputDefs.Single(input => input.Name == BuiltInIds.GraphInputMultiple);

        NodeFieldIO.Write(node, setting, true, null, null);

        var output = node.Outputs[BuiltInIds.DefaultOutput];
        Assert.True(output.Multiple);
        Assert.EndsWith("double[]", output.TypeLine);
    }

    [Fact]
    public void MultipleOutputDescriptor_CreatesMultiplePortAndArrayTooltip()
    {
        var catalog = ArrayCatalog();
        var node = new DataNodeModel(catalog.Resolve(ListSource)!, catalog: catalog);
        var port = node.Outputs[BuiltInIds.DefaultOutput];

        Assert.True(port.Multiple);
        Assert.EndsWith("double[]", port.TypeLine);
    }

    [Fact]
    public void ListDeclaration_GivesSinkAnArrayPort()
    {
        var catalog = ArrayCatalog();
        var declared = new List<GraphOutput>
        {
            new() { Name = TestGraph.Result, Type = TypeIds.Double, Multiple = true },
        };
        var sink = new DataNodeModel(catalog.Resolve(BuiltInIds.Output)!, catalog: catalog);

        sink.SyncDeclaredInputs(declared);

        var value = sink.InputDefs.Single(input => input.Name == TestGraph.Result);
        Assert.True(value.Multiple);
        Assert.True(value.Port?.Multiple);
        Assert.EndsWith("double[]", value.Port?.TypeLine);
    }

    [Fact]
    public void Compatibility_AllowsEveryArityCombinationExceptListToScalar()
    {
        var parent = new DataNodeModel(new NodeTypeDescriptor { Id = "test/Parent" });

        Assert.True(Compatible(parent, outputMultiple: false, inputMultiple: false));
        Assert.True(Compatible(parent, outputMultiple: false, inputMultiple: true));
        Assert.False(Compatible(parent, outputMultiple: true, inputMultiple: false));
        Assert.True(Compatible(parent, outputMultiple: true, inputMultiple: true));
    }

    [Fact]
    public void Export_MixedScalarAndListSources_MarksOnlyListSourceAsSpread()
    {
        var (diagram, catalog, collector) = ArrayInputGraph(includeScalar: true);

        using var export = Export(diagram, catalog);
        var items = ExportedCollector(export, collector.TypeId).GetProperty("Items").EnumerateArray().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(ScalarSource, items[0].GetProperty("$type").GetString());
        Assert.False(items[0].TryGetProperty("$spread", out _));
        Assert.Equal(ListSource, items[1].GetProperty("$spread").GetProperty("$type").GetString());
    }

    [Fact]
    public void Export_SingleListSource_ProducesOneSpreadElementNotNestedArray()
    {
        var (diagram, catalog, collector) = ArrayInputGraph(includeScalar: false);

        using var export = Export(diagram, catalog);
        var items = ExportedCollector(export, collector.TypeId).GetProperty("Items");
        var item = Assert.Single(items.EnumerateArray());

        Assert.Equal(JsonValueKind.Object, item.ValueKind);
        Assert.Equal(ListSource, item.GetProperty("$spread").GetProperty("$type").GetString());
    }

    [Fact]
    public void SubgraphListOutput_PortAndLinkSurviveCallerRoundTrip()
    {
        using var library = new TempLibrary();
        var subgraphId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
        var subgraphDocument = TempLibrary.SubgraphReferencing(subgraphId, null);
        subgraphDocument.Settings.Outputs!.Single().Multiple = true;
        library.Write("list-output", subgraphDocument);

        var asset = library.Index.Resolve(subgraphId)!;
        Assert.True(Assert.Single(asset.Interface.Outputs).Multiple);

        var settings = new GraphSettings
        {
            Outputs = { new GraphOutput { Name = TestGraph.Result, Type = TypeIds.Double, Multiple = true } },
        };
        var diagram = TestGraph.NewDiagram();
        var instance = new SubgraphNodeModel(asset, new Point(0, 0));
        diagram.Nodes.Add(instance);
        var collector = diagram.AddSink(library.Catalog, settings.Outputs, 300, 0);
        diagram.Links.Add(new LinkModel(
            instance.Outputs[BuiltInIds.DefaultOutput], collector.Input(TestGraph.Result)));

        var saved = GraphPersistence.ToDocument(diagram, settings);
        var loaded = TestGraph.NewDiagram();
        GraphPersistence.LoadInto(loaded, saved, library.Catalog, library.Index);

        var loadedInstance = Assert.Single(loaded.Nodes.OfType<SubgraphNodeModel>());
        Assert.True(loadedInstance.Outputs[BuiltInIds.DefaultOutput].Multiple);
        Assert.Single(loaded.Links);
    }

    [Fact]
    public void ReturnNode_InheritsDeclaredOutputArity()
    {
        var catalog = ArrayCatalog();
        var declared = new List<GraphOutput>
        {
            new() { Name = "Items", Type = TypeIds.Double, Multiple = true },
        };
        var diagram = TestGraph.NewDiagram();

        var returnNode = diagram.Add(catalog, BuiltInIds.Return, declared: declared);

        var input = returnNode.InputDefs.Single(candidate => candidate.Name == "Items");
        Assert.True(input.Descriptor.Multiple);
        Assert.True(input.Multiple);
        Assert.True(input.Port?.Multiple);
    }

    [Fact]
    public void ExecFunctionDeclaration_CarriesOutputArity()
    {
        using var library = new TempLibrary();
        var functionId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000003");
        library.Write("list-function", new GraphDocument
        {
            Settings = new GraphSettingsDto
            {
                Id = functionId.ToString(),
                Name = "list-function",
                Flow = nameof(GraphFlow.Exec),
                Outputs = new() { new GraphOutputDto { Name = TestGraph.Result, Type = TypeIds.Double, Multiple = true } },
            },
            Nodes = { new GraphNodeDto { Id = "entry", TypeName = BuiltInIds.ExecEntry } },
        });

        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(library.Catalog, BuiltInIds.ExecEntry);
        var call = new SubgraphNodeModel(library.Index.Resolve(functionId)!, new Point(200, 0));
        diagram.Nodes.Add(call);
        diagram.Links.Add(new LinkModel(entry.Outputs[BuiltInIds.ExecEntryOutput], call.ExecInput!));

        using var export = JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Flow = GraphFlow.Exec }, library.Index, library.Catalog));
        var declaration = Assert.Single(export.RootElement.GetProperty("functions")[0]
            .GetProperty("outputs").EnumerateArray());

        Assert.True(declaration.GetProperty("multiple").GetBoolean());
    }

    [Fact]
    public void RebuildFromInterface_UpdatesOutputArityInPlace()
    {
        var id = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");
        var node = new SubgraphNodeModel(Asset(id, multiple: false));
        var original = node.Outputs[BuiltInIds.DefaultOutput];

        node.RebuildFromInterface(Asset(id, multiple: true).Interface);

        Assert.Same(original, node.Outputs[BuiltInIds.DefaultOutput]);
        Assert.True(original.Multiple);
    }

    [Fact]
    public void InlinedListSubgraph_KeepsEveryElement()
    {
        using var lib = new TempLibrary();
        var subId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000003");
        lib.Write("list-sub", ListSubgraph(subId));

        var settings = new GraphSettings
        {
            Outputs = { new GraphOutput { Name = TestGraph.Result, Type = TypeIds.Double, Multiple = true } },
        };
        var diagram = TestGraph.NewDiagram();
        var call = new SubgraphNodeModel(lib.Index.Resolve(subId)!, new Point(0, 0));
        diagram.Nodes.Add(call);
        var sink = diagram.AddSink(lib.Catalog, settings.Outputs);
        diagram.Links.Add(new LinkModel(
            call.Outputs[BuiltInIds.DefaultOutput], sink.Input(TestGraph.Result)));

        using var export = JsonDocument.Parse(
            GraphExporter.Export(diagram, settings, lib.Index, lib.Catalog));

        // Inline rusi hranici, ale ne aritu: brat jen prvniho producenta by tise zahodilo
        // zbytek a konzument by pritom dostal neco, co povazuje za pole.
        var element = Assert.Single(export.RootElement.GetProperty("outputs")[0]
            .GetProperty("value").EnumerateArray());
        Assert.Equal(new[] { 10d, 20d }, element.GetProperty("$spread").GetProperty("$list")
            .EnumerateArray().Select(node => node.GetProperty("Value").GetDouble()));
    }

    [Fact]
    public void TurningListOff_DropsSurplusLinks()
    {
        var catalog = TestGraph.Catalog();
        var declared = new List<GraphOutput>
        {
            new() { Name = TestGraph.Result, Type = TypeIds.Double, Multiple = true },
        };
        var diagram = TestGraph.NewDiagram();
        var one = diagram.Add(catalog, TestGraph.Number, values: ("Value", 1d));
        var two = diagram.Add(catalog, TestGraph.Number, values: ("Value", 2d));
        var sink = diagram.AddSink(catalog, declared);
        diagram.Link(one, sink, TestGraph.Result);
        diagram.Link(two, sink, TestGraph.Result);
        Assert.Equal(2, diagram.Links.Count);

        declared[0].Multiple = false;
        sink.SyncDeclaredInputs(declared);

        // Typova kontrola aritu vstupu neporovnava, takze by prebytek zustal viset
        // a export by z nej tise vzal jen prvni drat.
        Assert.Single(diagram.Links);
        Assert.Contains(
            GraphValidator.Validate(diagram, null, new GraphSettings { Outputs = declared }),
            issue => issue.MessageKey == "Validation_Orphan" && issue.Node == two);
    }

    [Fact]
    public void PickerFilters_RespectArityOnBothSides()
    {
        var catalog = ArrayCatalog();
        var collector = catalog.Resolve(Collector)!;         // vstup Items je pole
        var scalarSink = catalog.Resolve(TestGraph.AddNode)!; // vstupy A/B jsou skaláry
        var listSource = catalog.Resolve(ListSource)!;        // výstup je pole
        var scalarSource = catalog.Resolve(ScalarSource)!;

        // Tažení z polového výstupu: uzel se skalárním vstupem nabídnout nelze, link by se
        // nedotáhl a na plátně by zůstal osamocený uzel.
        Assert.True(AcceptsFrom(collector, outputMultiple: true));
        Assert.False(AcceptsFrom(scalarSink, outputMultiple: true));
        Assert.True(AcceptsFrom(scalarSink, outputMultiple: false));

        // Tažení ze skalárního vstupu: producent pole se nabídnout nesmí.
        Assert.False(ProducesInto(listSource, inputMultiple: false));
        Assert.True(ProducesInto(listSource, inputMultiple: true));
        Assert.True(ProducesInto(scalarSource, inputMultiple: false));
    }

    private static bool AcceptsFrom(NodeTypeDescriptor type, bool outputMultiple) =>
        NedCatalog.InputPorts(type).Any(port =>
            TypedPortModel.ArityFits(outputMultiple, port.Multiple)
            && TypeIds.IsCompatible(TypeIds.Double, Array.Empty<string>(), port.Type));

    private static bool ProducesInto(NodeTypeDescriptor type, bool inputMultiple) =>
        NedCatalog.OutputPorts(type).Any(port =>
            TypedPortModel.ArityFits(port.Multiple, inputMultiple)
            && TypeIds.IsCompatible(port.Type, Array.Empty<string>(), TypeIds.Double));

    private static GraphDocument ListSubgraph(Guid id) => new()
    {
        Settings = new GraphSettingsDto
        {
            Id = id.ToString(), Name = "list-sub",
            Outputs = new() { new GraphOutputDto { Name = TestGraph.Result, Type = TypeIds.Double, Multiple = true } },
        },
        Nodes =
        {
            new GraphNodeDto { Id = "out", TypeName = BuiltInIds.Output },
            new GraphNodeDto { Id = "a", TypeName = TestGraph.Number, Fields = { ["Value"] = 10d } },
            new GraphNodeDto { Id = "b", TypeName = TestGraph.Number, Fields = { ["Value"] = 20d } },
        },
        Links =
        {
            new GraphLinkDto
            {
                FromNode = "a", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "out", ToPort = TestGraph.Result,
            },
            new GraphLinkDto
            {
                FromNode = "b", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "out", ToPort = TestGraph.Result,
            },
        },
    };

    private static bool Compatible(DataNodeModel parent, bool outputMultiple, bool inputMultiple)
    {
        var output = new TypedPortModel(parent, PortAlignment.Right, TypeIds.Double)
            { Multiple = outputMultiple };
        var input = new TypedPortModel(parent, PortAlignment.Left, TypeIds.Double)
            { Multiple = inputMultiple };
        return TypedPortModel.IsCompatible(output, input);
    }

    private static (Blazor.Diagrams.BlazorDiagram Diagram, NedCatalog Catalog, DataNodeModel Collector)
        ArrayInputGraph(bool includeScalar)
    {
        var catalog = ArrayCatalog();
        var diagram = TestGraph.NewDiagram();
        var collector = diagram.Add(catalog, Collector);

        if (includeScalar)
        {
            var scalar = diagram.Add(catalog, ScalarSource);
            diagram.Links.Add(new LinkModel(
                scalar.Outputs[BuiltInIds.DefaultOutput], collector.Input("Items")));
        }

        var list = diagram.Add(catalog, ListSource);
        diagram.Links.Add(new LinkModel(
            list.Outputs[BuiltInIds.DefaultOutput], collector.Input("Items")));

        var output = diagram.Add(catalog, BuiltInIds.Output);
        diagram.Link(collector, output, TestGraph.Result);
        return (diagram, catalog, collector);
    }

    private static JsonDocument Export(Blazor.Diagrams.BlazorDiagram diagram, NedCatalog catalog) =>
        JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, catalog: catalog));

    private static JsonElement ExportedCollector(JsonDocument export, string typeId)
    {
        var value = export.RootElement.GetProperty("outputs")[0].GetProperty("value");
        Assert.Equal(typeId, value.GetProperty("$type").GetString());
        return value;
    }

    private static NedCatalog ArrayCatalog()
    {
        var sandbox = ManifestBuilder.Build(typeof(Sandbox.Add).Assembly, out _);
        var arrays = new NodeManifest
        {
            Pack = new PackInfo { Id = "arrays" },
            Types =
            {
                Source(ScalarSource, multiple: false),
                Source(ListSource, multiple: true),
                new NodeTypeDescriptor
                {
                    Id = Collector,
                    Name = "Collector",
                    Inputs =
                    {
                        new NodeInputDescriptor
                        {
                            Name = "Items", Label = "Items", Kind = InputKind.Port,
                            Type = TypeIds.Double, Multiple = true,
                        },
                    },
                    Outputs =
                    {
                        new NodeOutputDescriptor
                        {
                            Name = BuiltInIds.DefaultOutput, Type = TypeIds.Double,
                        },
                    },
                },
            },
        };
        return new NedCatalog([sandbox, arrays]);
    }

    private static NodeTypeDescriptor Source(string id, bool multiple) => new()
    {
        Id = id,
        Name = id,
        Outputs =
        {
            new NodeOutputDescriptor
            {
                Name = BuiltInIds.DefaultOutput,
                Type = TypeIds.Double,
                Multiple = multiple,
            },
        },
    };

    private static AssetEntry Asset(Guid id, bool multiple) => new()
    {
        Id = id,
        Path = "",
        Name = "array-subgraph",
        Interface = new SubgraphInterface
        {
            Outputs =
            [
                new SubgraphOutput { Name = "Result", Type = TypeIds.Double, Multiple = multiple },
            ],
        },
    };

    private const string ScalarSource = "arrays/ScalarSource";
    private const string ListSource = "arrays/ListSource";
    private const string Collector = "arrays/Collector";
}
