using Blazor.Diagrams;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;

namespace NED.Core;

public partial class NedCanvas
{
    private EditorTab CreateTab(GraphSettings? settings = null, bool seedOutput = false)
    {
        var tab = new EditorTab();
        if (settings is not null) tab.Settings = settings;
        SubscribeDiagram(tab.Diagram);
        if (seedOutput)
        {
            var seedTypeId = tab.Settings.IsExec
                ? Manifest.BuiltInIds.ExecEntry
                : Manifest.BuiltInIds.Output;
            if (Catalog.Resolve(seedTypeId) is { } seedType)
            {
                var seed = new DataNodeModel(seedType, new Point(700, 220), id: null, Catalog);
                seed.SyncDeclaredInputs(tab.Settings.Outputs);
                tab.Diagram.Nodes.Add(seed);
            }
        }
        // Baseline = počáteční stav tabu (prázdný nebo se seed OutputNode). Bez toho by
        // první uživatelská mutace zapsala falešný undo krok „zpět na prázdno".
        tab.Undo.Reset(tab.Diagram, tab.Settings);
        return tab;
    }

    private void SubscribeDiagram(BlazorDiagram diagram)
    {
        // Odregistruj předchozí handlery (bezpečné i při prvním volání — -= na null nic nedělá).
        diagram.Nodes.Added -= OnNodeAdded;
        diagram.Nodes.Removed -= OnNodeRemoved;
        diagram.Links.Added -= OnLinkAdded;
        diagram.Links.Removed -= OnLinkRemoved;
        diagram.PointerMove -= OnDiagramPointerMove;
        diagram.PointerUp -= OnDiagramPointerUp;
        diagram.PointerDoubleClick -= OnDiagramDoubleClick;
        diagram.SelectionChanged -= OnSelectionChanged;

        diagram.Nodes.Added += OnNodeAdded;
        diagram.Nodes.Removed += OnNodeRemoved;
        diagram.Links.Added += OnLinkAdded;
        diagram.Links.Removed += OnLinkRemoved;
        diagram.PointerMove += OnDiagramPointerMove;
        diagram.PointerUp += OnDiagramPointerUp;
        diagram.PointerDoubleClick += OnDiagramDoubleClick;
        diagram.SelectionChanged += OnSelectionChanged;

        // Přihlas Moved na všechny existující nody. Po undo/redo tenhle loop běží znovu,
        // takže se nejdřív odebere (idempotence) — jinak by nody vzniklé v LoadInto měly
        // handler přihlášený DVAKRÁT (jednou přes OnNodeAdded, podruhé tady) → B8.
        foreach (var node in diagram.Nodes)
        {
            node.Moved -= OnNodeMoved;
            node.Moved += OnNodeMoved;
        }
    }

    private void OnDiagramDoubleClick(Model? model, Blazor.Diagrams.Core.Events.PointerEventArgs e)
    {
        if (model is SubgraphNodeModel sg)
            OpenSubgraphFromNode(sg.SubgraphId);
    }

    private void MarkDirty()
    {
        if (_loading) return;
        if (_active is not null) _active.IsDirty = true;
    }

    private void OnNodeAdded(NodeModel node)
    {
        node.Moved += OnNodeMoved;
        MarkDirty();
        Revalidate();
        RequestUndoCommit();
        InvokeAsync(StateHasChanged);
    }

    private void OnNodeRemoved(NodeModel node)
    {
        node.Moved -= OnNodeMoved;
        MarkDirty();
        Revalidate();
        RequestUndoCommit();
        InvokeAsync(StateHasChanged);
    }

    private void OnNodeMoved(MovableModel _)
    {
        MarkDirty();
        // Move fíruje mnoho eventů za drag → commit se odloží až na pointer-up
        // (jinak by každý frame založil samostatný undo krok).
        _moveCommitPending = true;
    }

    private void OnLinkRemoved(BaseLinkModel link)
    {
        MarkDirty();
        // Skutečné spojení (oba konce porty) → revalidace + undo krok. Neúplný link
        // zrušeného tažení (cíl = kurzor) nic nemění, žádný krok nezakládá.
        if (link.Source.Model is PortModel && link.Target.Model is PortModel)
        {
            Revalidate();
            RequestUndoCommit();
        }
        InvokeAsync(StateHasChanged);
    }

    /// <summary>Centrální aktivace tabu — nastaví aktivní tab a zapíše do historie navigace.</summary>
    private void Activate(TabBase tab)
    {
        _active = tab;
        _history.Visit(tab);
        Revalidate();
        PushInspectorState();   // přepnutí tabu → panel ukáže nový graf / jeho výběr
    }

    private void SwitchTab(TabBase tab)
    {
        if (_paletteOpen) ClosePalette();
        Activate(tab);
        StateHasChanged();
    }

    /// <summary>Zpět v historii navigace. Obchází <see cref="Activate"/>, aby se krok znovu nezapsal.</summary>
    [NedCommand("Navigate/Back", Shortcut = "Alt+←", Order = 0)]
    private void NavigateBack()
    {
        if (_paletteOpen) ClosePalette();
        var target = _history.Back();
        if (target is null) return;
        _active = target;
        Revalidate();
        PushInspectorState();
        StateHasChanged();
    }

    /// <summary>Vpřed v historii navigace.</summary>
    [NedCommand("Navigate/Forward", Shortcut = "Alt+→", Order = 1)]
    private void NavigateForward()
    {
        if (_paletteOpen) ClosePalette();
        var target = _history.Forward();
        if (target is null) return;
        _active = target;
        Revalidate();
        PushInspectorState();
        StateHasChanged();
    }

    /// <summary>Zavření tabu uživatelem (×). U neuloženého se zeptá Save/Discard/Cancel.</summary>
    private async Task CloseTab(TabBase tab)
    {
        if (!await ConfirmSaveTab(tab)) return;
        RemoveTab(tab);
    }

    /// <summary>Holé odebrání tabu bez ptaní — pro případy, kdy se soubor maže.</summary>
    private void RemoveTab(TabBase tab)
    {
        var idx = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        _history.Remove(tab);
        // Osiřelé děti (drill-in) připoj na rodiče zavřeného tabu, ať breadcrumb nemíří na mrtvý tab.
        foreach (var t in _tabs)
            if (t.OpenedFrom == tab) t.OpenedFrom = tab.OpenedFrom;

        if (_tabs.Count == 0)
        {
            _active = null;
            PushInspectorState();   // žádný graf → panel se vyprázdní
        }
        else if (_active == tab)
        {
            Activate(_tabs[Math.Min(idx, _tabs.Count - 1)]);
        }
        StateHasChanged();
    }
}
