using Microsoft.AspNetCore.Components;
using Blazor.Diagrams.Core.Models.Base;

namespace NED.Core;

/// <summary>
/// Napojení výběru nodu na <see cref="NodeInspector"/> (zdroj dat pro Details panel).
/// Panel žije mimo cascading scope, takže komunikace jde přes scoped službu.
/// </summary>
public partial class NedCanvas
{
    [Inject] private NodeInspector Inspector { get; set; } = default!;

    /// <summary>Handler diagram.SelectionChanged — přepočítá vybraný node a pošle do inspectoru.</summary>
    private void OnSelectionChanged(SelectableModel _)
    {
        PushInspectorState();
        InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Naplní inspector aktuálním stavem: vybraný node (jen když je právě jeden),
    /// most a nastavení aktivního grafu. Volá se po změně výběru i po přepnutí tabu.
    /// </summary>
    private void PushInspectorState()
    {
        if (ActiveEditor is not { } ed)
        {
            Inspector.Set(null, _bridge, null);
            return;
        }
        var nodes = ed.Diagram.GetSelectedModels().OfType<DataNodeModel>().ToList();
        Inspector.Set(nodes.Count == 1 ? nodes[0] : null, _bridge, ed.Settings);
    }

    /// <summary>
    /// Panel upravil graf-level nastavení → dirty + překresli chrome. Revalidace kvůli
    /// druhu grafu: přepnutí na Exec mění, která pravidla platí (Output node vs. Exec Entry).
    /// </summary>
    private void OnGraphEdited()
    {
        if (ActiveEditor is { } ed)
        {
            // Deklarace se editují v panelu, porty z nich staví uzel — po změně je musí
            // někdo srovnat, jinak by port zůstal na starém typu a linky by se zdánlivě
            // náhodně přestaly dávat spojit.
            foreach (var node in ed.Diagram.Nodes.OfType<DataNodeModel>()
                         .Where(node => node.DeclaresGraphOutputs))
                node.SyncDeclaredInputs(ed.Settings.Outputs);
        }

        MarkDirty();
        Revalidate();
        InvokeAsync(StateHasChanged);
    }
}
