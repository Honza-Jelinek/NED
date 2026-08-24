using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions;
using NED.Abstractions.Manifest;
using Blazor.Diagrams.Options;
using NED.Core.Assets;
using NED.Core;
using NED.Core.Manifest;

namespace NED.Core.Persistence;

/// <summary>
/// Export grafu: rekurzivní průchod od OutputNode po linkách → čistý strom bez pozic,
/// který se předá zvolenému <see cref="IExportTranslator"/> (default = vestavěný JSON).
/// SubgraphNode se <b>inlinuje</b> — hranice zmizí, výstup je plochý strom.
/// </summary>
public static partial class GraphExporter
{
    public static string Export(BlazorDiagram diagram, GraphSettings settings,
                                AssetIndex? assetIndex = null, NedCatalog? catalog = null)
        => Export(diagram, settings, out _, assetIndex, catalog);

    /// <summary>Jako <see cref="Export(BlazorDiagram,GraphSettings,AssetIndex?,NedCatalog?)"/>, navíc
    /// vrací chyby narazené během exportu (chybějící/nenačitatelné subgrafy) — jeden Problems-panel řádek na chybu.</summary>
    public static string Export(BlazorDiagram diagram, GraphSettings settings, out IReadOnlyList<NedNotice> errors,
                                AssetIndex? assetIndex = null, NedCatalog? catalog = null)
    {
        var shared = new ExportShared();
        var inputs = ExportInputs(diagram);
        var bindings = new Dictionary<string, InputBinding>(StringComparer.Ordinal);
        foreach (var input in inputs)
            bindings[input.Name] = new InputBinding(
                new Dictionary<string, object?> { ["$param"] = input.Name });

        if (settings.IsExec)
        {
            var execContext = new ExportContext(diagram, assetIndex, catalog, shared, bindings);
            var execModel = BuildExecModel(diagram, execContext, catalog);
            execModel.Inputs = inputs;
            execModel.Outputs = DeclareOutputs(settings.Outputs);
            errors = shared.Errors;
            var execTranslator = catalog?.FindTranslator(settings.ExportTranslator) ?? new DefaultJsonExportTranslator();
            return execTranslator.Translate(execModel);
        }

        var ctx = new ExportContext(diagram, assetIndex, catalog, shared, bindings);
        var outputNode = diagram.Nodes.OfType<DataNodeModel>().FirstOrDefault(n => n.IsOutputNode);

        // Stromy se musí postavit DŘÍV než ExportPacks — ten čte type id posbíraná
        // během stavby. V inicializátoru objektu by běžel první a packy by vyšly prázdné.
        var outputs = ValueOutputs(settings.Outputs, outputNode, ctx, new HashSet<string>());

        var model = new ExportModel
        {
            Packs = ExportPacks(shared.UsedTypeIds, catalog),
            Inputs = inputs,
            Outputs = outputs,
        };

        errors = shared.Errors;
        var translator = catalog?.FindTranslator(settings.ExportTranslator) ?? new DefaultJsonExportTranslator();
        return translator.Translate(model);
    }

    private static IReadOnlyList<ExportGraphInput> ExportInputs(BlazorDiagram diagram) => diagram.Nodes
        .OfType<DataNodeModel>()
        .Where(node => node.IsGraphInputNode)
        .OrderBy(node => node.Values.TryGetValue(BuiltInIds.GraphInputOrder, out var order)
            && int.TryParse(order?.ToString(), out var parsed) ? parsed : 0)
        .Select(node => new ExportGraphInput
        {
            Name = node.ValueAsString(BuiltInIds.GraphInputName) ?? "",
            Type = node.ValueAsString(BuiltInIds.GraphInputTypeName) ?? TypeIds.Any,
            Multiple = bool.TryParse(node.ValueAsString(BuiltInIds.GraphInputMultiple), out var multiple) && multiple,
            Default = node.ValueAsString(BuiltInIds.GraphInputDefault),
            Description = node.ValueAsString(BuiltInIds.GraphInputDescription),
        })
        .ToList();

