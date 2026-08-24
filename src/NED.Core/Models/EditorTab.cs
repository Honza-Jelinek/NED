using Blazor.Diagrams;
using Blazor.Diagrams.Options;

namespace NED.Core;

public sealed class EditorTab : TabBase
{
    public BlazorDiagram Diagram { get; }
    public GraphSettings Settings { get; set; } = new();
    public UndoManager Undo { get; } = new();

    public int Added { get; set; }

    public override string Title => string.IsNullOrWhiteSpace(Settings.Name)
        ? (FilePath is not null ? Path.GetFileNameWithoutExtension(FilePath).Replace(".nedgraph", "") : $"Untitled {Index}")
        : Settings.Name;

    public override string Icon => GraphIcons.For(Settings.Flow);

    public override bool CanExport => true;

    public EditorTab()
    {
        var opts = new BlazorDiagramOptions { AllowMultiSelection = true };
        opts.Zoom.Enabled = true;
        opts.Zoom.Inverse = true;
        opts.Zoom.Minimum = 0.2;
        opts.Zoom.Maximum = 3.0;
        Diagram = new BlazorDiagram(opts);
        Diagram.RegisterComponent<DataNodeModel, DataNodeWidget>();
        Diagram.RegisterComponent<SubgraphNodeModel, SubgraphNodeWidget>();
        Diagram.RegisterComponent<MissingNodeModel, MissingNodeWidget>();
    }
}
