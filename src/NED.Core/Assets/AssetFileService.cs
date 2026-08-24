using System.Text.Json.Nodes;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core.Assets;

/// <summary>
/// Čistá logika pro souborové operace nad assets (rename, duplicate).
/// Odděleno od UI (dialogy, callbacks, tab state).
/// </summary>
public static class AssetFileService
{
    /// <summary>Odstraní neplatné znaky a ořeže jméno souboru.</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "graph" : clean;
    }

    /// <summary>Generuje unikátní jméno pro asset v adresáři (bez .nedgraph.json).</summary>
    public static string GenerateUniqueName(string dir, string baseName)
    {
        var name = baseName;
        var i = 2;
        while (File.Exists(Path.Combine(dir, name + ".nedgraph.json")))
            name = $"{baseName} {i++}";
        return name;
    }

    /// <summary>
    /// Přejmenuje asset soubor a aktualizuje Settings.Name uvnitř.
    /// Vrací (newPath, success). Při selhání vrací (null, false).
    /// </summary>
    public static (string? newPath, bool success) RenameAsset(string oldPath, string newName)
    {
        try
        {
            var dir = Path.GetDirectoryName(oldPath)!;
            var newPath = Path.Combine(dir, SanitizeFileName(newName) + ".nedgraph.json");
            if (string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase))
                return (null, false);
            if (File.Exists(newPath))
                return (null, false);

            var json = File.ReadAllText(oldPath);
            var doc = GraphPersistence.Deserialize(json);
            if (doc is not null)
            {
                doc.Settings.Name = newName;
                File.WriteAllText(oldPath, GraphPersistence.Serialize(doc));
            }
            File.Move(oldPath, newPath);
            return (newPath, true);
        }
        catch
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Duplikuje asset soubor s novým GUID a unikátním jménem.
    /// Vrací cestu k novému souboru, nebo null při selhání.
    /// </summary>
    public static string? DuplicateAsset(string sourcePath)
    {
        try
        {
            var json = File.ReadAllText(sourcePath);
            var doc = GraphPersistence.Deserialize(json);
            if (doc is null) return null;

            doc.Settings.Id = Guid.NewGuid().ToString();

            var dir = Path.GetDirectoryName(sourcePath)!;
            var baseName = Path.GetFileNameWithoutExtension(sourcePath).Replace(".nedgraph", "");
            var newName = GenerateUniqueName(dir, baseName + " copy");
            if (!string.IsNullOrWhiteSpace(doc.Settings.Name))
                doc.Settings.Name = newName;

            var newPath = Path.Combine(dir, newName + ".nedgraph.json");
            File.WriteAllText(newPath, GraphPersistence.Serialize(doc));
            return newPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Přepíše Settings.Id v souboru na nový GUID (oprava duplicitní identity po ruční
    /// kopii souboru mimo editor). Chirurgická úprava jen tohoto jednoho pole přes
    /// JsonNode — na rozdíl od DTO round-tripu zachová i neznámá/budoucí pole v souboru.
    /// Vrací nový GUID, nebo null při selhání (zamčený / read-only soubor).
    /// </summary>
    public static Guid? ReassignId(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var root = JsonNode.Parse(json)?.AsObject();
            if (root is null) return null;
            if (root["Settings"] is not JsonObject settings) return null;

            var newId = Guid.NewGuid();
            settings["Id"] = newId.ToString();
            File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return newId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Vytvoří prázdný subgraf soubor odpovídající danému rozhraní (GraphInputNode per vstup
    /// + OutputNode) a se zadaným Id — řeší stale <c>SubgraphNodeModel.SubgraphId</c> referenci
    /// (uzel referencující smazaný soubor). Interface stubu je hodnotově shodný s <paramref name="iface"/>
    /// (viz <see cref="SubgraphInterface.SameAs"/>), takže po zaindexování existující porty/linky přežijí.
    /// Vrací cestu k novému souboru, nebo null při selhání.
    /// </summary>
    public static string? CreateSubgraphStub(string dir, string name, Guid id, SubgraphInterface iface) =>
        CreateInputStub(dir, name, id, iface.Flow, instanceable: false, iface.Outputs, iface.Inputs);

    /// <summary>
    /// Vytvoří prázdný template soubor pro instanci, jejíž šablona chybí — řeší stale
    /// <c>InstanceData.TemplateId</c> referenci. Na rozdíl od subgrafu (interface se odvozuje
    /// z typovaných portů/linků) tady známe jen jména a textové hodnoty z <c>InstanceData.Values</c>
    /// — typy stub odhadnout nemůže, pole vytvoří jako string; uživatel typ doladí
    /// v Details panelu. Vrací cestu k novému souboru, nebo null při selhání.
    /// </summary>
    public static string? CreateTemplateStub(string dir, string name, Guid id, IReadOnlyDictionary<string, string> values)
    {
        var inputs = values.Select((kv, i) => new SubgraphInput
        {
            Name = kv.Key,
            Type = TypeIds.String,
            Exposure = InputExposure.Field,
            Default = kv.Value,
            Order = i,
        }).ToList();
        return CreateInputStub(
            dir, name, id, GraphFlow.Data, instanceable: true, Array.Empty<SubgraphOutput>(), inputs);
    }

    private static string? CreateInputStub(
        string dir, string name, Guid id, GraphFlow flow, bool instanceable,
        IReadOnlyList<SubgraphOutput> outputs, IReadOnlyList<SubgraphInput> inputs)
    {
        try
        {
            var doc = new GraphDocument
            {
                SchemaVersion = GraphDocument.CurrentSchemaVersion,
                Settings = new GraphSettingsDto
                {
                    Id = id.ToString(),
                    Name = name,
                    Flow = flow == GraphFlow.Data ? null : flow.ToString(),
                    Instanceable = instanceable,
                    Outputs = outputs.Count == 0
                        ? null
                        : outputs
                            .Select(o => new GraphOutputDto
                            {
                                Id = o.Id, Name = o.Name, Type = o.Type, Multiple = o.Multiple,
                            })
                            .ToList(),
                },
            };

            var y = 80.0;
            foreach (var inp in inputs)
            {
                doc.Nodes.Add(new GraphNodeDto
                {
                    Id = Guid.NewGuid().ToString(),
                    X = 120,
                    Y = y,
                    TypeName = BuiltInIds.GraphInput,
                    Fields = new Dictionary<string, object?>
                    {
                        [BuiltInIds.GraphInputName] = inp.Name,
                        [BuiltInIds.GraphInputTypeName] = inp.Type,
                        [BuiltInIds.GraphInputMultiple] = inp.Multiple,
                        [BuiltInIds.GraphInputExposure] = inp.Exposure.ToString(),
                        [BuiltInIds.GraphInputDefault] = inp.Default ?? "",
                        [BuiltInIds.GraphInputOrder] = inp.Order,
                        [BuiltInIds.GraphInputDescription] = inp.Description ?? "",
                    },
                });
                y += 140;
            }

            // Sink jen v datovém toku. Exec graf hodnoty vrací Return uzly, které stub
            // nezakládá — nemá kam je připojit a exec entry taky nevytváří.
            if (flow == GraphFlow.Data)
                doc.Nodes.Add(new GraphNodeDto
                {
                    Id = Guid.NewGuid().ToString(),
                    X = 700,
                    Y = 220,
                    TypeName = BuiltInIds.Output,
                });

            var uniqueName = GenerateUniqueName(dir, SanitizeFileName(name));
            var path = Path.Combine(dir, uniqueName + ".nedgraph.json");
            File.WriteAllText(path, GraphPersistence.Serialize(doc));
            return path;
        }
        catch
        {
            return null;
        }
    }
}