    public static string ExportInstance(InstanceData instance, AssetIndex assetIndex, NedCatalog catalog)
    {
        var shared = new ExportShared();

        var asset = assetIndex.Resolve(instance.TemplateId)
            ?? throw new InvalidOperationException($"Template {instance.TemplateId} not found.");

        var body = LoadSubgraphBody(asset, catalog, assetIndex, shared)
            ?? throw new InvalidOperationException("Cannot parse template file.");

        var guardStack = new HashSet<string> { instance.TemplateId.ToString() };

        var bindings = new Dictionary<string, InputBinding>();
        var bodyCtxForBindings = new ExportContext(body.Diagram, assetIndex, catalog, shared);
        foreach (var inp in asset.Interface.Inputs)
        {
            var val = instance.Values.TryGetValue(inp.Name, out var v) ? v : inp.Default ?? "";

            if (val.StartsWith("sg:") && Guid.TryParse(val.AsSpan(3), out var sgId))
            {
                var sgAsset = assetIndex.Resolve(sgId);
                if (sgAsset is not null)
                {
                    var nestedSg = new SubgraphNodeModel(sgAsset);
                    var inlined = InlineSubgraph(nestedSg, bodyCtxForBindings, guardStack);
                    bindings[inp.Name] = new InputBinding(inlined);
                    continue;
                }
            }

            bindings[inp.Name] = new InputBinding(val);
        }

        var outputNode = body.Diagram.Nodes.OfType<DataNodeModel>()
            .FirstOrDefault(n => n.IsOutputNode);
        var bodyCtx = new ExportContext(body.Diagram, assetIndex, catalog, shared, bindings);
        var declared = asset.Interface.Outputs
            .Select(o => new GraphOutput { Id = o.Id, Name = o.Name, Type = o.Type, Multiple = o.Multiple })
            .ToList();

        var outputs = ValueOutputs(declared, outputNode, bodyCtx, guardStack);
        var model = new ExportModel
        {
            Packs = ExportPacks(shared.UsedTypeIds, catalog),
            Outputs = outputs,
        };

        var translator = catalog.FindTranslator(null); // šablony nemají vlastní volbu translatoru
        return translator.Translate(model);
    }

    // ── Core: rekurzivní stavba stromu ──────────────────────

    private static Dictionary<string, object?> BuildTree(
        NodeModel node, ExportContext ctx, HashSet<string> guardStack, string? outputName = null)
    {
        Dictionary<string, object?> result;
        if (node is DataNodeModel dn)
            result = BuildDataNode(dn, ctx, guardStack);
        else if (node is SubgraphNodeModel sg)
            result = InlineSubgraph(sg, ctx, guardStack, outputName);

        // Placeholder za nerozpoznaný typ — export označí chybu, ale nespadne.
        else if (node is MissingNodeModel mn)
        {
            ctx.Shared.UsedTypeIds.Add(mn.Dto.TypeName);
            result = new Dictionary<string, object?> { ["$error"] = "missing node type", ["$type"] = mn.Dto.TypeName };
        }
        else result = new Dictionary<string, object?> { ["$error"] = "unknown node type" };

        if (outputName is not null && outputName != BuiltInIds.DefaultOutput)
            result["$output"] = outputName;
        return result;
    }

    private static Dictionary<string, object?> BuildDataNode(
        DataNodeModel node, ExportContext ctx, HashSet<string> guardStack)
    {
        ctx.Shared.UsedTypeIds.Add(node.TypeId);

        // Druhé setkání s týmž uzlem → odkaz. Musí to být PŘED větví pro parametr subgrafu:
        // ta jinak resolvuje binding znovu a u PrecomputedTree vrátí tentýž slovník podruhé,
        // takže by se v exportu objevilo jedno $id dvakrát.
        if (ctx.NodeIds.TryGetValue(node, out var existingId))
        {
            var reference = new Dictionary<string, object?> { ["$ref"] = existingId };
            if (ctx.Visiting.Contains(node)) reference["$cycle"] = true;
            return reference;
        }

        // Parametr subgrafu → resolvuj z binding contextu. Vlastní $id nedostává (uzel v exportu
        // nevzniká), ale zapamatuje si kořen toho, čím byl nahrazen — na ten pak míří další odkazy.
        if (node.IsGraphInputNode && ctx.Bindings is not null)
        {
            var resolved = ResolveGraphInput(node, ctx, guardStack);
            if (RootIdOf(resolved) is { } boundId) ctx.NodeIds[node] = boundId;
            return resolved;
        }

        var id = ctx.Shared.NextId();
        ctx.NodeIds[node] = id;
        return BuildDataNodeDefinition(node, ctx, guardStack, id);
    }

