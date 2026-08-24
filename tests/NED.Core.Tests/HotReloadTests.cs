using NED.Core.Persistence;

namespace NED.Core.Tests;

public class HotReloadTests
{
    [Fact]
    public void UnchangedReload_PreservesDirtyUndoSelectionAndViewport()
    {
        var catalog = TestGraph.Catalog();
        var source = TestGraph.AddTwoNumbers(catalog);
        var tab = LoadTab(source, catalog);
        tab.Undo.Reset(tab.Diagram, tab.Settings);
        tab.Settings.Description = "user edit";
        Assert.True(tab.Undo.CommitIfChanged(tab.Diagram, tab.Settings));
        tab.IsDirty = false;
        var selectedId = tab.Diagram.Nodes.First().Id;
        tab.Diagram.SelectModel(tab.Diagram.Nodes.First(), unselectOthers: true);
        tab.Diagram.SetPan(123, 456);
        tab.Diagram.SetZoom(1.5);

        var changed = NedCanvas.ReloadTab(tab, catalog, assetIndex: null);

        Assert.False(changed);
        Assert.False(tab.IsDirty);
        Assert.True(tab.Undo.CanUndo);
        Assert.Equal(selectedId, Assert.Single(tab.Diagram.GetSelectedModels()).Id);
        Assert.Equal(123, tab.Diagram.Pan.X);
        Assert.Equal(456, tab.Diagram.Pan.Y);
        Assert.Equal(1.5, tab.Diagram.Zoom);
    }

    [Fact]
    public void RemovedTypeReload_CreatesPlaceholderWithoutChangingDto()
    {
        var originalCatalog = TestGraph.Catalog();
        var source = TestGraph.AddTwoNumbers(originalCatalog);
        var tab = LoadTab(source, originalCatalog);
        var before = GraphPersistence.ToDocument(tab.Diagram, tab.Settings).Nodes
            .Where(node => node.TypeName.StartsWith("sandbox/", StringComparison.Ordinal))
            .ToDictionary(node => node.Id, node => GraphPersistence.Serialize(new GraphDocument { Nodes = { node } }));

        NedCanvas.ReloadTab(tab, new NedCatalog(Array.Empty<NED.Abstractions.Manifest.NodeManifest>()), null);

        Assert.Contains(tab.Diagram.Nodes, node => node is MissingNodeModel);
        var placeholders = tab.Diagram.Nodes.OfType<MissingNodeModel>().ToList();
        Assert.Equal(before.Count, placeholders.Count);
        foreach (var placeholder in placeholders)
        {
            var serialized = GraphPersistence.Serialize(new GraphDocument { Nodes = { placeholder.Dto } });
            Assert.Equal(before[placeholder.Dto.Id], serialized);
        }
    }

    private static EditorTab LoadTab((Blazor.Diagrams.BlazorDiagram diagram, GraphSettings settings) source, NedCatalog catalog)
    {
        var tab = new EditorTab();
        var document = GraphPersistence.ToDocument(source.diagram, source.settings);
        tab.Settings = GraphPersistence.LoadInto(tab.Diagram, document, catalog);
        return tab;
    }
}
