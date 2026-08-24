using NED.Core.Assets;
using NED.Core.Persistence;

namespace NED.Core.Tests;

/// <summary>
/// Dočasná knihovna assetů na disku + <see cref="AssetIndex"/> nad ní.
/// Subgrafy se skládají přímo jako <see cref="GraphDocument"/> — pro test je to
/// čitelnější než stavět diagram a serializovat ho.
/// </summary>
public sealed class TempLibrary : IDisposable
{
    public string Root { get; }
    public NedCatalog Catalog { get; }
    public AssetIndex Index { get; }

    public TempLibrary()
    {
        Root = Path.Combine(Path.GetTempPath(), "ned-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        Catalog = TestGraph.Catalog();
        var options = new NedOptions();
        options.LibraryConfig(Path.Combine(Root, "libraries.json"));

        Index = new AssetIndex(options, new NedNotifier());
        Index.AddRoot(Root);
    }

    /// <summary>Zapíše dokument jako asset a přeindexuje ho.</summary>
    public string Write(string fileName, GraphDocument doc)
    {
        var path = Path.Combine(Root, fileName + ".nedgraph.json");
        File.WriteAllText(path, GraphPersistence.Serialize(doc));
        Index.UpdateFile(path);
        return path;
    }

    /// <summary>
    /// Subgraf, jehož tělo je jediný odkaz na jiný subgraf — minimální stavební kámen
    /// pro test cyklu. <paramref name="referencedId"/> null = subgraf bez odkazu.
    /// </summary>
    public static GraphDocument SubgraphReferencing(Guid id, Guid? referencedId)
    {
        var doc = new GraphDocument
        {
            Settings = new GraphSettingsDto
            {
                Id = id.ToString(),
                Name = "sub-" + id.ToString("N")[..4],
                Outputs = TestGraph.Declare(NED.Abstractions.Manifest.TypeIds.Double),
            },
            Nodes =
            {
                new GraphNodeDto { Id = "out", X = 400, Y = 0, TypeName = NED.Core.Manifest.BuiltInIds.Output },
            },
        };

        if (referencedId is { } refId)
        {
            doc.SubgraphNodes.Add(new SubgraphNodeDto { Id = "sg", X = 0, Y = 0, SubgraphId = refId.ToString() });
            doc.Links.Add(new GraphLinkDto
            {
                FromNode = "sg", FromPort = NED.Core.Manifest.BuiltInIds.DefaultOutput,
                ToNode = "out", ToPort = TestGraph.Result,
            });
        }

        return doc;
    }

    public void Dispose()
    {
        Index.Dispose();
        try { Directory.Delete(Root, recursive: true); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Temporary library cleanup failed: {ex.Message}"); }
    }
}
