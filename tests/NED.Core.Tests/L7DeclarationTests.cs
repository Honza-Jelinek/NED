using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core.Tests;

/// <summary>
/// Deklarace návratů sedí na grafu, uzly z nich staví porty. Sem patří to, co se tím
/// změnilo: sdílený sync pro Output i Return, round-trip víc drátů do jednoho sinku,
/// a zploštění rolí na jediný tok.
/// </summary>
public sealed class L7DeclarationTests
{
    [Fact]
    public void ThreeDeclarations_SurviveSaveLoadWithAllLinks()
    {
        var catalog = TestGraph.Catalog();
        var settings = new GraphSettings
        {
            Outputs = TestGraph.Outputs(TypeIds.Double, TypeIds.Double, TypeIds.Double),
        };
        var diagram = TestGraph.NewDiagram();
        var sink = diagram.AddSink(catalog, settings.Outputs);
        foreach (var output in settings.Outputs)
            diagram.Link(diagram.Add(catalog, TestGraph.Number, values: ("Value", 1d)), sink, output.Name);

        var loaded = TestGraph.NewDiagram();
        GraphPersistence.LoadInto(loaded, GraphPersistence.ToDocument(diagram, settings), catalog);

        // Porty musí vzniknout dřív, než se obnovují linky — jinak zmizí bez hlášky.
        Assert.Equal(3, loaded.Links.Count);
        var loadedSink = Assert.Single(loaded.Nodes.OfType<DataNodeModel>(), n => n.IsOutputNode);
        Assert.Equal(
            settings.Outputs.Select(o => o.Name),
            loadedSink.InputDefs.Select(input => input.Name));
    }

    [Fact]
    public void RemovingDeclaration_DropsItsPortAndKeepsTheRest()
    {
        var catalog = TestGraph.Catalog();
        var declared = TestGraph.Outputs(TypeIds.Double, TypeIds.Double);
        var diagram = TestGraph.NewDiagram();
        var sink = diagram.AddSink(catalog, declared);
        var keep = diagram.Add(catalog, TestGraph.Number, values: ("Value", 1d));
        var drop = diagram.Add(catalog, TestGraph.Number, values: ("Value", 2d));
        diagram.Link(keep, sink, declared[0].Name);
        diagram.Link(drop, sink, declared[1].Name);

        declared.RemoveAt(1);
        sink.SyncDeclaredInputs(declared);

        Assert.Single(diagram.Links);
        Assert.DoesNotContain(sink.InputDefs, input => input.Name == TestGraph.Result + "2");
        Assert.NotNull(sink.InputDefs.Single(input => input.Name == TestGraph.Result).Port);
    }

    /// <summary>
    /// Přejmenování typu deklarace nesmí port zahodit — jinak by uživateli po každé
    /// změně typu zmizel drát a musel ho tahat znovu.
    /// </summary>
    [Fact]
    public void RetypingDeclaration_KeepsPortAndLink()
    {
        var catalog = TestGraph.Catalog();
        var declared = TestGraph.Outputs(TypeIds.Double);
        var diagram = TestGraph.NewDiagram();
        var sink = diagram.AddSink(catalog, declared);
        diagram.Link(diagram.Add(catalog, TestGraph.Number), sink, TestGraph.Result);
        var port = sink.InputDefs.Single(input => input.Name == TestGraph.Result).Port;

        declared[0].Type = TypeIds.String;
        sink.SyncDeclaredInputs(declared);

        Assert.Same(port, sink.InputDefs.Single(input => input.Name == TestGraph.Result).Port);
        Assert.Equal(TypeIds.String, port!.DataType);
        Assert.Single(diagram.Links);
    }

    /// <summary>
    /// Přejmenování je přejmenování, ne „zrušit a založit znovu". Bez stabilního id se
    /// páruje podle jména, takže by uživateli po každé opravě názvu zmizel drát.
    /// </summary>
    [Fact]
    public void RenamingDeclaration_KeepsPortAndLink()
    {
        var catalog = TestGraph.Catalog();
        var declared = TestGraph.Outputs(TypeIds.Double);
        var diagram = TestGraph.NewDiagram();
        var sink = diagram.AddSink(catalog, declared);
        diagram.Link(diagram.Add(catalog, TestGraph.Number, values: ("Value", 4d)), sink, TestGraph.Result);
        var port = sink.InputDefs.Single(input => input.Name == TestGraph.Result).Port;

        declared[0].Name = "Total";
        sink.SyncDeclaredInputs(declared);

        var input = Assert.Single(sink.InputDefs, candidate => candidate.Name == "Total");
        Assert.Same(port, input.Port);
        Assert.Equal("Total", input.Port!.Label);
        Assert.Single(diagram.Links);
        Assert.DoesNotContain(sink.InputDefs, candidate => candidate.Name == TestGraph.Result);
    }

