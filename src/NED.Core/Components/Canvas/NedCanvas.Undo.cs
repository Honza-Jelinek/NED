using MudBlazor;

namespace NED.Core;

public partial class NedCanvas
{
    /// <summary>
    /// Nastavuje se z <see cref="RequestUndoCommit"/> a shazuje se v koalescujícím flushi.
    /// Jeden „user gesture" (např. picker: add node + Connect + EnforceSingleInput) tak
    /// vygeneruje jediný undo krok — všechny synchronně za sebou proběhlé mutace se sesypou.
    /// </summary>
    private bool _commitPending;

    /// <summary>
    /// True mezi prvním <see cref="OnNodeMoved"/> a nejbližším pointer-up. Move fíruje mnoho
    /// eventů za drag — zaznamenat undo krok chceme AŽ na konci pohybu (jinak by každý frame
    /// založil samostatný krok). Ostatní mutace (add/remove/link/field) commitují hned.
    /// </summary>
    private bool _moveCommitPending;

    /// <summary>Public entry point pro widgety přes <see cref="NodeEditorBridge"/>. Deferred commit.</summary>
    private void RecordUndo() => RequestUndoCommit();

    /// <summary>
    /// Naplánuje jediný commit na konec aktuálního synchronního bloku. Opakované volání
    /// v jednom bloku splyne — deferred flush udělá jen jedno porovnání baseline↔stav.
    /// </summary>
    private void RequestUndoCommit()
    {
        if (_commitPending || _loading) return;
        _commitPending = true;
        InvokeAsync(FlushUndoCommit);
    }

    private void FlushUndoCommit()
    {
        _commitPending = false;
        if (_loading) return;
        if (ActiveEditor is not { } ed) return;
        if (ed.Undo.CommitIfChanged(ed.Diagram, ed.Settings))
            MarkDirty();
    }

    [NedCommand("Edit/Undo", Shortcut = "Ctrl+Z", Icon = Icons.Material.Filled.Undo)]
    private void PerformUndo()
    {
        if (ActiveEditor is not { } ed || !ed.Undo.CanUndo) return;
        var settings = ed.Undo.Undo(ed.Diagram, ed.Settings, Catalog, AssetIndex);
        if (settings is not null) ed.Settings = settings;
        SubscribeDiagram(ed.Diagram);
        Revalidate();
        StateHasChanged();
    }

    [NedCommand("Edit/Redo", Shortcut = "Ctrl+Y", Icon = Icons.Material.Filled.Redo)]
    private void PerformRedo()
    {
        if (ActiveEditor is not { } ed || !ed.Undo.CanRedo) return;
        var settings = ed.Undo.Redo(ed.Diagram, ed.Settings, Catalog, AssetIndex);
        if (settings is not null) ed.Settings = settings;
        SubscribeDiagram(ed.Diagram);
        Revalidate();
        StateHasChanged();
    }

    /// <summary>AssetIndex.Changed může přijít z watcher/debounce vlákna (mimo Blazor dispatcher) —
    /// celé tělo (mutuje diagramy) musí běžet na UI vlákně, ne jen finální StateHasChanged.</summary>
    private void OnIndexChanged() => InvokeAsync(() =>
    {
        RefreshSubgraphReferences();
        RefreshInstanceTabs();
        Revalidate();
        StateHasChanged();
    });

    private void RefreshInstanceTabs()
    {
        foreach (var tab in _tabs.OfType<InstanceTab>())
        {
            var template = AssetIndex.Resolve(tab.Data.TemplateId);
            tab.TemplateInterface = template?.Interface;
            if (template is null) continue;

            var validNames = template.Interface.Inputs.Select(i => i.Name).ToHashSet();
            foreach (var key in tab.Data.Values.Keys.Except(validNames).ToList())
                tab.Data.Values.Remove(key);
            foreach (var inp in template.Interface.Inputs)
                if (!tab.Data.Values.ContainsKey(inp.Name))
                    tab.Data.Values[inp.Name] = inp.Default ?? "";
        }
    }
}
