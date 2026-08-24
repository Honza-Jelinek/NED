namespace NED.Core;

/// <summary>
/// Scoped sběrnice „co je právě vybráno" pro Details panel. NedCanvas do ní zapisuje
/// vybraný node (a kontext: bridge pro undo/revalidate + aktivní GraphSettings); Details
/// panel ji injektuje a poslouchá <see cref="Changed"/> (stejný vzor jako AssetIndex.Changed).
/// Nutné, protože Details panel se renderuje v portal vrstvě mimo cascading scope NedCanvasu.
/// </summary>
public sealed class NodeInspector
{
    /// <summary>Právě vybraný node (jen když je vybraný přesně jeden). null = žádný / víc.</summary>
    public DataNodeModel? Node { get; private set; }

    /// <summary>Můstek pro undo/revalidate v aktivním editoru.</summary>
    public NodeEditorBridge? Bridge { get; private set; }

    /// <summary>Nastavení aktivního grafu (pro graf-level editaci při prázdném výběru).</summary>
    public GraphSettings? Graph { get; private set; }

    /// <summary>Výběr nebo kontext se změnil → Details panel se překreslí.</summary>
    public event Action? Changed;

    /// <summary>Panel upravil graf-level nastavení → host označí dirty a překreslí chrome (název tabu).</summary>
    public event Action? GraphEdited;

    public void Set(DataNodeModel? node, NodeEditorBridge? bridge, GraphSettings? graph)
    {
        Node = node;
        Bridge = bridge;
        Graph = graph;
        Changed?.Invoke();
    }

    public void RaiseGraphEdited() => GraphEdited?.Invoke();
}
