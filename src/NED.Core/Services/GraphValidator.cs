using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions;
using NED.Abstractions.Manifest;
using NED.Core.Assets;
using NED.Core.Manifest;

namespace NED.Core;

public enum IssueSeverity { Error, Warning }

/// <summary>
/// Jeden problém nalezený validátorem. <see cref="MessageKey"/> je lokalizační klíč
/// (UI ho resolvuje přes IStringLocalizer), <see cref="Args"/> jsou parametry pro string.Format.
/// <see cref="Node"/> = null znamená graf-level problém (např. chybějící Output node).
/// </summary>
public sealed record ValidationIssue(
    IssueSeverity Severity,
    string MessageKey,
    NodeModel? Node = null,
    object?[]? Args = null);

/// <summary>
/// Statická validace grafu. Pravidla odvozená z toho, co <c>GraphExporter</c> považuje
/// za chybu ($error markery). UI-free — vrací jen lokalizační klíče.
/// </summary>
public static class GraphValidator
{
    public static List<ValidationIssue> Validate(
        BlazorDiagram diagram, AssetIndex? assetIndex, GraphSettings settings)
    {
        var isExec = settings.IsExec;
        var declared = settings.Outputs.Count;

        var issues = new List<ValidationIssue>();
        var nodes = diagram.Nodes.ToList();
        var dataNodes = nodes.OfType<DataNodeModel>().ToList();
        var outputNodes = dataNodes.Where(n => n.IsOutputNode).ToList();
        var execEntries = dataNodes.Where(n => n.IsExecEntryNode).ToList();
        var returnNodes = dataNodes.Where(n => n.IsReturnNode).ToList();

        // Datový graf je výraz — bez deklarované návratové hodnoty nic nepočítá.
        // Exec graf bez deklarací je běžný kořenový workflow, ten jen vykoná kroky.
        if (!isExec && declared == 0)
            issues.Add(new(IssueSeverity.Error, "Validation_NoOutput"));

        // Dráty se scházejí do jediného sinku. Ne role, ale tok: v exec toku hodnoty
        // vracejí Return uzly, protože návrat je událost v pořadí, ne datový tah.
        if (!isExec && outputNodes.Count != 1)
            issues.Add(new(IssueSeverity.Error, "Validation_OneOutputNode"));
        if (isExec)
            foreach (var node in outputNodes)
                issues.Add(new(IssueSeverity.Error, "Validation_OutputNodeInExecGraph", node));

        if (isExec && execEntries.Count == 0)
            issues.Add(new(IssueSeverity.Error, "Validation_NoExecEntry"));
        if (isExec && execEntries.Count > 1)
            issues.Add(new(IssueSeverity.Error, "Validation_MultipleExecEntries"));
        if (isExec && declared > 0 && returnNodes.Count == 0)
            issues.Add(new(IssueSeverity.Error, "Validation_NoReturn"));

        // Řídicí uzel v datovém grafu je tvrdá chyba, ne osamocenost: exec hrany se do
        // datového exportu vůbec nedostanou, takže by tam mlčky nic nedělal. Paleta ho
        // nenabízí, do grafu se dostane přepnutím toku nebo ze souboru.
        if (!isExec)
        {
            foreach (var dn in dataNodes.Where(node => NedCatalog.HasExecPort(node.Descriptor)))
                issues.Add(new(IssueSeverity.Error, "Validation_ExecNodeInDataGraph", dn,
                    [dn.Descriptor.Name]));
            foreach (var sg in nodes.OfType<SubgraphNodeModel>()
                         .Where(node => node.Interface.Flow == GraphFlow.Exec))
                issues.Add(new(IssueSeverity.Error, "Validation_ExecNodeInDataGraph", sg,
                    [sg.SubgraphName]));
        }

        foreach (var duplicate in dataNodes.Where(node => node.IsGraphInputNode)
                     .GroupBy(node => node.ValueAsString(BuiltInIds.GraphInputName) ?? "", StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            foreach (var node in duplicate)
                issues.Add(new(IssueSeverity.Error, "Validation_DuplicateParameter", node, [duplicate.Key]));

        // E2 — deklarace bez producenta. Nezapojený port není chyba (export vydá null,
        // u polové deklarace prázdné pole), ale sink, do kterého nevede vůbec nic, je.
        foreach (var on in isExec ? Enumerable.Empty<DataNodeModel>() : outputNodes)
        {
            if (declared > 0 && on.Ports.All(port => port.Links.Count == 0))
                issues.Add(new(IssueSeverity.Error, "Validation_OutputEmpty", on));
        }

        // E3 — povinný vstupní port nepřipojený (sink řeší E2)
        foreach (var dn in dataNodes)
        {
            if (dn.DeclaresGraphOutputs) continue;
            foreach (var input in dn.InputDefs)
            {
                if (input.DataType != TypeIds.Exec && input.AsPort && !input.Optional
                    && input.Port is { } port && port.Links.Count == 0)
                    issues.Add(new(IssueSeverity.Error, "Validation_RequiredInput", dn, [input.Label]));
            }
        }

        // E4 — mrtvá reference na subgraf
        foreach (var sg in nodes.OfType<SubgraphNodeModel>())
        {
            if (assetIndex?.Resolve(sg.SubgraphId) is null)
                issues.Add(new(IssueSeverity.Error, "Validation_StaleSubgraph", sg, [sg.SubgraphName]));
        }

        // E6 — placeholder za node s nerozpoznaným typem (chybí assembly?)
        foreach (var mn in nodes.OfType<MissingNodeModel>())
            issues.Add(new(IssueSeverity.Error, "Validation_MissingType", mn, [mn.Dto.TypeName]));

        // E5 — nekompatibilní link (po změně type-pickeru)
        foreach (var link in diagram.Links.OfType<LinkModel>())
        {
            if ((link.Source as SinglePortAnchor)?.Port is not TypedPortModel a) continue;
            if ((link.Target as SinglePortAnchor)?.Port is not TypedPortModel b) continue;
            if (a.Alignment == b.Alignment) continue;

            var (output, input) = a.Alignment == PortAlignment.Right ? (a, b) : (b, a);
            if (!TypedPortModel.IsCompatible(output, input))
                issues.Add(new(IssueSeverity.Error, "Validation_BrokenLink", input.Parent as NodeModel));
        }

        // W1 — osamocený node (nedosažitelný z žádného Output nodu)
        if (isExec && execEntries.Count > 0)
        {
            var reachable = new HashSet<NodeModel>();
            WalkExec(execEntries[0], reachable, new HashSet<DataNodeModel>());
            AddOrphans(nodes, reachable, issues);

            // W2 — exec cesta, která končí naslepo mimo tělo smyčky
            WalkDeadEnds(execEntries[0], new List<NodeModel>(), new HashSet<string>(), issues);
        }
        else if (outputNodes.Count > 0)
        {
            var reachable = new HashSet<NodeModel>();
            foreach (var on in outputNodes) Walk(on, reachable);
            AddOrphans(nodes, reachable, issues);
        }

        return issues;
    }

    /// <summary>
    /// Hledá exec piny bez drátu, které opravdu ukončují běh.
    ///
    /// Nezapojený pin má dva legitimní významy a rozlišit je jde jen podle toho,
    /// <b>jak se do uzlu přišlo</b>: uvnitř těla (<see cref="TypedPortModel.Subflow"/>)
    /// znamená „tělo dojelo“ a řízení se vrací vlastníkovi, mimo ně končí celý běh.
    /// Proto se kontext nese dopředu od Entry, ne dopočítává pozpátku — zpětný průchod
    /// dá na spojích a cyklech jen množinu předků, ne zásobník vlastníků.
    ///
    /// <paramref name="owners"/> je součástí klíče návštěvy schválně: uzel dosažitelný
    /// z těla i shora vyprodukuje dva stavy a varování padne jen na tom horním.
    /// </summary>
    private static void WalkDeadEnds(
        NodeModel node, List<NodeModel> owners, HashSet<string> visited, List<ValidationIssue> issues)
    {
        // Hrana na obklopující uzel je návrat do něj, ne jeho spuštění — stejné pravidlo,
        // jaké má interpret. Bez něj by se analýza na zpětné hraně zamotala.
        if (owners.Contains(node)) return;
        if (!visited.Add(string.Join("|", owners.Select(o => o.Id).Append(node.Id)))) return;

        foreach (var port in node.Ports.OfType<TypedPortModel>()
                     .Where(port => port.Alignment == PortAlignment.Right
                                    && port.DataType == TypeIds.Exec))
        {
            var target = port.Links.OfType<LinkModel>()
                .Select(link => OtherEnd(link, port))
                .FirstOrDefault(other => other is not null);

            if (target is null)
            {
                if (owners.Count == 0)
                    issues.Add(new(IssueSeverity.Warning, "Validation_DeadExecPath", node,
                                   new object?[] { port.Label ?? "" }));
                continue;
            }

            WalkDeadEnds(target, port.Subflow ? [.. owners, node] : owners, visited, issues);
        }
    }

    private static void AddOrphans(
        IEnumerable<NodeModel> nodes, HashSet<NodeModel> reachable, List<ValidationIssue> issues)
    {
        foreach (var node in nodes)
        {
            if (node is DataNodeModel dataNode
                && (dataNode.IsOutputNode || dataNode.IsExecEntryNode)) continue;
            if (node is not (DataNodeModel or SubgraphNodeModel or MissingNodeModel)) continue;
            if (!reachable.Contains(node))
                issues.Add(new(IssueSeverity.Warning, "Validation_Orphan", node));
        }
    }

    /// <summary>
    /// Dosažitelnost v exec grafu: dopředu po exec hranách, u každého uzlu pozpátku po datech.
    /// <paramref name="execVisited"/> je zvlášť od <paramref name="reachable"/> schválně —
    /// uzel objevený nejdřív jako datový producent (hrana proti exec pořadí) by jinak
    /// zablokoval vlastní exec pokračování a všechno za ním by se ohlásilo jako osamocené.
    /// </summary>
    private static void WalkExec(
        DataNodeModel node, HashSet<NodeModel> reachable, HashSet<DataNodeModel> execVisited)
    {
        if (!execVisited.Add(node)) return;
        reachable.Add(node);

        foreach (var port in node.Ports.OfType<TypedPortModel>()
                     .Where(port => port.Alignment == PortAlignment.Left && port.DataType != TypeIds.Exec))
            foreach (var link in port.Links.OfType<LinkModel>())
                if (OtherEnd(link, port) is { } producer) WalkData(producer, reachable);

        foreach (var port in node.Ports.OfType<TypedPortModel>()
                     .Where(port => port.Alignment == PortAlignment.Right && port.DataType == TypeIds.Exec))
            foreach (var link in port.Links.OfType<LinkModel>())
                if (OtherEnd(link, port) is DataNodeModel target) WalkExec(target, reachable, execVisited);
    }

    private static void WalkData(NodeModel node, HashSet<NodeModel> visited)
    {
        if (!visited.Add(node)) return;
        foreach (var port in node.Ports.OfType<TypedPortModel>()
                     .Where(port => port.Alignment == PortAlignment.Left && port.DataType != TypeIds.Exec))
            foreach (var link in port.Links.OfType<LinkModel>())
                if (OtherEnd(link, port) is { } producer) WalkData(producer, visited);
    }

    /// <summary>Projde graf od nodu proti směru toku (přes vstupní Left porty k producerům).</summary>
    private static void Walk(NodeModel node, HashSet<NodeModel> visited)
    {
        if (!visited.Add(node)) return;
        foreach (var port in node.Ports.Where(p => p.Alignment == PortAlignment.Left))
            foreach (var link in port.Links.OfType<LinkModel>())
                if (OtherEnd(link, port) is { } other)
                    Walk(other, visited);
    }

    private static NodeModel? OtherEnd(LinkModel link, PortModel port)
    {
        var a = (link.Source as SinglePortAnchor)?.Port;
        var b = (link.Target as SinglePortAnchor)?.Port;
        if (a == port) return b?.Parent as NodeModel;
        if (b == port) return a?.Parent as NodeModel;
        return null;
    }
}
