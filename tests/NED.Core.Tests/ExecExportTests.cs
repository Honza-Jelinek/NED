using System.Text.Json;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core.Tests;

public sealed class ExecExportTests
{
    [Fact]
    public void LinearChain_ExportsEntryOrderedNodesAndEdges()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var sequence = diagram.Add(catalog, "sandbox/Sequence");
        var branch = diagram.Add(catalog, "sandbox/Branch");
        Link(diagram, entry, BuiltInIds.ExecEntryOutput, sequence);
        Link(diagram, sequence, "Then", branch);

        using var export = Export(diagram, catalog);
        var root = export.RootElement;
        var nodes = root.GetProperty("nodes").EnumerateArray().ToList();
        var edges = root.GetProperty("exec").EnumerateArray().ToList();

        Assert.Equal("exec", root.GetProperty("settings").GetProperty("graphKind").GetString());
        Assert.Equal(nodes[0].GetProperty("$id").GetString(), root.GetProperty("entry").GetString());
        Assert.Equal(new[] { BuiltInIds.ExecEntry, "sandbox/Sequence", "sandbox/Branch" },
            nodes.Select(node => node.GetProperty("$type").GetString()));
        Assert.Equal(2, edges.Count);
        Assert.False(root.TryGetProperty("outputs", out _));
        Assert.False(root.TryGetProperty("inputs", out _));
    }

    [Fact]
    public void Parameters_ArePublicInputsAndConsumersUseParamMarker()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var first = diagram.Add(catalog, "sandbox/Branch");
        var second = diagram.Add(catalog, "sandbox/Branch");
        var ticket = AddParameter(diagram, catalog, "Ticket", TypeIds.Bool, "true", 20, "Ticket flag");
        var earlier = AddParameter(diagram, catalog, "Earlier", TypeIds.Double, "3", 10, "First");
        Link(diagram, entry, BuiltInIds.ExecEntryOutput, first);
        Link(diagram, first, "True", second);
        diagram.Links.Add(new LinkModel(ticket.Outputs[BuiltInIds.DefaultOutput], first.Input("Cond")));
        diagram.Links.Add(new LinkModel(ticket.Outputs[BuiltInIds.DefaultOutput], second.Input("Cond")));

        using var export = Export(diagram, catalog);
        var root = export.RootElement;
        var inputs = root.GetProperty("inputs").EnumerateArray().ToList();
        var branches = root.GetProperty("nodes").EnumerateArray()
            .Where(node => node.GetProperty("$type").GetString() == "sandbox/Branch").ToList();

        Assert.Equal(new[] { "Earlier", "Ticket" },
            inputs.Select(input => input.GetProperty("name").GetString()));
        Assert.Equal("bool", inputs[1].GetProperty("type").GetString());
        Assert.Equal("true", inputs[1].GetProperty("default").GetString());
        Assert.Equal("Ticket flag", inputs[1].GetProperty("description").GetString());
        Assert.All(branches, branch =>
        {
            var cond = branch.GetProperty("Cond");
            Assert.Equal("Ticket", cond.GetProperty("$param").GetString());
            Assert.False(cond.TryGetProperty("$id", out _));
        });
        Assert.DoesNotContain(BuiltInIds.GraphInput, root.GetRawText(), StringComparison.Ordinal);

        var ids = new List<string>();
        CollectIds(root, ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Validator_ReportsDuplicateParameterNames()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        AddParameter(diagram, catalog, "Ticket", TypeIds.Bool, "", 0, "");
        AddParameter(diagram, catalog, "Ticket", TypeIds.Double, "", 1, "");

        var issues = GraphValidator.Validate(diagram, null, new GraphSettings());

        Assert.Equal(2, issues.Count(issue => issue.MessageKey == "Validation_DuplicateParameter"));
    }

    [Fact]
    public void ExecLoop_IsExportedWithoutInfiniteTraversal()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var sequence = diagram.Add(catalog, "sandbox/Sequence");
        var branch = diagram.Add(catalog, "sandbox/Branch");
        Link(diagram, entry, BuiltInIds.ExecEntryOutput, sequence);
        Link(diagram, sequence, "Then", branch);
        Link(diagram, branch, "False", sequence);

        using var export = Export(diagram, catalog);

        Assert.Equal(3, export.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(3, export.RootElement.GetProperty("exec").GetArrayLength());
    }

    [Fact]
    public void DataFromExecNode_IsReferenceWhilePureDataProducerIsInline()
    {
        var catalog = CatalogWithExecValue();
        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var value = diagram.Add(catalog, "flow/ExecValue");
        var branch = diagram.Add(catalog, "sandbox/Branch");
        var number = diagram.Add(catalog, TestGraph.Number, values: ("Value", 1d));
        Link(diagram, entry, BuiltInIds.ExecEntryOutput, value);
        Link(diagram, value, "Then", branch);
        diagram.Links.Add(new LinkModel(value.Outputs["Result"], branch.Input("Cond")));
        diagram.Links.Add(new LinkModel(number.Outputs[BuiltInIds.DefaultOutput], value.Input("Amount")));

        using var export = Export(diagram, catalog);
        var nodes = export.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        var valueNode = nodes.Single(node => node.GetProperty("$type").GetString() == "flow/ExecValue");
        var branchNode = nodes.Single(node => node.GetProperty("$type").GetString() == "sandbox/Branch");

        Assert.Equal(valueNode.GetProperty("$id").GetString(),
            branchNode.GetProperty("Cond").GetProperty("$ref").GetString());
        Assert.Equal("Result", branchNode.GetProperty("Cond").GetProperty("$output").GetString());
        Assert.Equal(TestGraph.Number, valueNode.GetProperty("Amount").GetProperty("$type").GetString());
    }

    [Fact]
    public void Validator_RequiresExactlyOneEntryAndReportsUnreachableExecNode()
    {
        var catalog = TestGraph.Catalog();
        var empty = TestGraph.NewDiagram();
        Assert.Contains(GraphValidator.Validate(empty, null, new GraphSettings { Flow = GraphFlow.Exec }),
            issue => issue.MessageKey == "Validation_NoExecEntry");

        var diagram = TestGraph.NewDiagram();
        diagram.Add(catalog, BuiltInIds.ExecEntry);
        diagram.Add(catalog, BuiltInIds.ExecEntry);
        var orphan = diagram.Add(catalog, "sandbox/Sequence");
        var issues = GraphValidator.Validate(diagram, null, new GraphSettings { Flow = GraphFlow.Exec });

        Assert.Contains(issues, issue => issue.MessageKey == "Validation_MultipleExecEntries");
        Assert.Contains(issues, issue => issue.MessageKey == "Validation_Orphan" && issue.Node == orphan);
        Assert.DoesNotContain(issues, issue => issue.MessageKey == "Validation_NoOutput");
    }

    /// <summary>
    /// Hrana s daty proti exec pořadí (S1 bere Result z pozdějšího S2) nesmí zamaskovat
    /// zbytek řetězu — dosažitelnost po exec musí jít dál, i když uzel už objevil datový průchod.
    /// </summary>
    [Fact]
    public void BackwardDataEdge_DoesNotHideLaterExecNodes()
    {
        var catalog = CatalogWithExecValue();
        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var first = diagram.Add(catalog, "flow/ExecValue");
        var second = diagram.Add(catalog, "flow/ExecValue");
        var last = diagram.Add(catalog, "sandbox/Sequence");
        Link(diagram, entry, BuiltInIds.ExecEntryOutput, first);
        Link(diagram, first, "Then", second);
        Link(diagram, second, "Then", last);
        diagram.Links.Add(new LinkModel(second.Outputs["Result"], first.Input("Amount")));

        var issues = GraphValidator.Validate(diagram, null, new GraphSettings { Flow = GraphFlow.Exec });

        Assert.DoesNotContain(issues,
            issue => issue.MessageKey == "Validation_Orphan" && issue.Node == last);
        Assert.Equal(4, Export(diagram, catalog).RootElement.GetProperty("nodes").GetArrayLength());
    }

    [Fact]
    public void EveryExportedId_IsUnique()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var branch = diagram.Add(catalog, "sandbox/Branch");
        var number = diagram.Add(catalog, TestGraph.Number);
        Link(diagram, entry, BuiltInIds.ExecEntryOutput, branch);
        diagram.Links.Add(new LinkModel(number.Outputs[BuiltInIds.DefaultOutput], branch.Input("Cond")));

        using var export = Export(diagram, catalog);
        var ids = new List<string>();
        CollectIds(export.RootElement, ids);

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static void Link(
        Blazor.Diagrams.BlazorDiagram diagram, DataNodeModel from, string pin, DataNodeModel to) =>
        diagram.Links.Add(new LinkModel(from.Outputs[pin], to.Input("In")));

    private static DataNodeModel AddParameter(
        Blazor.Diagrams.BlazorDiagram diagram, NedCatalog catalog, string name, string type,
        string defaultValue, int order, string description) =>
        diagram.Add(catalog, BuiltInIds.GraphInput, values:
        [
            (BuiltInIds.GraphInputName, name),
            (BuiltInIds.GraphInputTypeName, type),
            (BuiltInIds.GraphInputDefault, defaultValue),
            (BuiltInIds.GraphInputOrder, order),
            (BuiltInIds.GraphInputDescription, description),
        ]);

    private static JsonDocument Export(Blazor.Diagrams.BlazorDiagram diagram, NedCatalog catalog) =>
        JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Flow = GraphFlow.Exec }, catalog: catalog));

    private static void CollectIds(JsonElement element, List<string> ids)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("$id", out var id)) ids.Add(id.GetString()!);
            foreach (var property in element.EnumerateObject()) CollectIds(property.Value, ids);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CollectIds(item, ids);
    }

    private static NedCatalog CatalogWithExecValue()
    {
        var sandbox = NED.Manifest.Generator.ManifestBuilder.Build(typeof(Sandbox.Add).Assembly, out _);
        var flow = new NodeManifest
        {
            Pack = new PackInfo { Id = "flow" },
            Types =
            {
                new NodeTypeDescriptor
                {
                    Id = "flow/ExecValue", Name = "Exec value",
                    Inputs =
                    {
                        new NodeInputDescriptor
                        {
                            Name = "In", Label = "In", Kind = InputKind.Port,
                            Type = TypeIds.Exec, Multiple = true,
                        },
                        new NodeInputDescriptor
                        {
                            Name = "Amount", Label = "Amount", Kind = InputKind.Port,
                            Type = TypeIds.Double,
                        },
                    },
                    Outputs =
                    {
                        new NodeOutputDescriptor { Name = "Then", Type = TypeIds.Exec },
                        new NodeOutputDescriptor { Name = "Result", Type = TypeIds.Bool },
                    },
                },
            },
        };
        return new NedCatalog(new[] { sandbox, flow });
    }
}
