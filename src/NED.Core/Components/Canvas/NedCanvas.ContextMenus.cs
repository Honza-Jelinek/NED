using System.Reflection;
using Microsoft.AspNetCore.Components.Web;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions;
using NED.Core.Assets;

namespace NED.Core;

public partial class NedCanvas
{
    /// <summary>Pravý klik: na nodu → kontext menu, na prázdnu → node picker.</summary>
    private void OnCanvasContextMenu(MouseEventArgs e)
    {
        if (ActiveEditor is not { } ed) return;

        var node = NodeAt(e.ClientX, e.ClientY);
        if (node is not null)
        {
            ed.Diagram.SelectModel(node, unselectOthers: true);
            _nodeMenuTarget = node;
            _nodeMenuX = e.ClientX;
            _nodeMenuY = e.ClientY;
            _nodeMenuOpen = true;
            return;
        }

        _linkSource = null;
        _pickerFilter = null;
        // Exec funkce patří jen do exec grafu a naopak — v datovém grafu by se
        // inlinovala i s exec řetězem, který do exportu nejde.
        _pickerSubFilter = MatchesFlow;
        _pickerContext = null;
        _pickerX = e.ClientX;
        _pickerY = e.ClientY;
        _pickerOpen = true;
    }

    /// <summary>Topmost node pod daným client bodem (hit-test podle bounds).</summary>
    private NodeModel? NodeAt(double clientX, double clientY)
    {
        var p = ActiveEditor!.Diagram.GetRelativeMousePoint(clientX, clientY);
        NodeModel? hit = null;
        foreach (var n in ActiveEditor.Diagram.Nodes)
        {
            if (n.Size is null) continue;
            if (p.X >= n.Position.X && p.X <= n.Position.X + n.Size.Width
                && p.Y >= n.Position.Y && p.Y <= n.Position.Y + n.Size.Height)
                hit = n; // poslední shoda = navrchu v render pořadí
        }
        return hit;
    }

    // ── Node context menu akce ──────────────────────

    private void CloseNodeMenu()
    {
        _nodeMenuOpen = false;
        _nodeMenuTarget = null;
        StateHasChanged();
    }

    private void OnMenuDelete()
    {
        if (_nodeMenuTarget is not null && ActiveEditor is { } ed2)
        {
            RecordUndo();
            ed2.Diagram.Nodes.Remove(_nodeMenuTarget);
        }
        CloseNodeMenu();
    }

    private void OnMenuRefresh()
    {
        if (_nodeMenuTarget is not null) RefreshNode(_nodeMenuTarget);
        CloseNodeMenu();
    }

    private void OnMenuDuplicate()
    {
        if (_nodeMenuTarget is not null) { RecordUndo(); DuplicateNode(_nodeMenuTarget); }
        CloseNodeMenu();
    }

    /// <summary>
    /// Refreshuje jeden node — pro SubgraphNodeModel přestaví porty z aktuálního
    /// rozhraní v AssetIndex, pro DataNodeModel přečte znovu dynamické typy.
    /// Univerzální — použitelné z context menu i automaticky po uložení.
    /// </summary>
    public void RefreshNode(NodeModel node)
    {
        switch (node)
        {
            case SubgraphNodeModel sg:
                var fresh = AssetIndex.Resolve(sg.SubgraphId);
                if (fresh is not null)
                    sg.RebuildFromInterface(fresh.Interface);
                break;

            case DataNodeModel dn:
                dn.RefreshDynamicTypes();
                break;
        }
    }

