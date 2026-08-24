using Blazor.Diagrams;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core.Tests;

/// <summary>
/// Nezapojený exec pin má dva legitimní významy — konec běhu, nebo konec těla smyčky.
/// Rozlišit je jde jen podle kontextu, kterým se do uzlu přišlo, a ten nese
/// <see cref="TypedPortModel.Subflow"/> deklarovaný v manifestu.
/// </summary>
public sealed class SubflowValidationTests
{
    private const string Loop = "sandbox/Loop";
    private const string Branch = "sandbox/Branch";
    private const string Sequence = "sandbox/Sequence";

    private static GraphSettings ExecSettings() =>
        new() { Flow = GraphFlow.Exec, Outputs = TestGraph.Outputs(TypeIds.Double) };

    /// <summary>Exec vstup se hleda podle typu portu — Return ho ma pod jmenem "$exec", ostatni "In".</summary>
    private static void Wire(BlazorDiagram diagram, DataNodeModel from, string pin, DataNodeModel to) =>
        diagram.Links.Add(new LinkModel(
            from.Outputs[pin],
            to.Ports.OfType<TypedPortModel>().First(
                port => port.Alignment == PortAlignment.Left && port.DataType == TypeIds.Exec)));

    private static List<ValidationIssue> DeadEnds(BlazorDiagram diagram, GraphSettings settings) =>
        GraphValidator.Validate(diagram, null, settings)
            .Where(issue => issue.MessageKey == "Validation_DeadExecPath")
            .ToList();

    /// <summary>Manifest → port. Bez tohohle drátu nemá validátor podle čeho rozhodovat.</summary>
    [Fact]
    public void SubflowRole_ReachesThePort()
    {
        var catalog = TestGraph.Catalog();
        var loop = TestGraph.NewDiagram().Add(catalog, Loop);

        Assert.True(loop.Outputs["Body"].Subflow);
        Assert.False(loop.Outputs["Then"].Subflow);
        Assert.False(TestGraph.NewDiagram().Add(catalog, Branch).Outputs["True"].Subflow);
    }

    /// <summary>
    /// Slepá větev uvnitř těla je legitimní: řízení se vrátí smyčce, která rozhodne
    /// o další iteraci. Graf navíc obsahuje zpětnou hranu na smyčku — analýza na ní
    /// nesmí zacyklit.
    /// </summary>
    [Fact]
    public void DeadEndInsideLoopBody_IsSilent()
    {
        var catalog = TestGraph.Catalog();
        var settings = ExecSettings();
        var diagram = TestGraph.NewDiagram();

        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var loop = diagram.Add(catalog, Loop);
        var branch = diagram.Add(catalog, Branch);
        var ret = diagram.Add(catalog, BuiltInIds.Return, declared: settings.Outputs);

        Wire(diagram, entry, BuiltInIds.ExecEntryOutput, loop);
        Wire(diagram, loop, "Body", branch);
        Wire(diagram, branch, "True", loop);    // zpět do smyčky = další iterace
        Wire(diagram, loop, "Then", ret);
        // branch.False zůstává naprázdno — uvnitř těla to znamená „tahle iterace končí"

        Assert.Empty(DeadEnds(diagram, settings));
    }

    /// <summary>Mimo tělo tentýž tvar ukončuje celý běh, aniž by cokoliv vrátil.</summary>
    [Fact]
    public void DeadEndAtTopLevel_Warns()
    {
        var catalog = TestGraph.Catalog();
        var settings = ExecSettings();
        var diagram = TestGraph.NewDiagram();

        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var branch = diagram.Add(catalog, Branch);
        var ret = diagram.Add(catalog, BuiltInIds.Return, declared: settings.Outputs);

        Wire(diagram, entry, BuiltInIds.ExecEntryOutput, branch);
        Wire(diagram, branch, "True", ret);

        var issue = Assert.Single(DeadEnds(diagram, settings));
        Assert.Equal(branch, issue.Node);
        Assert.Equal("False", issue.Args![0]);
    }

    /// <summary>
    /// Uzel dosažitelný z těla i shora. Zpětný průchod by tady dal jen množinu předků
    /// a nerozhodl; dopředná propagace drží oba stavy vedle sebe a varuje právě jednou —
    /// za tu cestu, na které nezapojený pin opravdu ukončí běh.
    /// </summary>
    [Fact]
    public void NodeReachableBothWays_WarnsExactlyOnce()
    {
        var catalog = TestGraph.Catalog();
        var settings = ExecSettings();
        var diagram = TestGraph.NewDiagram();

        var entry = diagram.Add(catalog, BuiltInIds.ExecEntry);
        var loop = diagram.Add(catalog, Loop);
        var shared = diagram.Add(catalog, Sequence);
        var ret = diagram.Add(catalog, BuiltInIds.Return, declared: settings.Outputs);

        Wire(diagram, entry, BuiltInIds.ExecEntryOutput, loop);
        Wire(diagram, loop, "Body", shared);    // kontext: uvnitř těla
        Wire(diagram, loop, "Then", shared);    // kontext: nejvyšší úroveň
        Wire(diagram, shared, "Next", ret);
        // shared.Then zůstává naprázdno

        var issue = Assert.Single(DeadEnds(diagram, settings));
        Assert.Equal(shared, issue.Node);
        Assert.Equal("Then", issue.Args![0]);
    }
}
