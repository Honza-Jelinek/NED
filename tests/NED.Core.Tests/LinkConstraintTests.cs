using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;

namespace NED.Core.Tests;

public sealed class LinkConstraintTests
{
    [Fact]
    public void ExecOutput_KeepsOnlyTheNewestLink()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var first = diagram.Add(catalog, "sandbox/Sequence");
        var second = diagram.Add(catalog, "sandbox/Sequence");

        var old = Connect(diagram, entry, BuiltInIds.ExecEntryOutput, first);
        var fresh = Connect(diagram, entry, BuiltInIds.ExecEntryOutput, second);

        Assert.DoesNotContain(old, diagram.Links);
        Assert.Contains(fresh, diagram.Links);
    }

    [Fact]
    public void ExecInput_AcceptsSeveralDifferentProducers()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var branch = diagram.Add(catalog, "sandbox/Branch");
        var target = diagram.Add(catalog, "sandbox/Sequence");

        Connect(diagram, branch, "True", target);
        Connect(diagram, branch, "False", target);

        Assert.Equal(2, target.Input("In").Links.Count);
    }

    [Fact]
    public void SamePortPairLinkedTwice_CollapsesToOneLink()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var branch = diagram.Add(catalog, "sandbox/Branch");
        var target = diagram.Add(catalog, "sandbox/Sequence");

        Connect(diagram, branch, "True", target);
        Connect(diagram, branch, "True", target);

        Assert.Single(diagram.Links);
        Assert.Single(target.Input("In").Links);
    }

    /// <summary>Datový výstup se větví — omezení se týká jen exec strany.</summary>
    [Fact]
    public void DataOutput_StillFansOutToSeveralConsumers()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var number = diagram.Add(catalog, TestGraph.Number);
        var add = diagram.Add(catalog, TestGraph.AddNode);

        Attach(diagram, number.Outputs[BuiltInIds.DefaultOutput], add.Input("A"));
        Attach(diagram, number.Outputs[BuiltInIds.DefaultOutput], add.Input("B"));

        Assert.Equal(2, number.Outputs[BuiltInIds.DefaultOutput].Links.Count);
    }

    [Fact]
    public void NonMultipleDataInput_KeepsOnlyTheNewestLink()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var first = diagram.Add(catalog, TestGraph.Number);
        var second = diagram.Add(catalog, TestGraph.Number);
        var add = diagram.Add(catalog, TestGraph.AddNode);

        var old = Attach(diagram, first.Outputs[BuiltInIds.DefaultOutput], add.Input("A"));
        var fresh = Attach(diagram, second.Outputs[BuiltInIds.DefaultOutput], add.Input("A"));

        Assert.DoesNotContain(old, diagram.Links);
        Assert.Contains(fresh, diagram.Links);
    }

    private static LinkModel Connect(
        Blazor.Diagrams.BlazorDiagram diagram, DataNodeModel from, string pin, DataNodeModel to) =>
        Attach(diagram, from.Outputs[pin], to.Input("In"));

    /// <summary>Napojení tak, jak ho vidí plátno: přidat link a nechat doběhnout pravidla.</summary>
    private static LinkModel Attach(
        Blazor.Diagrams.BlazorDiagram diagram, PortModel output, PortModel input)
    {
        var link = new LinkModel(output, input);
        diagram.Links.Add(link);
        LinkConstraints.Apply(link);
        return link;
    }
}