    /// <summary>
    /// Přejmenování v subgrafu nesmí odpojit volajícího. Musí to být subgraf s <b>víc</b>
    /// výstupy — u jediného se port historicky jmenuje <c>Out</c> bez ohledu na deklaraci,
    /// takže by se přejmenování na portu vůbec neprojevilo a test by nic netestoval.
    /// </summary>
    [Fact]
    public void RenamingSubgraphOutput_KeepsCallerPort()
    {
        var id = Guid.Parse("77777777-0000-0000-0000-000000000002");
        var node = new SubgraphNodeModel(SubgraphAsset(id, "Raw"));
        var port = node.Outputs["Raw"];

        node.RebuildFromInterface(SubgraphAsset(id, "Cooked").Interface);

        Assert.Same(port, node.Outputs["Cooked"]);
        Assert.Equal("Cooked", port.Label);
        Assert.DoesNotContain("Raw", node.Outputs.Keys);
    }

    private static Assets.AssetEntry SubgraphAsset(Guid id, string firstName) => new()
    {
        Id = id,
        Path = "",
        Name = "sub",
        Interface = new Assets.SubgraphInterface
        {
            Outputs = new[]
            {
                new Assets.SubgraphOutput { Id = "decl-1", Name = firstName, Type = TypeIds.Double },
                new Assets.SubgraphOutput { Id = "decl-2", Name = "Other", Type = TypeIds.Double, Order = 1 },
            },
        },
    };

    /// <summary>Output i Return staví porty stejným mechanismem, jen na jiném konci toku.</summary>
    [Fact]
    public void OutputAndReturn_BuildTheSamePortsFromTheSameDeclarations()
    {
        var catalog = TestGraph.Catalog();
        var declared = TestGraph.Outputs(TypeIds.Double, TypeIds.String);
        var diagram = TestGraph.NewDiagram();

        var sink = diagram.Add(catalog, BuiltInIds.Output, declared: declared);
        var returnNode = diagram.Add(catalog, BuiltInIds.Return, declared: declared);

        Assert.Equal(
            declared.Select(o => (o.Name, o.Type)),
            sink.InputDefs.Select(input => (input.Name, input.DataType)));
        // Return má navíc řídicí pin, hodnotové vstupy jsou tytéž.
        Assert.Equal(
            declared.Select(o => o.Name),
            returnNode.InputDefs.Where(input => input.DataType != TypeIds.Exec).Select(input => input.Name));
    }

    [Fact]
    public void EmptyListDeclaration_ExportsEmptyArrayNotNull()
    {
        var catalog = TestGraph.Catalog();
        var settings = new GraphSettings
        {
            Outputs =
            {
                new GraphOutput { Name = "Items", Type = TypeIds.Double, Multiple = true },
                new GraphOutput { Name = "One", Type = TypeIds.Double },
            },
        };
        var diagram = TestGraph.NewDiagram();
        diagram.AddSink(catalog, settings.Outputs);

        using var export = System.Text.Json.JsonDocument.Parse(
            GraphExporter.Export(diagram, settings, catalog: catalog));
        var outputs = export.RootElement.GetProperty("outputs").EnumerateArray().ToList();

        // Arita slíbila pole, tak vydá pole — konzument řeší jen jeden druh „nic".
        Assert.Equal(System.Text.Json.JsonValueKind.Array, outputs[0].GetProperty("value").ValueKind);
        Assert.Empty(outputs[0].GetProperty("value").EnumerateArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, outputs[1].GetProperty("value").ValueKind);
    }

    /// <summary>
    /// Role zmizela: parametry smí mít každý graf, sink patří do datového toku,
    /// Return do exec. Nic z toho se neptá na to, čím graf „je".
    /// </summary>
    [Fact]
    public void Palette_DependsOnFlowOnly()
    {
        var data = new GraphSettings();
        var exec = new GraphSettings { Flow = GraphFlow.Exec };
        var input = new NodeTypeDescriptor { Id = BuiltInIds.GraphInput };
        var sink = new NodeTypeDescriptor { Id = BuiltInIds.Output };
        var ret = new NodeTypeDescriptor { Id = BuiltInIds.Return };

        Assert.True(NedCanvas.BuiltInPaletteVisible(input, data, _ => false));
        Assert.True(NedCanvas.BuiltInPaletteVisible(input, exec, _ => false));
        Assert.True(NedCanvas.BuiltInPaletteVisible(sink, data, _ => false));
        Assert.False(NedCanvas.BuiltInPaletteVisible(sink, exec, _ => false));
        Assert.False(NedCanvas.BuiltInPaletteVisible(ret, data, _ => false));
        Assert.True(NedCanvas.BuiltInPaletteVisible(ret, exec, _ => false));
    }

    /// <summary>Instancovatelnost je vlastnost grafu, ne jeho druhu.</summary>
    [Fact]
    public void Instanceable_RoundTripsAndDrivesTemplateList()
    {
        using var library = new TempLibrary();
        var id = Guid.Parse("77777777-0000-0000-0000-000000000001");
        var document = TempLibrary.SubgraphReferencing(id, null);
        document.Settings.Instanceable = true;
        library.Write("instanceable", document);

        var asset = library.Index.Resolve(id)!;

        Assert.True(asset.Instanceable);
        Assert.Contains(library.Index.Templates(), entry => entry.Id == id);
        // Vkládat jde každý graf; kompatibilitu toku řeší CanBePlacedIn.
        Assert.Contains(library.Index.Subgraphs(), entry => entry.Id == id);
    }
}