    private static Dictionary<string, object?> BuildDataNodeDefinition(
        DataNodeModel node, ExportContext ctx, HashSet<string> guardStack, string id)
    {
        ctx.Shared.UsedTypeIds.Add(node.TypeId);
        var result = new Dictionary<string, object?> { ["$id"] = id, ["$type"] = node.TypeId };
        ctx.Visiting.Add(node);

        // Každý vstup: port režim → recurse do producerů; field režim → skalární hodnota.
        // (Přepnutí port↔pole je per-node, proto jdeme přes InputDefs, ne přes manifest.)
        foreach (var input in node.InputDefs)
        {
            if (input.DataType == TypeIds.Exec) continue;
            if (input.AsPort && input.Port is not null)
            {
                var sources = ctx.ProducersOf(input.Port);
                result[input.Name] = input.Multiple
                    ? sources.Select(s => BuildElement(s, ctx, guardStack)).ToArray()
                    : (sources.Count > 0
                        ? BuildTree(sources[0].Node, ctx, guardStack, sources[0].OutputName)
                        : null);
            }
            else if (input.Complex)
            {
                // Doménový vstup ve field režimu: vybraný typ → literál, "sg:GUID" → inline subgraf.
                var sel = node.ValueAsString(input.Name) ?? "";
                if (sel.StartsWith("sg:") && Guid.TryParse(sel.AsSpan(3), out var sgId)
                    && ctx.AssetIndex?.Resolve(sgId) is { } asset)
                {
                    result[input.Name] = InlineSubgraph(new SubgraphNodeModel(asset), ctx, guardStack);
                }
                else
                {
                    result[input.Name] = EmitLiteral(sel, input.DataType);
                }
            }
            else
            {
                result[input.Name] = ValueFormat.ForExport(
                    node.Values.TryGetValue(input.Name, out var v) ? v : null, input.DataType);
            }
        }

        ctx.Visiting.Remove(node);
        return result;
    }

    /// <summary>Čím je parametr subgrafu nahrazen — hotový strom, producer v rodiči, nebo literál.</summary>
    private static Dictionary<string, object?> ResolveGraphInput(
        DataNodeModel node, ExportContext ctx, HashSet<string> guardStack)
    {
        var paramName = node.ValueAsString(BuiltInIds.GraphInputName) ?? "";
        if (!ctx.Bindings!.TryGetValue(paramName, out var binding))
        {
            // nepřipojený + žádný binding → default
            return EmitLiteral(node.ValueAsString(BuiltInIds.GraphInputDefault), node.OutputType);
        }

        if (binding.PrecomputedTree is not null) return binding.PrecomputedTree;
        if (binding.Producer is not null)
            return BuildTree(binding.Producer.Node, binding.ParentContext!, guardStack,
                binding.Producer.OutputName);
        return EmitLiteral(binding.Value, node.OutputType);
    }

    // ── Inlining subgrafu ───────────────────────────────────

