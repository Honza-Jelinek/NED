using Blazor.Diagrams.Core.Behaviors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using NED.Abstractions.Manifest;
using NED.Core.Assets;

namespace NED.Core;

public partial class NedCanvas
{
    private void OpenLinkPicker(PortModel src, double clientX, double clientY)
    {
        _linkSource = src;
        _pickerX = clientX;
        _pickerY = clientY;

        if (src is TypedPortModel tp && src.Alignment == PortAlignment.Right)
        {
            // Jediné kritérium je „umí to přijmout, co táhnu" — typ i arita. Sink se
            // nevylučuje: Return uzel žádný výstup nemá a přesně on exec větev končí.
            _pickerFilter = t => NedCatalog.InputPorts(t).Any(port =>
                TypedPortModel.ArityFits(tp.Multiple, port.Multiple)
                && TypeIds.IsCompatible(tp.DataType, tp.Extends, port.Type));
            // Exec funkce se z exec pinu nabízí přes svůj exec vstup — datové rozhraní
            // s tím nemá co dělat, může být klidně prázdné.
            // Vstupy subgrafu aritu zatím nenesou, proto se u nich neptáme.
            _pickerSubFilter = tp.DataType == TypeIds.Exec
                ? sg => sg.Interface.Flow == GraphFlow.Exec
                : sg => MatchesFlow(sg) && !tp.Multiple && sg.Interface.Inputs.Any(i =>
                    i.Exposure == InputExposure.Port && TypeIds.IsCompatible(tp.DataType, tp.Extends, i.Type));
        }
        else if (src is TypedPortModel tp2)
        {
            _pickerFilter = t => NedCatalog.OutputPorts(t).Any(port =>
                TypedPortModel.ArityFits(port.Multiple, tp2.Multiple)
                && Catalog.IsCompatible(port.Type, tp2.DataType));
            _pickerSubFilter = tp2.DataType == TypeIds.Exec
                ? sg => sg.Interface.Flow == GraphFlow.Exec
                : sg => MatchesFlow(sg) && sg.Interface.Outputs.Any(output =>
                    TypedPortModel.ArityFits(output.Multiple, tp2.Multiple)
                    && Catalog.IsCompatible(output.Type, tp2.DataType));
        }

        _pickerOpen = true;
        InvokeAsync(StateHasChanged);
    }

    /// <summary>Subgraf, který se do aktuálního grafu smí vložit.</summary>
    private bool MatchesFlow(AssetEntry asset) => asset.Interface.CanBePlacedIn(Settings.Flow);

    private void ClosePicker()
    {
        _pickerOpen = false;
        _linkSource = null;
        _pickerFilter = null;
        _pickerSubFilter = null;
        _pickerContext = null;
        StateHasChanged();
    }

    private Point PickerDropPoint() =>
        ActiveEditor!.Diagram.GetRelativeMousePoint(_pickerX, _pickerY);

    private void OnPickType(NodeTypeDescriptor t)
    {
        if (ActiveEditor is null) { ClosePicker(); return; }
        RecordUndo();
        var node = new DataNodeModel(t, PickerDropPoint(), id: null, Catalog);
        node.SyncDeclaredInputs(Settings.Outputs);
        ActiveEditor.Diagram.Nodes.Add(node);
        if (_linkSource is not null) Connect(_linkSource, node);
        ClosePicker();
    }

    private void OnPickSubgraph(AssetEntry asset)
    {
        if (ActiveEditor is null) { ClosePicker(); return; }
        RecordUndo();
        var node = new SubgraphNodeModel(asset, PickerDropPoint());
        ActiveEditor.Diagram.Nodes.Add(node);
        if (_linkSource is not null) Connect(_linkSource, node);
        ClosePicker();
    }

    private void Connect(PortModel src, NodeModel newNode)
    {
        if (ActiveEditor is not { } ed || src is not TypedPortModel sp) return;

        if (src.Alignment == PortAlignment.Right)
        {
            var input = newNode.Ports.OfType<TypedPortModel>().FirstOrDefault(p =>
                p.Alignment == PortAlignment.Left && TypedPortModel.IsCompatible(sp, p));
            if (input is not null) ed.Diagram.Links.Add(new LinkModel(src, input));
        }
        else
        {
            var output = newNode.Ports.OfType<TypedPortModel>().FirstOrDefault(p =>
                p.Alignment == PortAlignment.Right && TypedPortModel.IsCompatible(p, sp));
            if (output is not null) ed.Diagram.Links.Add(new LinkModel(output, src));
        }
    }

    private void OnLinkAdded(BaseLinkModel link)
    {
        // Revalidace AŽ při TargetAttached (dokončené spojení), ne tady. Links.Added
        // fíruje na ZAČÁTKU tažení (cíl je ještě kurzor) — node.Refresh() z revalidace
        // by uprostřed dragu rozbil DOM a link by zůstal „v ruce".
        link.TargetAttached += OnLinkTargetAttached;
        if (link.Target.Model is PortModel) OnLinkTargetAttached(link);
        MarkDirty();
        InvokeAsync(StateHasChanged);
    }

    private void OnLinkTargetAttached(BaseLinkModel link)
    {
        LinkConstraints.Apply(link);
        Revalidate();
        // Link dotažen na port = skutečná mutace grafu → undo krok. Composite gesta
        // (picker: add node + Connect + LinkConstraints) se koalescují v jediném
        // flushi na konci synchronního bloku, takže tenhle commit spadne dohromady
        // s případným OnNodeAdded/OnLinkRemoved z jednoho user gesture.
        RequestUndoCommit();
        InvokeAsync(StateHasChanged);
    }

    private void OnDiagramPointerMove(Model? model, Blazor.Diagrams.Core.Events.PointerEventArgs e)
    {
        var ongoing = ActiveEditor?.Diagram.GetBehavior<DragNewLinkBehavior>()?.OngoingLink;
        if (ongoing is not null)
            _dragSourcePort = ongoing.Source.Model as PortModel;
    }

    private void OnDiagramPointerUp(Model? model, Blazor.Diagrams.Core.Events.PointerEventArgs e)
    {
        var src = _dragSourcePort;
        _dragSourcePort = null;
        if (src is not null && model is null)
            OpenLinkPicker(src, e.ClientX, e.ClientY);

        // Konec drag pohybu nodů: flushni pending commit (jeden undo krok za celý drag).
        if (_moveCommitPending)
        {
            _moveCommitPending = false;
            RequestUndoCommit();
        }
    }
}
