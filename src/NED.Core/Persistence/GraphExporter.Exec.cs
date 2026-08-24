using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions;
using NED.Abstractions.Manifest;
using NED.Core.Assets;
using NED.Core.Manifest;

namespace NED.Core.Persistence;

public static partial class GraphExporter
{
    private static ExportModel BuildExecModel(
        BlazorDiagram diagram, ExportContext ctx, NedCatalog? catalog)
    {
        var body = BuildExecBody(diagram, ctx, new HashSet<Guid>());

        return new ExportModel
        {
            Packs = ExportPacks(ctx.Shared.UsedTypeIds, catalog),
            GraphKind = "exec",
            Entry = body.Entry,
            Nodes = body.Nodes,
            ExecEdges = body.Edges,
            Functions = ctx.Shared.Functions,
        };
    }

    /// <summary>
    /// Tabulka uzlů a exec hran jednoho exec grafu — kořenového i těla funkce.
    ///
    /// <paramref name="callStack"/> hlídá <b>zanoření během exportu</b>, ne rekurzi v grafu:
    /// funkce, která volá sama sebe, se do <c>functions</c> zapíše jednou a druhé volání
    /// najde hotovou položku. Zacyklit by se to mohlo jen tehdy, kdyby se tělo stavělo
    /// znovu uprostřed sebe sama.
    /// </summary>
    private static ExecBody BuildExecBody(
        BlazorDiagram diagram, ExportContext ctx, HashSet<Guid> callStack)
    {
        var entry = diagram.Nodes.OfType<DataNodeModel>().FirstOrDefault(node => node.IsExecEntryNode);
        var ordered = entry is null ? new List<NodeModel>() : ExecOrder(entry);

        // Id se vydají VŠEM dopředu: zpětný tah dat, který narazí na exec uzel, se pak
        // zastaví a vydá $ref místo toho, aby jeho definici zanořil do konzumenta.
        foreach (var node in ordered)
            ctx.IdOf(node);

        var nodes = ordered
            .Select(node => (IReadOnlyDictionary<string, object?>)BuildExecNode(node, ctx, callStack))
            .ToList();

        // Editor od jednoho exec výstupu pustí jen jeden link, ale graf uložený dřív
        // (nebo ručně upravený) může mít víc. Hrana nenese cílový pin, takže by z toho
        // vypadly nerozlišitelné duplikáty — deduplikuj podle celé trojice.
        var seen = new HashSet<(string From, string Pin, string To)>();
        var edges = new List<ExportExecEdge>();
        foreach (var node in ordered)
        foreach (var (pin, output) in ExecOutputsOf(node))
        foreach (var target in ExecTargets(output))
            if (ctx.NodeIds.TryGetValue(target, out var targetId)
                && seen.Add((ctx.NodeIds[node], pin, targetId)))
                edges.Add(new ExportExecEdge { From = ctx.NodeIds[node], Pin = pin, To = targetId });

        return new ExecBody(entry is null ? null : ctx.NodeIds[entry], nodes, edges);
    }

    /// <summary>Uzel v exec tabulce — buď obyčejná definice, nebo volání funkce.</summary>
    private static Dictionary<string, object?> BuildExecNode(
        NodeModel node, ExportContext ctx, HashSet<Guid> callStack) => node switch
    {
        SubgraphNodeModel sg => BuildCall(sg, ctx, callStack),
        DataNodeModel dn => BuildDataNodeDefinition(dn, ctx, new HashSet<string>(), ctx.NodeIds[dn]),
        _ => new Dictionary<string, object?> { ["$id"] = ctx.NodeIds[node], ["$error"] = "unknown node type" },
    };

    /// <summary>
    /// Volání exec funkce. Argumenty jsou obyčejné datové vstupy, takže se taháním pozpátku
    /// vyhodnotí stejně jako u kteréhokoliv uzlu; tělo funkce leží v <c>functions</c>.
    /// </summary>
    private static Dictionary<string, object?> BuildCall(
        SubgraphNodeModel sg, ExportContext ctx, HashSet<Guid> callStack)
    {
        var result = new Dictionary<string, object?>
        {
            ["$id"] = ctx.NodeIds[sg],
            ["$call"] = ExportFunctionOf(sg, ctx, callStack),
        };

        foreach (var input in sg.Interface.Inputs)
        {
            if (sg.InputPorts.TryGetValue(input.Name, out var port))
            {
                var sources = ctx.ProducersOf(port);
                result[input.Name] = sources.Count > 0
                    ? BuildTree(sources[0].Node, ctx, new HashSet<string>(), sources[0].OutputName)
                    : null;
                continue;
            }

            var fieldValue = sg.FieldValues.TryGetValue(input.Name, out var value) ? value : input.Default;
            result[input.Name] = EmitLiteral(fieldValue, input.Type);
        }

        return result;
    }