    private static Dictionary<string, object?> InlineSubgraph(
        SubgraphNodeModel sg, ExportContext parentCtx, HashSet<string> guardStack,
        string? consumedOutput = null)
    {
        // Klíč nese i jméno výstupu: dva porty téhož subgraf uzlu vracejí RŮZNÉ stromy.
        var cacheKey = (sg, consumedOutput ?? BuiltInIds.DefaultOutput);
        if (parentCtx.InlinedSubgraphRootIds.TryGetValue(cacheKey, out var existingRootId))
            return new Dictionary<string, object?> { ["$ref"] = existingRootId };

        var guardKey = sg.SubgraphId.ToString();
        if (!guardStack.Add(guardKey))
            return new Dictionary<string, object?> { ["$error"] = "cycle", ["$subgraph"] = sg.SubgraphName };

        // Načti tělo subgrafu ze souboru (cachováno per-export v ExportShared.BodyCache).
        var asset = parentCtx.AssetIndex?.Resolve(sg.SubgraphId);
        if (asset is null || parentCtx.Catalog is null)
        {
            guardStack.Remove(guardKey);
            parentCtx.Shared.Errors.Add(new NedNotice(NedNoticeSeverity.Error, "Notice_ExportMissingSubgraph", new object?[] { sg.SubgraphName }));
            return new Dictionary<string, object?> { ["$error"] = "missing subgraph", ["$id"] = guardKey };
        }

        var body = LoadSubgraphBody(asset, parentCtx.Catalog, parentCtx.AssetIndex, parentCtx.Shared);
        if (body is null)
        {
            guardStack.Remove(guardKey);
            parentCtx.Shared.Errors.Add(new NedNotice(NedNoticeSeverity.Error, "Notice_ExportSubgraphLoadFailed", new object?[] { sg.SubgraphName }));
            return new Dictionary<string, object?> { ["$error"] = "cannot load subgraph", ["$id"] = guardKey };
        }

        // Postav binding context: GraphInput[name] → producer v rodiči / Field value
        var bindings = new Dictionary<string, InputBinding>();
        foreach (var inp in sg.Interface.Inputs)
        {
            if (inp.Exposure == InputExposure.Port
                && sg.InputPorts.TryGetValue(inp.Name, out var port))
            {
                var producers = parentCtx.ProducersOf(port);
                if (producers.Count > 0)
                {
                    bindings[inp.Name] = new InputBinding(producers[0], parentCtx);
                    continue;
                }
            }

            // Field value nebo nepřipojený port → literál
            var fieldVal = sg.FieldValues.TryGetValue(inp.Name, out var fv) ? fv : inp.Default ?? "";

            // sg:GUID v Field hodnotě → inline vnořený subgraf
            if (fieldVal.StartsWith("sg:") && Guid.TryParse(fieldVal.AsSpan(3), out var nestedId))
            {
                var nestedAsset = parentCtx.AssetIndex?.Resolve(nestedId);
                if (nestedAsset is not null)
                {
                    var nestedSg = new SubgraphNodeModel(nestedAsset);
                    var inlined = InlineSubgraph(nestedSg, parentCtx, guardStack);
                    bindings[inp.Name] = new InputBinding(inlined);
                    continue;
                }
            }

            bindings[inp.Name] = new InputBinding(fieldVal);
        }

        // Subgraf může deklarovat víc návratů; inlinuje se ten, jehož port volající konzumuje.
        var sink = body.Diagram.Nodes.OfType<DataNodeModel>().FirstOrDefault(n => n.IsOutputNode);
        var wanted = sg.Interface.PortOutputs()
            .FirstOrDefault(pair => pair.PortName == (consumedOutput ?? BuiltInIds.DefaultOutput)).Output
            ?? sg.Interface.Outputs.FirstOrDefault();

        object? value = wanted is null
            ? new Dictionary<string, object?> { ["$error"] = "subgraph declares no output" }
            : BuildOutputValue(
                new GraphOutput { Id = wanted.Id, Name = wanted.Name, Type = wanted.Type, Multiple = wanted.Multiple },
                sink, new ExportContext(body.Diagram, parentCtx.AssetIndex, parentCtx.Catalog, parentCtx.Shared, bindings),
                guardStack);

        // Volající čeká slovník (BuildTree ho zanořuje do svého vstupu). Pole z polové
        // deklarace se do něj zabalí jako $list, aby $spread v rodiči měl co rozbalit.
        var result = value switch
        {
            Dictionary<string, object?> tree => tree,
            object?[] items => new Dictionary<string, object?> { ["$list"] = items },
            _ => new Dictionary<string, object?> { ["$error"] = "empty subgraph output" },
        };

        guardStack.Remove(guardKey);
        if (RootIdOf(result) is { } rootId)
            parentCtx.InlinedSubgraphRootIds[cacheKey] = rootId;
        return result;
    }

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Jeden prvek polového vstupu. Producent polové arity přispívá svými prvky, ne sebou —
    /// jinak by z pole do pole vznikla matice. Rozbalení se zapisuje staticky, protože arita
    /// je na portu známá; interpret ji nemusí hádat z hodnoty.
    /// </summary>
    private static object? BuildElement(ProducerRef source, ExportContext ctx, HashSet<string> guardStack)
    {
        var tree = BuildTree(source.Node, ctx, guardStack, source.OutputName);
        return source.Port.Multiple
            ? new Dictionary<string, object?> { ["$spread"] = tree }
            : tree;
    }

    /// <summary>Deklarace bez hodnot — exec tok, kde hodnoty vydávají Return uzly.</summary>
    private static IReadOnlyList<ExportGraphOutput> DeclareOutputs(IEnumerable<GraphOutput> declared) =>
        declared
            .Select(output => new ExportGraphOutput
            {
                Name = output.Name, Type = output.Type, Multiple = output.Multiple,
            })
            .ToList();

