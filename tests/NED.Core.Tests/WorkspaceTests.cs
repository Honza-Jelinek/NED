using NED.Core.Assets;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core.Tests;

public class WorkspaceTests
{
    /// <summary>
    /// Uložený graf nese seznam packů, ze kterých pochází. Bez toho by chybějící pack
    /// při otevření udělal z uzlů placeholdery a uživatel by neměl jak zjistit proč.
    /// </summary>
    [Fact]
    public void Save_RecordsRequiredPacks()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);

        var doc = GraphPersistence.ToDocument(diagram, settings, catalog);

        var pack = Assert.Single(doc.Settings.RequiredPacks!);
        Assert.Equal("sandbox", pack.Id);
        Assert.Equal("1.0.0", pack.Version);
    }

    /// <summary>Vestavěné uzly se do RequiredPacks nepočítají — editor je má vždy.</summary>
    [Fact]
    public void Save_OmitsBuiltInPack()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        diagram.Add(catalog, TestGraph.Output);

        var doc = GraphPersistence.ToDocument(diagram, new GraphSettings());

        Assert.Null(doc.Settings.RequiredPacks);
    }

    [Fact]
    public void Save_StampsCurrentSchemaVersion()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);

        Assert.Equal(GraphDocument.CurrentSchemaVersion,
                     GraphPersistence.ToDocument(diagram, settings).SchemaVersion);
    }

    /// <summary>
    /// Klíčová záruka refaktoru: hodnota, které manifest nerozumí (starší schéma,
    /// přejmenovaný vstup), se drží stranou a při uložení se vrátí. Save nikdy neztratí data.
    /// </summary>
    [Fact]
    public void UnknownField_SurvivesLoadAndSave()
    {
        var catalog = TestGraph.Catalog();
        var json = $$"""
        {
          "SchemaVersion": 2,
          "Settings": { "Id": "44444444-4444-4444-4444-444444444444", "Kind": "Graph" },
          "Nodes": [
            {
              "Id": "n1", "X": 0, "Y": 0,
              "TypeName": "{{TestGraph.Number}}",
              "Fields": { "Value": 5, "FieldFromTheFuture": "keep me" },
              "PortModes": { "GoneInput": true }
            }
          ],
          "SubgraphNodes": [],
          "Links": []
        }
        """;

        var diagram = TestGraph.NewDiagram();
        var settings = GraphPersistence.LoadInto(diagram, GraphPersistence.Deserialize(json)!, catalog);

        var saved = Assert.Single(GraphPersistence.ToDocument(diagram, settings).Nodes);

        Assert.Equal("keep me", saved.Fields["FieldFromTheFuture"]?.ToString());
        Assert.Equal(5d, Convert.ToDouble(saved.Fields["Value"]));
        Assert.True(saved.PortModes!["GoneInput"]);
    }

    [Fact]
    public void Workspace_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "ned-tests", Guid.NewGuid().ToString("N"), "ws.json");

        WorkspaceConfig.Save(path, new Workspace
        {
            Roots = { @"C:\graphs" },
            Manifests = { @"C:\packs\a.nodes.json" },
        });

        var loaded = WorkspaceConfig.Load(path);

        Assert.Equal(new[] { @"C:\graphs" }, loaded.Roots);
        Assert.Equal(new[] { @"C:\packs\a.nodes.json" }, loaded.Manifests);
    }

    [Fact]
    public void OldStringRequiredPack_RemainsReadable()
    {
        const string json = """
        {
          "SchemaVersion": 2,
          "Settings": { "RequiredPacks": ["sandbox"] }
        }
        """;

        var document = GraphPersistence.Deserialize(json)!;

        var pack = Assert.Single(document.Settings.RequiredPacks!);
        Assert.Equal("sandbox", pack.Id);
        Assert.Null(pack.Version);
    }

    [Fact]
    public void Workspace_RoundTripsPackGenerationRecipe()
    {
        var path = Path.Combine(Path.GetTempPath(), "ned-tests", Guid.NewGuid().ToString("N"), "ws.json");
        var workspace = new Workspace
        {
            Packs =
            {
                new WorkspacePack
                {
                    Path = @"C:\packs\sandbox.nodes.json",
                    Enabled = false,
                    Generation = new WorkspacePackGeneration
                    {
                        Provider = "dotnet",
                        Source = @"C:\src\Sandbox.fsproj",
                        Options = { ["Configuration"] = "Release", ["Framework"] = "net10.0" },
                    },
                },
            },
        };

        WorkspaceConfig.Save(path, workspace);
        var loaded = WorkspaceConfig.Load(path);

        var pack = Assert.Single(loaded.Packs);
        Assert.False(pack.Enabled);
        Assert.Equal(@"C:\packs\sandbox.nodes.json", pack.Path);
        Assert.Equal("dotnet", pack.Generation?.Provider);
        Assert.Equal(@"C:\src\Sandbox.fsproj", pack.Generation?.Source);
        Assert.Equal("Release", pack.Generation?.Options["Configuration"]);
        Assert.Equal("net10.0", pack.Generation?.Options["Framework"]);
    }

    [Fact]
    public void Workspace_ExposesLegacyManifestsAsEnabledPacks()
    {
        var workspace = new Workspace
        {
            Manifests = { @"C:\packs\legacy.nodes.json" },
        };

        var pack = Assert.Single(workspace.EffectivePacks());

        Assert.Equal(@"C:\packs\legacy.nodes.json", pack.Path);
        Assert.True(pack.Enabled);
        Assert.Null(pack.Generation);
    }

    [Fact]
    public void Workspace_ExplicitPackOverridesLegacyManifestEntry()
    {
        var workspace = new Workspace
        {
            Manifests = { @"C:\packs\sandbox.nodes.json" },
            Packs =
            {
                new WorkspacePack { Path = @"C:\packs\sandbox.nodes.json", Enabled = false },
            },
        };

        var pack = Assert.Single(workspace.EffectivePacks());

        Assert.False(pack.Enabled);
    }

    /// <summary>Starší config byl holé pole kořenů — uživatel o knihovny přijít nesmí.</summary>
    [Fact]
    public void Workspace_ReadsLegacyRootArray()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ned-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "libraries.json");
        File.WriteAllText(path, """["C:\\old-graphs"]""");

        var loaded = WorkspaceConfig.Load(path);

        Assert.Equal(new[] { @"C:\old-graphs" }, loaded.Roots);
        Assert.Empty(loaded.Manifests);
    }

    /// <summary>Poškozený config nesmí shodit start — prostě se začne s prázdným workspace.</summary>
    [Fact]
    public void Workspace_CorruptFileYieldsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ned-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "libraries.json");
        File.WriteAllText(path, "{ not json");

        var loaded = WorkspaceConfig.Load(path);

        Assert.Empty(loaded.Roots);
        Assert.Empty(loaded.Manifests);
    }
}