    /// <summary>Id funkce; tělo se vyexportuje při prvním volání, další už jen odkazují.</summary>
    private static string ExportFunctionOf(
        SubgraphNodeModel sg, ExportContext ctx, HashSet<Guid> callStack)
    {
        if (ctx.Shared.FunctionIds.TryGetValue(sg.SubgraphId, out var known)) return known;

        var functionId = ctx.Shared.NextFunctionId();
        ctx.Shared.FunctionIds[sg.SubgraphId] = functionId;

        var asset = ctx.AssetIndex?.Resolve(sg.SubgraphId);
        var body = asset is null || ctx.Catalog is null
            ? null
            : LoadSubgraphBody(asset, ctx.Catalog, ctx.AssetIndex, ctx.Shared);

        var function = new ExportFunction { Id = functionId, Name = sg.SubgraphName };
        ctx.Shared.Functions.Add(function);

        if (body is null)
        {
            ctx.Shared.Errors.Add(new NedNotice(
                NedNoticeSeverity.Error, "Notice_ExportMissingSubgraph", new object?[] { sg.SubgraphName }));
            return functionId;
        }

        if (!callStack.Add(sg.SubgraphId)) return functionId;   // už se staví, druhé volání jen odkáže

        // Vlastní kontext: uzly těla mají svoje id i svoje $param bindingy.
        var bindings = sg.Interface.Inputs.ToDictionary(
            input => input.Name,
            input => new InputBinding(new Dictionary<string, object?> { ["$param"] = input.Name }),
            StringComparer.Ordinal);
        var bodyCtx = new ExportContext(
            body.Diagram, ctx.AssetIndex, ctx.Catalog, ctx.Shared, bindings);

        var inner = BuildExecBody(body.Diagram, bodyCtx, callStack);
        callStack.Remove(sg.SubgraphId);

        function.Inputs = sg.Interface.Inputs
            .Select(input => new ExportGraphInput
            {
                Name = input.Name, Type = input.Type, Default = input.Default,
                Multiple = input.Multiple,
                Description = input.Description,
            })
            .ToList();
        function.Entry = inner.Entry;
        function.Nodes = inner.Nodes;
        function.ExecEdges = inner.Edges;
        function.Outputs = FunctionOutputs(sg.Interface);
        return functionId;
    }

    /// <summary>
    /// Rozhraní funkce je deklarace jmen, typů a arit. Konkrétní hodnoty nese Return uzel
    /// v exec tabulce v místě, kde daná větev skutečně skončí.
    /// </summary>
    private static List<ExportGraphOutput> FunctionOutputs(SubgraphInterface iface) => iface.Outputs
        .Select(output => new ExportGraphOutput
        {
            Name = output.Name,
            Type = output.Type,
            Multiple = output.Multiple,
        })
        .ToList();

    private static List<NodeModel> ExecOrder(DataNodeModel entry)
    {
        var result = new List<NodeModel>();
        var visited = new HashSet<NodeModel>();

        void Walk(NodeModel node)
        {
            if (!visited.Add(node)) return;
            result.Add(node);
            foreach (var (_, output) in ExecOutputsOf(node))
            foreach (var target in ExecTargets(output))
                Walk(target);
        }

        Walk(entry);
        return result;
    }

    /// <summary>Exec výstupy uzlu. Volaná funkce má jediný, <c>Then</c>.</summary>
    private static IEnumerable<(string Pin, TypedPortModel Port)> ExecOutputsOf(NodeModel node) => node switch
    {
        DataNodeModel dn => dn.Outputs
            .Where(pair => pair.Value.DataType == TypeIds.Exec)
            .Select(pair => (pair.Key, pair.Value)),
        SubgraphNodeModel sg => sg.Outputs
            .Where(pair => pair.Value.DataType == TypeIds.Exec)
            .Select(pair => (pair.Key, pair.Value)),
        _ => Array.Empty<(string, TypedPortModel)>(),
    };

    private static IEnumerable<NodeModel> ExecTargets(PortModel output)
    {
        foreach (var link in output.Links.OfType<LinkModel>())
        {
            var source = (link.Source as SinglePortAnchor)?.Port;
            var target = (link.Target as SinglePortAnchor)?.Port;
            var other = source == output ? target : target == output ? source : null;
            if (other is TypedPortModel { DataType: TypeIds.Exec } execInput
                && execInput.Parent is NodeModel node)
                yield return node;
        }
    }

    private sealed record ExecBody(
        string? Entry,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Nodes,
        IReadOnlyList<ExportExecEdge> Edges);
}