    /// <summary>
    /// Po rescanu AssetIndex projde VŠECHNY taby a aktualizuje SubgraphNodeModely,
    /// jejichž interface se SKUTEČNĚ změnil (hot reload po uložení subgrafu).
    /// Rescan staví interface objekty vždy znovu, proto hodnotové porovnání —
    /// beze změny žádný rebuild (a žádné sahání na porty/linky).
    /// </summary>
    private void RefreshSubgraphReferences()
    {
        foreach (var tab in _tabs.OfType<EditorTab>())
            foreach (var node in tab.Diagram.Nodes.OfType<SubgraphNodeModel>().ToList())
            {
                var entry = AssetIndex.Resolve(node.SubgraphId);
                if (entry is null) continue;
                if (node.SubgraphName != entry.Name)
                {
                    // Rename souboru / zahojení stale reference (Missing-xxxx → skutečné jméno).
                    node.SubgraphName = entry.Name;
                    node.Refresh();
                }
                if (!node.Interface.SameAs(entry.Interface))
                    node.RebuildFromInterface(entry.Interface);
            }
    }

    /// <summary>Vytvoří kopii uzlu i s hodnotami vstupů, s offsetem.</summary>
    private void DuplicateNode(NodeModel node)
    {
        if (ActiveEditor is not { } ed) return;
        var pos = new Point(node.Position.X + 30, node.Position.Y + 30);
        switch (node)
        {
            case DataNodeModel dn:
                var copy = new DataNodeModel(dn.Descriptor, pos, id: null, Catalog);
                foreach (var (k, v) in dn.Values) copy.Values[k] = v;
                foreach (var (k, v) in dn.UnknownValues) copy.UnknownValues[k] = v;
                CopyExposure(dn, copy);
                ed.Diagram.Nodes.Add(copy);
                copy.RefreshDynamicTypes();
                copy.SyncDeclaredInputs(Settings.Outputs);
                break;

            case SubgraphNodeModel sg:
                // Stale reference (asset smazaný) — duplikuj přes syntetický entry z aktuálního
                // stavu nodu, aby duplikace tiše neselhala.
                var asset = AssetIndex.Resolve(sg.SubgraphId) ?? new AssetEntry
                {
                    Id = sg.SubgraphId,
                    Path = "",
                    Name = sg.SubgraphName,
                    Flow = sg.Interface.Flow,
                    Interface = sg.Interface,
                };
                ed.Diagram.Nodes.Add(new SubgraphNodeModel(asset, pos));
                break;

            case MissingNodeModel mn:
                // Duplikát DTO s novým Id; vstupní porty převezmi z originálu (linky se nekopírují).
                ed.Diagram.Nodes.Add(new MissingNodeModel(
                    mn.CloneDto(), mn.InputPorts.Keys, mn.Outputs.Keys, pos));
                break;
        }
        ed.Added++;
    }

    /// <summary>Přenese override expozice (port↔pole) ze zdrojového nodu na kopii.</summary>
    private static void CopyExposure(DataNodeModel from, DataNodeModel to)
    {
        foreach (var src in from.InputDefs)
        {
            if (!src.Togglable || src.AsPort == src.DefaultAsPort) continue;
            var dst = to.InputDefs.FirstOrDefault(i => i.Name == src.Name);
            if (dst is not null) to.SetExposure(dst, src.AsPort);
        }
    }

    // ── Input (port↔pole) context menu ──────────────

    /// <summary>
    /// Volá widget přes bridge (pravý klik na přepínatelný vstup). Node-agnostické —
    /// widget dodá hotovou toggle akci, takže funguje pro DataNode i SubgraphNode.
    /// </summary>
    private void OpenInputMenu(bool isPort, Action toggle, double clientX, double clientY)
    {
        _inputMenuIsPort = isPort;
        _inputMenuToggle = toggle;
        _inputMenuX = clientX;
        _inputMenuY = clientY;
        _inputMenuOpen = true;
        InvokeAsync(StateHasChanged);
    }

    private void CloseInputMenu()
    {
        _inputMenuOpen = false;
        _inputMenuToggle = null;
        StateHasChanged();
    }

    private void ToggleInputExposure()
    {
        RecordUndo();
        _inputMenuToggle?.Invoke();
        Revalidate();
        CloseInputMenu();
    }
}
