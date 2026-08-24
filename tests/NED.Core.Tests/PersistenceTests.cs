using NED.Core.Persistence;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using Blazor.Diagrams.Core.Models;

namespace NED.Core.Tests;

/// <summary>
/// Charakterizační testy persistence. Popisují DNEŠNÍ chování a musí projít i po
/// přechodu na manifest — formát souboru se změnit smí, invarianty ne.
/// </summary>
public class PersistenceTests
{
    /// <summary>
    /// Save → load → save je identita. Nejlevnější síť na regrese persistence:
    /// cokoliv, co se při načtení ztratí, se v druhé serializaci projeví.
    /// </summary>
    [Fact]
    public void RoundTrip_IsIdentity()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);

        var first = GraphPersistence.Serialize(GraphPersistence.ToDocument(diagram, settings));

        var reloaded = TestGraph.NewDiagram();
        var reloadedSettings = GraphPersistence.LoadInto(
            reloaded, GraphPersistence.Deserialize(first)!, catalog);

        var second = GraphPersistence.Serialize(GraphPersistence.ToDocument(reloaded, reloadedSettings));

        Assert.Equal(first, second);
    }

    /// <summary>Načtený graf má stejný počet uzlů i linků — round-trip nesmí nic tiše zahodit.</summary>
    [Fact]
    public void RoundTrip_PreservesNodesAndLinks()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);

        var reloaded = TestGraph.NewDiagram();
        GraphPersistence.LoadInto(reloaded,
            GraphPersistence.Deserialize(GraphPersistence.Serialize(GraphPersistence.ToDocument(diagram, settings)))!,
            catalog);

        Assert.Equal(diagram.Nodes.Count, reloaded.Nodes.Count);
        Assert.Equal(diagram.Links.Count, reloaded.Links.Count);
    }

    /// <summary>Skalární hodnoty polí přežijí round-trip.</summary>
    [Fact]
    public void RoundTrip_PreservesFieldValues()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);

        var reloaded = TestGraph.NewDiagram();
        GraphPersistence.LoadInto(reloaded,
            GraphPersistence.Deserialize(GraphPersistence.Serialize(GraphPersistence.ToDocument(diagram, settings)))!,
            catalog);

        var numbers = reloaded.Nodes.OfType<DataNodeModel>()
            .Where(n => n.TypeId == TestGraph.Number)
            .Select(n => Convert.ToDouble(n.Values["Value"]))
            .OrderBy(v => v).ToList();

        Assert.Equal(new[] { 2d, 3d }, numbers);
    }

    [Fact]
    public void RoundTrip_PreservesLinksFromTwoDifferentOutputs()
    {
        var catalog = MultiOutputCatalog();
        var diagram = TestGraph.NewDiagram();
        var producer = diagram.Add(catalog, "flow/Producer");
        var consumer = diagram.Add(catalog, TestGraph.AddNode);
        diagram.Links.Add(new LinkModel(producer.Outputs["Index"], consumer.Input("A")));
        diagram.Links.Add(new LinkModel(producer.Outputs["Completed"], consumer.Input("B")));
        var settings = new GraphSettings();

        var first = GraphPersistence.Serialize(GraphPersistence.ToDocument(diagram, settings));
        var document = GraphPersistence.Deserialize(first)!;
        Assert.Equal(new[] { "Completed", "Index" },
            document.Links.Select(link => link.FromPort).OrderBy(name => name));

        var reloaded = TestGraph.NewDiagram();
        var reloadedSettings = GraphPersistence.LoadInto(reloaded, document, catalog);
        var second = GraphPersistence.Serialize(GraphPersistence.ToDocument(reloaded, reloadedSettings));

        Assert.Equal(first, second);
    }

    [Fact]
    public void SecondaryOutput_ExtendsComeFromItsOutputType()
    {
        var catalog = MultiOutputCatalog();
        var descriptor = catalog.Resolve("flow/Producer")!;
        var node = new DataNodeModel(descriptor, catalog: catalog);

        Assert.Contains("flow/Base", node.Outputs["Completed"].Extends);
        Assert.DoesNotContain("flow/Producer", node.Outputs["Completed"].Extends);
    }

    private static NedCatalog MultiOutputCatalog()
    {
        var sandbox = NED.Manifest.Generator.ManifestBuilder.Build(typeof(Sandbox.Add).Assembly, out _);
        var flow = new NodeManifest
        {
            Pack = new PackInfo { Id = "flow", Version = "1.0.0" },
            Types =
            {
                new NodeTypeDescriptor { Id = "flow/Base", Name = "Base" },
                new NodeTypeDescriptor
                {
                    Id = "flow/Child", Name = "Child", Extends = { "flow/Base" },
                },
                new NodeTypeDescriptor
                {
                    Id = "flow/Producer", Name = "Producer",
                    Outputs =
                    {
                        new NodeOutputDescriptor { Name = "Index", Type = TypeIds.Int },
                        new NodeOutputDescriptor { Name = "Completed", Type = "flow/Child" },
                    },
                },
            },
        };
        return new NedCatalog(new[] { sandbox, flow });
    }

    /// <summary>
    /// Uzel s neznámým typem (chybí assembly) se nesmí tiše zahodit — stane se z něj
    /// <see cref="MissingNodeModel"/> a jeho DTO projde uložením beze změny.
    /// </summary>
    [Fact]
    public void MissingNodeType_SurvivesRoundTripUnchanged()
    {
        const string json = """
        {
          "Settings": { "Id": "22222222-2222-2222-2222-222222222222", "Kind": "Graph" },
          "Nodes": [
            {
              "Id": "n1",
              "X": 10,
              "Y": 20,
              "TypeName": "Ghost.Type.That.Does.Not.Exist",
              "Fields": { "Whatever": 42, "Text": "keep me" }
            }
          ],
          "SubgraphNodes": [],
          "Links": []
        }
        """;

        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var settings = GraphPersistence.LoadInto(diagram, GraphPersistence.Deserialize(json)!, catalog);

        var missing = Assert.Single(diagram.Nodes.OfType<MissingNodeModel>());
        Assert.Equal("Ghost.Type.That.Does.Not.Exist", missing.Dto.TypeName);

        var saved = GraphPersistence.ToDocument(diagram, settings);
        var dto = Assert.Single(saved.Nodes);
        Assert.Equal("Ghost.Type.That.Does.Not.Exist", dto.TypeName);
        Assert.Equal(2, dto.Fields.Count);
        Assert.Equal(10, dto.X);
        Assert.Equal(20, dto.Y);
    }

    /// <summary>Tok se u dataflow grafu nezapisuje, aby se nezměnily existující soubory.</summary>
    [Fact]
    public void DataFlow_IsNotWrittenToFile()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);

        var data = GraphPersistence.ToDocument(diagram, settings);
        settings.Flow = GraphFlow.Exec;
        var exec = GraphPersistence.ToDocument(diagram, settings);

        Assert.Null(data.Settings.Flow);
        Assert.Equal("Exec", exec.Settings.Flow);
    }
}
