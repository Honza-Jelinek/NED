using System.Text.Json;
using System.Text.Json.Serialization;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Assets;
using NED.Core.Manifest;

namespace NED.Core.Persistence;

/// <summary>Save/Load grafu do/z <see cref="GraphDocument"/>.</summary>
public static class GraphPersistence
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Port z anchoru (jen SinglePortAnchor má port; jinak null).</summary>
    private static PortModel? PortOf(Anchor? a) => (a as SinglePortAnchor)?.Port;

    // ── Save ────────────────────────────────────────────────

    public static GraphDocument ToDocument(
        BlazorDiagram diagram, GraphSettings settings, NedCatalog? catalog = null)
    {
        var doc = new GraphDocument
        {
            SchemaVersion = GraphDocument.CurrentSchemaVersion,
            Settings = new GraphSettingsDto
            {
                Id = settings.Id.ToString(),
                Name = settings.Name,
                Description = settings.Description,
                Flow = settings.Flow == GraphFlow.Data ? null : settings.Flow.ToString(),
                Outputs = settings.Outputs.Count == 0
                    ? null
                    : settings.Outputs
                        .Select(o => new GraphOutputDto
                        {
                            Id = o.Id, Name = o.Name, Type = o.Type, Multiple = o.Multiple,
                        })
                        .ToList(),
                Instanceable = settings.Instanceable,
                ExportTranslator = settings.ExportTranslator,
            },
        };

        foreach (var node in diagram.Nodes.OfType<DataNodeModel>())
        {
            var dto = new GraphNodeDto
            {
                Id = node.Id,
                X = node.Position.X,
                Y = node.Position.Y,
                TypeName = node.TypeId,
            };

            foreach (var input in node.InputDefs)
            {
                var value = node.Values.TryGetValue(input.Name, out var v) ? v : null;
                if (value is not null) dto.Fields[input.Name] = value;

                // Ulož jen režim, který se liší od výchozího (čisté soubory).
                if (input.Togglable && input.AsPort != input.DefaultAsPort)
                    (dto.PortModes ??= new())[input.Name] = input.AsPort;
            }

            // Hodnoty, kterým manifest nerozumí, projdou beze změny — save nic neztratí.
            foreach (var (key, value) in node.UnknownValues)
                dto.Fields[key] = value;
            foreach (var (key, value) in node.UnknownPortModes)
                (dto.PortModes ??= new())[key] = value;

            doc.Nodes.Add(dto);
        }

        // Placeholder uzly (chybí pack): zapiš PŮVODNÍ DTO beze změny — jen aktualizuj
        // pozici. Round-trip tak zachová Fields i PortModes.
        foreach (var mn in diagram.Nodes.OfType<MissingNodeModel>())
        {
            mn.Dto.X = mn.Position.X;
            mn.Dto.Y = mn.Position.Y;
            doc.Nodes.Add(mn.Dto);
        }

        foreach (var sgNode in diagram.Nodes.OfType<SubgraphNodeModel>())
        {
            doc.SubgraphNodes.Add(new SubgraphNodeDto
            {
                Id = sgNode.Id,
                X = sgNode.Position.X,
                Y = sgNode.Position.Y,
                SubgraphId = sgNode.SubgraphId.ToString(),
                FieldValues = new Dictionary<string, string>(sgNode.FieldValues),
                PortModes = sgNode.ExposureOverride.Count > 0
                    ? new Dictionary<string, bool>(sgNode.ExposureOverride)
                    : null,
            });
        }

        foreach (var link in diagram.Links.OfType<LinkModel>())
        {
            var p1 = PortOf(link.Source);
            var p2 = PortOf(link.Target);
            if (p1 is null || p2 is null) continue;

            // Směrově nezávisle: najdi vstupní port a k němu producera.
            var linkDto = TryBuildLinkDto(p1, p2) ?? TryBuildLinkDto(p2, p1);
            if (linkDto is not null) doc.Links.Add(linkDto);
        }

        doc.Settings.RequiredPacks = RequiredPacks(doc, catalog);

        return doc;
    }

    /// <summary>
    /// Packy, ze kterých pocházejí použité typy uzlů. Odvozuje se z dokumentu (ne z katalogu),
    /// takže se do seznamu dostane i pack chybějícího uzlu drženého jako placeholder.
    /// </summary>
    private static List<PackRequirement>? RequiredPacks(GraphDocument doc, NedCatalog? catalog)
    {
        var packs = doc.Nodes
            .Select(n => n.TypeName)
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t.Contains('/') ? t[..t.IndexOf('/')] : t)
            .Where(p => p != Manifest.BuiltInIds.Pack)   // vestavěné uzly má editor vždy
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return packs.Count > 0
            ? packs.Select(id => new PackRequirement
            {
                Id = id,
                Version = catalog?.Packs.FirstOrDefault(pack => pack.Id == id)?.Version,
            }).ToList()
            : null;
    }

    private static GraphLinkDto? TryBuildLinkDto(PortModel candidateInput, PortModel candidateOutput)
    {
        var inputName = FindInputName(candidateInput);
        if (inputName is null) return null;
        var outputName = FindOutputName(candidateOutput);
        if (outputName is null) return null;
        return new GraphLinkDto
        {
            FromNode = candidateOutput.Parent.Id,
            FromPort = outputName,
            ToNode = candidateInput.Parent.Id,
            ToPort = inputName,
        };
    }

    private static string? FindInputName(PortModel port)
    {
        if (port.Parent is DataNodeModel dn && dn.Inputs.TryGetValue(port.Id, out var input))
            return input.Name;
        // Exec pin volané funkce nežije v InputPorts (ty jsou klíčované jménem parametru).
        // Bez téhle větve by se link do funkce při uložení tiše zahodil.
        if (port.Parent is SubgraphNodeModel sg)
            return sg.ExecInput?.Id == port.Id
                ? Manifest.BuiltInIds.ExecInput
                : sg.InputPorts.FirstOrDefault(kv => kv.Value.Id == port.Id).Key;
        if (port.Parent is MissingNodeModel mn)
            return mn.InputPorts.FirstOrDefault(kv => kv.Value.Id == port.Id).Key;
        return null;
    }

    private static string? FindOutputName(PortModel port) => port.Parent switch
    {
        DataNodeModel dn => dn.Outputs.FirstOrDefault(kv => kv.Value.Id == port.Id).Key,
        SubgraphNodeModel sg => sg.Outputs.FirstOrDefault(kv => kv.Value.Id == port.Id).Key,
        MissingNodeModel mn => mn.Outputs.FirstOrDefault(kv => kv.Value.Id == port.Id).Key,
        _ => null,
    };

    public static string Serialize(GraphDocument doc) => JsonSerializer.Serialize(doc, JsonOpts);

    // ── Load ────────────────────────────────────────────────

    public static GraphDocument? Deserialize(string json) =>
        JsonSerializer.Deserialize<GraphDocument>(json, JsonOpts);

    /// <summary>Načte graf i jeho settings. Vrací rekonstruované <see cref="GraphSettings"/>.</summary>
    public static GraphSettings LoadInto(BlazorDiagram diagram, GraphDocument doc, NedCatalog catalog, AssetIndex? assetIndex = null)
    {
        diagram.Links.Clear();
        diagram.Nodes.Clear();

        var settings = new GraphSettings
        {
            Id = Guid.TryParse(doc.Settings.Id, out var gid) && gid != Guid.Empty ? gid : Guid.NewGuid(),
            Name = doc.Settings.Name ?? "",
            Description = doc.Settings.Description ?? "",
            Flow = Enum.TryParse<GraphFlow>(doc.Settings.Flow, ignoreCase: true, out var parsedFlow)
                ? parsedFlow
                : GraphFlow.Data,
            Outputs = ReadOutputs(doc.Settings.Outputs),
            Instanceable = doc.Settings.Instanceable,
            ExportTranslator = doc.Settings.ExportTranslator,
        };

        var byId = new Dictionary<string, NodeModel>();

        foreach (var n in doc.Nodes)
        {
            var descriptor = catalog.Resolve(n.TypeName);
            if (descriptor is null)
            {
                // Neznámý typ (chybí pack) → placeholder místo tichého zahození. Vstupní porty
                // odvodíme z linků mířících na uzel; DTO zůstává netknuté, takže save nic neztratí.
                var inputNames = doc.Links.Where(l => l.ToNode == n.Id).Select(l => l.ToPort);
                var outputNames = doc.Links.Where(l => l.FromNode == n.Id).Select(l => l.FromPort);
                var missing = new MissingNodeModel(n, inputNames, outputNames);
                byId[n.Id] = missing;
                diagram.Nodes.Add(missing);
                continue;
            }

            var node = new DataNodeModel(descriptor, new Point(n.X, n.Y), n.Id, catalog);

            foreach (var (name, raw) in n.Fields)
            {
                var input = node.InputDefs.FirstOrDefault(i => i.Name == name);
                if (input is null)
                {
                    // Pole, které manifest nezná (starší schéma, přejmenovaná property bez Id).
                    // Drží se stranou a při uložení se vrátí — žádná tichá ztráta.
                    node.UnknownValues[name] = raw;
                    continue;
                }
                node.Values[name] = ValueFormat.FromJson(raw, input.DataType);
            }

            // Výstupní typ parametru subgrafu se čte z hodnoty — po jejím načtení ho srovnej.
            if (node.IsGraphInputNode) node.RefreshDynamicTypes();

            // Obnov override expozice (port↔pole) PŘED připojováním linků.
            if (n.PortModes is not null)
                foreach (var (inputName, asPort) in n.PortModes)
                {
                    var input = node.InputDefs.FirstOrDefault(i => i.Name == inputName);
                    if (input is not null) node.SetExposure(input, asPort);
                    else node.UnknownPortModes[inputName] = asPort;
                }

            byId[n.Id] = node;
            diagram.Nodes.Add(node);
        }

        // Porty z deklarací musejí existovat dřív, než se začnou obnovovat linky.
        // Revalidate je pak průběžně synchronizuje při dalších editacích deklarací.
        foreach (var node in diagram.Nodes.OfType<DataNodeModel>().Where(n => n.DeclaresGraphOutputs))
            node.SyncDeclaredInputs(settings.Outputs);

        // SubgraphNode reference
        foreach (var sn in doc.SubgraphNodes)
        {
            // Reference se NIKDY tiše nezahodí. Dva důvody, proč nejde resolvovat:
            //  1) neplatný GUID string (poškozený soubor) → náhradní GUID, ať uzel má identitu,
            //  2) platný GUID, ale soubor chybí (smazaný subgraf) → Resolve vrátí null.
            // V obou postavíme uzel nad syntetickým interface odvozeným z dokumentu. Validace
            // ho nahlásí jako E4 (stale subgraph); po obnovení souboru se sám zahojí.
            var sgId = Guid.TryParse(sn.SubgraphId, out var parsed) ? parsed : Guid.NewGuid();
            var asset = assetIndex?.Resolve(sgId) ?? BuildMissingEntry(sgId, sn, doc.Links);

            var sgNode = new SubgraphNodeModel(asset, new Point(sn.X, sn.Y), sn.Id);
            foreach (var (fk, fv) in sn.FieldValues)
                sgNode.FieldValues[fk] = fv;

            // Obnov override expozice (port↔pole) PŘED připojováním linků.
            if (sn.PortModes is not null)
                foreach (var (inName, asPort) in sn.PortModes)
                    sgNode.SetExposure(inName, asPort);

            byId[sn.Id] = sgNode;
            diagram.Nodes.Add(sgNode);
        }

        foreach (var l in doc.Links)
        {
            if (!byId.TryGetValue(l.FromNode, out var fromNode)) continue;
            if (!byId.TryGetValue(l.ToNode, out var toNode)) continue;

            var fromOutput = FindOutputPort(fromNode, l.FromPort);
            if (fromOutput is null) continue;

            var targetPort = FindPortByName(toNode, l.ToPort);
            if (targetPort is null) continue;

            diagram.Links.Add(new LinkModel(fromOutput, targetPort));
        }

        return settings;
    }

    /// <summary>Deklarace ze souboru; pořadí je pořadí v poli, prázdné jméno se zahazuje.</summary>
    private static List<GraphOutput> ReadOutputs(List<GraphOutputDto>? dtos)
    {
        if (dtos is null) return new List<GraphOutput>();
        var outputs = dtos
            .Where(dto => !string.IsNullOrWhiteSpace(dto.Name))
            .Select(dto => new GraphOutput
            {
                Id = dto.Id ?? "",
                Name = dto.Name,
                Type = string.IsNullOrWhiteSpace(dto.Type) ? TypeIds.Any : dto.Type,
                Multiple = dto.Multiple,
            })
            .ToList();
        GraphOutput.EnsureUniqueIds(outputs);
        return outputs;
    }

    /// <summary>
    /// Syntetický <see cref="AssetEntry"/> pro subgraf, jehož soubor se nepodařilo resolvovat.
    /// Interface se odvodí z dokumentu: linky mířící na uzel → port vstupy, FieldValues →
    /// field vstupy, PortModes rozhoduje expozici. Typy neznáme → Any.
    /// Uzel tak zůstane na plátně (validace E4 ho nahlásí) a „Create subgraph…" fix z Problems
    /// panelu vytvoří stub přesně s tímto rozhraním.
    /// </summary>
    private static AssetEntry BuildMissingEntry(Guid sgId, SubgraphNodeDto sn, List<GraphLinkDto> links)
    {
        var linked = links.Where(l => l.ToNode == sn.Id).Select(l => l.ToPort).Distinct().ToList();

        var names = new List<string>(linked);
        foreach (var k in sn.FieldValues.Keys) if (!names.Contains(k)) names.Add(k);
        if (sn.PortModes is not null)
            foreach (var k in sn.PortModes.Keys) if (!names.Contains(k)) names.Add(k);

        var inputs = new List<SubgraphInput>();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            // Výchozí expozice NAOPAK vůči PortModes override — LoadInto pak SetExposure
            // skutečně provede a zapíše do ExposureOverride, takže PortModes přežije save.
            var isPort = sn.PortModes?.TryGetValue(name, out var ov) == true ? !ov : linked.Contains(name);
            inputs.Add(new SubgraphInput
            {
                Name = name,
                Type = TypeIds.Any,
                Exposure = isPort ? InputExposure.Port : InputExposure.Field,
                Default = sn.FieldValues.TryGetValue(name, out var dv) ? dv : "",
                Order = i,
            });
        }

        return new AssetEntry
        {
            Id = sgId,
            Path = "",
            Name = $"Missing-{sgId.ToString("N")[..8]}",
            // Jeden výstup typu Any: placeholder za smazaný subgraf si musí udržet výstupní
            // port, jinak by uložený graf při načtení přišel o linky, které z něj vedou.
            Interface = new SubgraphInterface
            {
                Inputs = inputs,
                Outputs = [new SubgraphOutput { Name = "Result", Type = TypeIds.Any }],
            },
        };
    }

    private static PortModel? FindOutputPort(NodeModel node, string name) => node switch
    {
        DataNodeModel dn => dn.Outputs.TryGetValue(name, out var port) ? port : null,
        SubgraphNodeModel sg => sg.Outputs.TryGetValue(name, out var port) ? port : null,
        MissingNodeModel mn => mn.Outputs.TryGetValue(name, out var port) ? port : null,
        _ => null,
    };

    private static PortModel? FindPortByName(NodeModel node, string name) => node switch
    {
        DataNodeModel dn => dn.Ports.FirstOrDefault(p =>
            dn.Inputs.TryGetValue(p.Id, out var input) && input.Name == name),
        SubgraphNodeModel sg => name == Manifest.BuiltInIds.ExecInput
            ? sg.ExecInput
            : sg.InputPorts.TryGetValue(name, out var port) ? port : null,
        MissingNodeModel mn => mn.InputPorts.TryGetValue(name, out var mport) ? mport : null,
        _ => null,
    };
}