    /// <summary>
    /// Deklarace i s hodnotami vytaženými ze sinku — kořen datového grafu i instance šablony.
    /// Nezapojený skalár vydá <c>null</c>, nezapojená polová deklarace prázdné pole: arita
    /// slíbila pole a konzument by jinak řešil dva různé druhy „nic".
    /// </summary>
    private static IReadOnlyList<ExportGraphOutput> ValueOutputs(
        IReadOnlyList<GraphOutput> declared, DataNodeModel? sink,
        ExportContext ctx, HashSet<string> guardStack) =>
        declared
            .Select(output => new ExportGraphOutput
            {
                Name = output.Name,
                Type = output.Type,
                Multiple = output.Multiple,
                HasValue = true,
                Value = BuildOutputValue(output, sink, ctx, guardStack),
            })
            .ToList();

    /// <summary>
    /// Hodnota jedné deklarace. U polové arity je to pole <b>všech</b> producentů — brát
    /// jen prvního by tiše zahodilo zbytek a konzument by přitom dostal něco, co považuje
    /// za pole; producent, který sám vydává pole, se rozbalí (<c>$spread</c>).
    /// </summary>
    private static object? BuildOutputValue(
        GraphOutput declared, DataNodeModel? sink, ExportContext ctx, HashSet<string> guardStack)
    {
        var input = sink?.InputDefs.FirstOrDefault(i => i.Name == declared.Name);
        var sources = input?.Port is { } port ? ctx.ProducersOf(port) : new List<ProducerRef>();

        if (declared.Multiple)
            return sources.Select(s => BuildElement(s, ctx, guardStack)).ToArray();

        return sources.Count > 0
            ? BuildTree(sources[0].Node, ctx, guardStack, sources[0].OutputName)
            : null;
    }

    private static Dictionary<string, object?> EmitLiteral(string? raw, string typeId) => new()
    {
        ["$type"] = "$literal",
        ["value"] = string.IsNullOrEmpty(raw) ? null : ValueFormat.Parse(raw, typeId, raw),
    };

    private static string? RootIdOf(IReadOnlyDictionary<string, object?> tree) =>
        tree.TryGetValue("$id", out var id) ? id as string
        : tree.TryGetValue("$ref", out var reference) ? reference as string
        : null;

    private static IReadOnlyList<ExportPack> ExportPacks(
        IEnumerable<string> typeIds, NedCatalog? catalog)
    {
        return typeIds
            .Select(typeId => catalog?.PackOf(typeId)
                ?? (typeId.Contains('/') ? typeId[..typeId.IndexOf('/')] : null))
            .Where(packId => !string.IsNullOrEmpty(packId) && packId != BuiltInIds.Pack)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(packId => packId, StringComparer.Ordinal)
            .Select(packId => new ExportPack
            {
                Id = packId!,
                Version = catalog?.Packs.FirstOrDefault(pack => pack.Id == packId)?.Version,
            })
            .ToList();
    }

    /// <summary>Načte + rozparsuje tělo subgrafu ze souboru, cachováno per-export podle asset Id
    /// (subgraf použitý N× v jednom exportu se čte z disku jen jednou). Cachuje i selhání.</summary>
    private static SubgraphBody? LoadSubgraphBody(AssetEntry asset, NedCatalog catalog, AssetIndex? assetIndex, ExportShared shared)
    {
        if (shared.BodyCache.TryGetValue(asset.Id, out var cached))
            return cached;

        SubgraphBody? body;
        try
        {
            var json = File.ReadAllText(asset.Path);
            var doc = GraphPersistence.Deserialize(json);
            if (doc is null)
            {
                body = null;
            }
            else
            {
                var opts = new BlazorDiagramOptions();
                var diagram = new BlazorDiagram(opts);
                GraphPersistence.LoadInto(diagram, doc, catalog, assetIndex);
                body = new SubgraphBody(diagram);
            }
        }
        catch
        {
            body = null;
        }

        shared.BodyCache[asset.Id] = body;
        return body;
    }

    // ── Kontextové typy ─────────────────────────────────────

    /// <summary>Stav sdílený napříč celým jedním voláním Export/ExportInstance — cache těl
    /// subgrafů (čti z disku max. jednou) a seznam chyb pro souhrnné hlášení po exportu.</summary>
    private sealed class ExportShared
    {
        private int _nextId;

