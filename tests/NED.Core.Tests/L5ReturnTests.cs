using System.Text.Json;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Assets;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core.Tests;

public sealed class L5ReturnTests
{
    [Fact]
    public void DataGraph_WithSecondOutputNode_IsInvalid()
    {
        var catalog = TestGraph.Catalog();
        var settings = TestGraph.Settings();
        var diagram = TestGraph.NewDiagram();
        var number = diagram.Add(catalog, TestGraph.Number, values: ("Value", 10d));
        var sink = diagram.AddSink(catalog, settings.Outputs);
        diagram.AddSink(catalog, settings.Outputs);
        diagram.Link(number, sink, TestGraph.Result);

        var issues = GraphValidator.Validate(diagram, null, settings);

        Assert.Contains(issues, issue => issue.MessageKey == "Validation_OneOutputNode");
    }

    [Fact]
    public void DataGraph_WithoutDeclarations_IsInvalid()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        diagram.AddSink(catalog, Array.Empty<GraphOutput>());

        var issues = GraphValidator.Validate(diagram, null, new GraphSettings());

        Assert.Contains(issues, issue => issue.MessageKey == "Validation_NoOutput");
    }

    [Fact]
    public void Palette_HidesExistingSingletonsAndShowsReturnOnlyForExec()
    {
        var entry = new NodeTypeDescriptor { Id = BuiltInIds.ExecEntry };
        var output = new NodeTypeDescriptor { Id = BuiltInIds.Output };
        var returnNode = new NodeTypeDescriptor { Id = BuiltInIds.Return };
        var exec = new GraphSettings { Flow = GraphFlow.Exec };
        var data = new GraphSettings { Flow = GraphFlow.Data };

        Assert.False(NedCanvas.BuiltInPaletteVisible(entry, exec, id => id == BuiltInIds.ExecEntry));
        Assert.False(NedCanvas.BuiltInPaletteVisible(output, data, id => id == BuiltInIds.Output));
        Assert.True(NedCanvas.BuiltInPaletteVisible(returnNode, exec, _ => false));
        Assert.False(NedCanvas.BuiltInPaletteVisible(returnNode, data, _ => false));
    }

    [Fact]
    public void OutputNodeInExecGraph_IsError()
    {
        var catalog = TestGraph.Catalog();
        var settings = new GraphSettings { Flow = GraphFlow.Exec, Outputs = TestGraph.Outputs(TypeIds.Double) };
        var diagram = TestGraph.NewDiagram();
        diagram.Add(catalog, BuiltInIds.ExecEntry);
        var sink = diagram.AddSink(catalog, settings.Outputs);

        // V exec toku je navrat udalost v poradi — sink, do ktereho se taha pozpatku,
        // by neurcoval, ktera vetev tu hodnotu vraci.
        var issues = GraphValidator.Validate(diagram, null, settings);

        Assert.Contains(issues, issue =>
            issue.MessageKey == "Validation_OutputNodeInExecGraph" && issue.Node == sink);
    }

    [Fact]
    public void ExecFunction_TwoBranchesExportDifferentReturnValuesAndOutputDeclarations()
    {
        using var lib = new TempLibrary();
        var functionId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
        lib.Write("two-returns", TwoReturnFunction(functionId));

        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(lib.Catalog, BuiltInIds.ExecEntry);
        var call = new SubgraphNodeModel(lib.Index.Resolve(functionId)!, new Point(200, 0));
        diagram.Nodes.Add(call);
        diagram.Links.Add(new LinkModel(entry.Outputs[BuiltInIds.ExecEntryOutput], call.ExecInput!));

        using var export = JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Flow = GraphFlow.Exec }, lib.Index, lib.Catalog));
        var function = Assert.Single(export.RootElement.GetProperty("functions").EnumerateArray());
        var declaration = Assert.Single(function.GetProperty("outputs").EnumerateArray());
        var returns = function.GetProperty("nodes").EnumerateArray()
            .Where(node => node.GetProperty("$type").GetString() == BuiltInIds.Return)
            .ToList();

        Assert.Equal("Result", declaration.GetProperty("name").GetString());
        Assert.Equal(TypeIds.Double, declaration.GetProperty("type").GetString());
        Assert.False(declaration.TryGetProperty("value", out _));
        Assert.Equal(2, returns.Count);
        Assert.Equal(new[] { 1d, 2d }, returns
            .Select(node => node.GetProperty("Result").GetProperty("Value").GetDouble())
            .OrderBy(value => value));
    }

    [Fact]
    public void ExecGraph_WithDeclaredOutputAndNoReturn_IsInvalid()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        diagram.Add(catalog, BuiltInIds.ExecEntry);
        var settings = new GraphSettings { Flow = GraphFlow.Exec, Outputs = TestGraph.Outputs(TypeIds.Double) };

        var issues = GraphValidator.Validate(diagram, null, settings);

        Assert.Contains(issues, issue => issue.MessageKey == "Validation_NoReturn");
        Assert.DoesNotContain(issues, issue => issue.MessageKey == "Validation_OutputEmpty");
    }

    [Fact]
    public void ReturnValueLink_SurvivesSaveLoadRoundTrip()
    {
        var catalog = TestGraph.Catalog();
        var settings = new GraphSettings { Flow = GraphFlow.Exec, Outputs = TestGraph.Outputs(TypeIds.Double) };
        var diagram = TestGraph.NewDiagram();
        var returnNode = diagram.Add(catalog, BuiltInIds.Return, declared: settings.Outputs);
        var number = diagram.Add(catalog, TestGraph.Number, values: ("Value", 7d));
        diagram.Link(number, returnNode, TestGraph.Result);

        var document = GraphPersistence.ToDocument(diagram, settings);
        var loaded = TestGraph.NewDiagram();
        GraphPersistence.LoadInto(loaded, document, catalog);

        // Porty musi vzniknout driv, nez se obnovuji linky — jinak link tise zmizi.
        var loadedReturn = Assert.Single(loaded.Nodes.OfType<DataNodeModel>(), node => node.IsReturnNode);
        Assert.NotNull(loadedReturn.InputDefs.Single(input => input.Name == TestGraph.Result).Port);
        Assert.Single(loaded.Links);
    }

    [Fact]
    public void ReturnNode_IsOfferedWhenDraggingFromExecPin()
    {
        var catalog = TestGraph.Catalog();
        var returnType = catalog.Resolve(BuiltInIds.Return)!;

        // Filtr pickeru u exec pinu se ptá jen na tohle. Dřív ho předcházela podmínka
        // „uzel má výstup", která sink — a tedy Return — vyřadila.
        Assert.Contains(NedCatalog.InputPorts(returnType),
            port => TypeIds.IsCompatible(TypeIds.Exec, Array.Empty<string>(), port.Type));
    }

    [Fact]
    public void AnyTypedInput_IsAlwaysPortNeverField()
    {
        var input = new NodeInput
        {
            Descriptor = new NodeInputDescriptor { Name = "X", Kind = InputKind.Field, Type = TypeIds.Any },
            DataType = TypeIds.Any,
        };

        // Neznámý typ ve field režimu nabízel dropdown všech typů světa.
        Assert.True(input.DefaultAsPort);
        Assert.False(input.Togglable);
    }

    [Fact]
    public void ExecNodeInDataGraph_IsError()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var output = diagram.Add(catalog, BuiltInIds.Output);
        var number = diagram.Add(catalog, TestGraph.Number);
        diagram.Link(number, output, TestGraph.Result);

        var issues = GraphValidator.Validate(diagram, null, TestGraph.Settings());
        var execIssues = issues.Where(issue => issue.MessageKey == "Validation_ExecNodeInDataGraph").ToList();

        Assert.Equal(IssueSeverity.Error, Assert.Single(execIssues).Severity);
        Assert.Same(entry, execIssues[0].Node);
        Assert.DoesNotContain(
            GraphValidator.Validate(diagram, null, new GraphSettings { Flow = GraphFlow.Exec }),
            issue => issue.MessageKey == "Validation_ExecNodeInDataGraph");
    }

    [Fact]
    public void ExportedOutputs_CarryDeclarationAndValue()
    {
        var catalog = TestGraph.Catalog();
        var settings = new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double, TypeIds.Double) };
        var diagram = TestGraph.NewDiagram();
        var number = diagram.Add(catalog, TestGraph.Number, values: ("Value", 3d));
        var sink = diagram.AddSink(catalog, settings.Outputs);
        diagram.Link(number, sink, TestGraph.Result);

        using var export = JsonDocument.Parse(GraphExporter.Export(diagram, settings, catalog: catalog));
        var outputs = export.RootElement.GetProperty("outputs").EnumerateArray().ToList();

        Assert.Equal(2, outputs.Count);
        Assert.Equal(TestGraph.Result, outputs[0].GetProperty("name").GetString());
        Assert.Equal(TypeIds.Double, outputs[0].GetProperty("type").GetString());
        Assert.Equal(3d, outputs[0].GetProperty("value").GetProperty("Value").GetDouble());
        // Nezapojena skalarni deklarace vyda null, ne chybejici klic.
        Assert.Equal(JsonValueKind.Null, outputs[1].GetProperty("value").ValueKind);
    }

    private static GraphDocument TwoReturnFunction(Guid id) => new()
    {
        Settings = new GraphSettingsDto
        {
            Id = id.ToString(), Name = "two-returns", Flow = nameof(GraphFlow.Exec), Outputs = TestGraph.Declare(TypeIds.Double),
        },
        Nodes =
        {
            new GraphNodeDto { Id = "entry", TypeName = BuiltInIds.ExecEntry },
            new GraphNodeDto { Id = "branch", TypeName = "sandbox/Branch" },
            new GraphNodeDto { Id = "return-true", TypeName = BuiltInIds.Return },
            new GraphNodeDto { Id = "return-false", TypeName = BuiltInIds.Return },
            new GraphNodeDto { Id = "one", TypeName = TestGraph.Number, Fields = { ["Value"] = 1d } },
            new GraphNodeDto { Id = "two", TypeName = TestGraph.Number, Fields = { ["Value"] = 2d } },
        },
        Links =
        {
            new GraphLinkDto
            {
                FromNode = "entry", FromPort = BuiltInIds.ExecEntryOutput,
                ToNode = "branch", ToPort = "In",
            },
            new GraphLinkDto
            {
                FromNode = "branch", FromPort = "True",
                ToNode = "return-true", ToPort = BuiltInIds.ExecInput,
            },
            new GraphLinkDto
            {
                FromNode = "branch", FromPort = "False",
                ToNode = "return-false", ToPort = BuiltInIds.ExecInput,
            },
            new GraphLinkDto
            {
                FromNode = "one", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "return-true", ToPort = "Result",
            },
            new GraphLinkDto
            {
                FromNode = "two", FromPort = BuiltInIds.DefaultOutput,
                ToNode = "return-false", ToPort = "Result",
            },
        },
    };
}
