using System.Text.Json;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core.Tests;

public sealed class ExecTests
{
    [Fact]
    public void Exec_IsCompatibleOnlyWithExec()
    {
        Assert.True(TypeIds.IsCompatible(TypeIds.Exec, null, TypeIds.Exec));
        Assert.False(TypeIds.IsCompatible(TypeIds.Exec, null, TypeIds.Any));
        Assert.False(TypeIds.IsCompatible(TypeIds.Any, null, TypeIds.Exec));
    }

    [Fact]
    public void TwoExecLinksIntoMultipleInput_SurviveRoundTrip()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var branch = diagram.Add(catalog, "sandbox/Branch");
        var sequence = diagram.Add(catalog, "sandbox/Sequence");
        var input = sequence.Input("In");
        diagram.Links.Add(new LinkModel(branch.Outputs["True"], input));
        diagram.Links.Add(new LinkModel(branch.Outputs["False"], input));
        var settings = new GraphSettings();

        var first = GraphPersistence.Serialize(GraphPersistence.ToDocument(diagram, settings));
        var reloaded = TestGraph.NewDiagram();
        var reloadedSettings = GraphPersistence.LoadInto(
            reloaded, GraphPersistence.Deserialize(first)!, catalog);
        var second = GraphPersistence.Serialize(GraphPersistence.ToDocument(reloaded, reloadedSettings));

        Assert.Equal(first, second);
        Assert.Equal(2, reloaded.Links.Count);
        Assert.True(sequence.InputDefs.Single(definition => definition.Name == "In").Multiple);
    }

    [Fact]
    public void Export_OmitsExecInputs()
    {
        var catalog = CatalogWithExecValueNode();
        var diagram = TestGraph.NewDiagram();
        var value = diagram.Add(catalog, "flow/ExecValue");
        var output = diagram.Add(catalog, TestGraph.Output);
        diagram.Link(value, output, TestGraph.Result);

        using var export = JsonDocument.Parse(GraphExporter.Export(
            diagram, new GraphSettings { Outputs = TestGraph.Outputs(TypeIds.Double) }, catalog: catalog));
        var exportedValue = export.RootElement.GetProperty("outputs")[0].GetProperty("value");

        Assert.False(exportedValue.TryGetProperty("In", out _));
    }

    [Fact]
    public void Validator_DoesNotRequireExecInput()
    {
        var catalog = TestGraph.Catalog();
        var diagram = TestGraph.NewDiagram();
        var branch = diagram.Add(catalog, "sandbox/Branch");

        var issues = GraphValidator.Validate(diagram, null, TestGraph.Settings());

        Assert.DoesNotContain(issues, issue =>
            issue.Node == branch && issue.MessageKey == "Validation_RequiredInput"
            && Equals(issue.Args?[0], "In"));
    }

    /// <summary>
    /// Uzel s řídicím pinem se pozná bez ohledu na to, na které straně pin je —
    /// paleta datového grafu ho podle toho skryje.
    /// </summary>
    [Fact]
    public void HasExecPort_DetectsBothSides()
    {
        var catalog = TestGraph.Catalog();

        Assert.True(NedCatalog.HasExecPort(catalog.Resolve("sandbox/Branch")!));
        Assert.True(NedCatalog.HasExecPort(catalog.Resolve(BuiltInIds.ExecEntry)!));   // jen výstup
        Assert.False(NedCatalog.HasExecPort(catalog.Resolve(TestGraph.AddNode)!));
        Assert.False(NedCatalog.HasExecPort(catalog.Resolve(BuiltInIds.Output)!));
    }

    [Fact]
    public void Catalog_DoesNotOfferExecAsSelectableDataType()
    {
        var catalog = TestGraph.Catalog();

        Assert.DoesNotContain(TypeIds.Exec, catalog.SelectableTypes());
    }

    [Fact]
    public void PickerCompatibilityForExec_OnlyMatchesNodesWithExecInputs()
    {
        var catalog = TestGraph.Catalog();
        var matches = catalog.AllTypes.Where(type => NedCatalog.InputPorts(type)
            .Any(port => TypeIds.IsCompatible(TypeIds.Exec, null, port.Type))).ToList();

        Assert.Contains(matches, type => type.Id == "sandbox/Branch");
        Assert.Contains(matches, type => type.Id == "sandbox/Sequence");
        Assert.All(matches, type => Assert.Contains(type.Inputs,
            input => input.Kind == InputKind.Port && input.Type == TypeIds.Exec));
    }

    [Fact]
    public void InvalidExecField_IsReportedAndLoadedAsPort()
    {
        var catalog = new NedCatalog(new[] { ExecFieldManifest() });

        Assert.Contains(catalog.Issues, issue => issue.MessageKey == "Notice_ExecMustBePort");

        // Srovnat to na port je věc modelu, ne katalogu — descriptor patří volajícímu.
        var input = new DataNodeModel(catalog.Resolve("bad/ExecField")!, catalog: catalog)
            .InputDefs.Single();
        Assert.True(input.AsPort);
        Assert.False(input.Details);
        Assert.False(input.Togglable);
    }

    [Fact]
    public void InvalidExecField_IsReportedOnEveryReload()
    {
        // Hot reload předá tytéž instance manifestu (ManifestStore → NedOptions.Manifest),
        // takže katalog je nesmí přepsat — jinak hlášení podruhé nevyjde.
        var manifest = ExecFieldManifest();
        var catalog = new NedCatalog(new[] { manifest });

        catalog.Reload(new[] { manifest });

        Assert.Contains(catalog.Issues, issue => issue.MessageKey == "Notice_ExecMustBePort");
        Assert.Equal(InputKind.Field, manifest.Types.Single().Inputs.Single().Kind);
    }

    /// <summary>Ručně psaný manifest, který dal exec vstupu field režim i Details.</summary>
    private static NodeManifest ExecFieldManifest() => new()
    {
        Pack = new PackInfo { Id = "bad" },
        Types =
        {
            new NodeTypeDescriptor
            {
                Id = "bad/ExecField", Name = "Exec field",
                Inputs =
                {
                    new NodeInputDescriptor
                    {
                        Name = "In", Label = "In", Kind = InputKind.Field,
                        Type = TypeIds.Exec, Details = true,
                    },
                },
            },
        },
    };

    private static NedCatalog CatalogWithExecValueNode()
    {
        var sandbox = NED.Manifest.Generator.ManifestBuilder.Build(typeof(Sandbox.Add).Assembly, out _);
        var flow = new NodeManifest
        {
            Pack = new PackInfo { Id = "flow" },
            Types =
            {
                new NodeTypeDescriptor
                {
                    Id = "flow/ExecValue", Name = "Exec value",
                    Inputs =
                    {
                        new NodeInputDescriptor
                        {
                            Name = "In", Label = "In", Kind = InputKind.Port,
                            Type = TypeIds.Exec, Multiple = true,
                        },
                    },
                    Outputs =
                    {
                        new NodeOutputDescriptor { Name = BuiltInIds.DefaultOutput, Type = TypeIds.Double },
                    },
                },
            },
        };
        return new NedCatalog(new[] { sandbox, flow });
    }
}