        public Dictionary<Guid, SubgraphBody?> BodyCache { get; } = new();
        public List<NedNotice> Errors { get; } = new();
        public HashSet<string> UsedTypeIds { get; } = new(StringComparer.Ordinal);

        /// <summary>Vyexportovaná těla funkcí a mapa asset id → id funkce (jedno tělo na subgraf).</summary>
        public List<ExportFunction> Functions { get; } = new();
        public Dictionary<Guid, string> FunctionIds { get; } = new();

        private int _nextFunctionId;

        public string NextId() => $"n{++_nextId}";
        public string NextFunctionId() => $"fn{++_nextFunctionId}";
    }

    private sealed class ExportContext
    {
        public BlazorDiagram Diagram { get; }
        public AssetIndex? AssetIndex { get; }
        public NedCatalog? Catalog { get; }
        public ExportShared Shared { get; }
        public Dictionary<string, InputBinding>? Bindings { get; }
        public HashSet<DataNodeModel> Visiting { get; } = new();
        public Dictionary<NodeModel, string> NodeIds { get; } = new();

        /// <summary>Id uzlu; vydá nové, pokud ještě žádné nemá.</summary>
        public string IdOf(NodeModel node) =>
            NodeIds.TryGetValue(node, out var existing) ? existing : NodeIds[node] = Shared.NextId();
        public Dictionary<(SubgraphNodeModel Node, string Output), string> InlinedSubgraphRootIds { get; } = new();

        private Dictionary<PortModel, List<ProducerRef>>? _producersByPort;

        public ExportContext(BlazorDiagram diagram, AssetIndex? assetIndex, NedCatalog? catalog,
            ExportShared shared, Dictionary<string, InputBinding>? bindings = null)
        {
            Diagram = diagram;
            AssetIndex = assetIndex;
            Catalog = catalog;
            Shared = shared;
            Bindings = bindings;
        }

        /// <summary>Producery portu — lazy index všech linků diagramu postavený jednou
        /// (O(links) celkem místo O(links) na každý dotaz).</summary>
        public List<ProducerRef> ProducersOf(PortModel port)
        {
            if (_producersByPort is null)
            {
                _producersByPort = new Dictionary<PortModel, List<ProducerRef>>();
                foreach (var link in Diagram.Links.OfType<LinkModel>())
                {
                    var a = (link.Source as SinglePortAnchor)?.Port;
                    var b = (link.Target as SinglePortAnchor)?.Port;
                    if (a is not null && b is not null)
                    {
                        if (!_producersByPort.TryGetValue(a, out var la)) _producersByPort[a] = la = new();
                        la.Add(ProducerOf(b));
                        if (!_producersByPort.TryGetValue(b, out var lb)) _producersByPort[b] = lb = new();
                        lb.Add(ProducerOf(a));
                    }
                }
            }
            return _producersByPort.TryGetValue(port, out var list) ? list : new List<ProducerRef>();
        }

        private static ProducerRef ProducerOf(PortModel port) => new(
            port.Parent,
            port.Parent switch
            {
                DataNodeModel dn => dn.Outputs.FirstOrDefault(kv => kv.Value == port).Key,
                SubgraphNodeModel sg => sg.Outputs.FirstOrDefault(kv => kv.Value == port).Key,
                MissingNodeModel mn => mn.Outputs.FirstOrDefault(kv => kv.Value == port).Key,
                _ => BuiltInIds.DefaultOutput,
            } ?? BuiltInIds.DefaultOutput,
            (TypedPortModel)port);
    }

    /// <summary>Vazba pro GraphInputNode uvnitř subgrafu.</summary>
    private sealed class InputBinding
    {
        public ProducerRef? Producer { get; }
        public ExportContext? ParentContext { get; }
        public string? Value { get; }
        public Dictionary<string, object?>? PrecomputedTree { get; }

        public InputBinding(ProducerRef producer, ExportContext parentCtx)
        { Producer = producer; ParentContext = parentCtx; }

        public InputBinding(string? value) { Value = value; }

        public InputBinding(Dictionary<string, object?> precomputed)
        { PrecomputedTree = precomputed; }
    }

    private sealed record ProducerRef(NodeModel Node, string OutputName, TypedPortModel Port);

    private sealed record SubgraphBody(BlazorDiagram Diagram);
}
