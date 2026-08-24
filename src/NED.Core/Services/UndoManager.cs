using Blazor.Diagrams;
using NED.Core.Assets;
using NED.Core.Persistence;

namespace NED.Core;

/// <summary>
/// Per-tab undo/redo. Snapshot-based — každý krok = kompletní serializace diagramu.
/// Model „commit po změně proti baseline": <see cref="_current"/> drží stav po posledním
/// commitu; <see cref="CommitIfChanged"/> porovná aktuální diagram s baseline a jen když
/// se liší, zaznamená krok. Žádné pre-mutation snapshoty se nevolají — každá skutečná
/// mutace (add/remove/move/field/link) prostě zavolá commit a rozdíl se vyhodnotí.
///
/// Historie je uložená v pevných kruhových bufferech (fixed capacity <see cref="MaxHistory"/>) —
/// push/pop v O(1), starý krok se přepíše nejmladším bez alokace.
/// </summary>
public sealed class UndoManager
{
    private const int MaxHistory = 100;

    private readonly string?[] _undo = new string?[MaxHistory];
    private readonly string?[] _redo = new string?[MaxHistory];
    private int _undoHead, _undoCount;
    private int _redoHead, _redoCount;

    /// <summary>Serializovaný stav k poslednímu commitu (nebo Reset). Baseline pro diff.</summary>
    private string? _current;

    /// <summary>
    /// Když true, <see cref="CommitIfChanged"/> nedělá nic. Nastavuje se během restore
    /// (undo/redo), aby eventy vyvolané <c>LoadInto</c> nezaznamenaly krok navíc.
    /// </summary>
    public bool Suspended { get; private set; }

    public bool CanUndo => _undoCount > 0;
    public bool CanRedo => _redoCount > 0;

    /// <summary>
    /// Nastaví baseline = aktuální stav diagramu a smaže historii. Volat po Load / vytvoření
    /// nového tabu, aby první uživatelská mutace zapsala krok proti reálnému výchozímu stavu
    /// (a ne proti prázdnému grafu, což by dělalo „falešný" první undo krok).
    /// </summary>
    public void Reset(BlazorDiagram diagram, GraphSettings settings)
    {
        _current = Serialize(diagram, settings);
        Array.Clear(_undo); _undoHead = 0; _undoCount = 0;
        Array.Clear(_redo); _redoHead = 0; _redoCount = 0;
    }

    /// <summary>
    /// Jádro nového modelu: porovná aktuální stav s baseline a jen když se liší, pushne
    /// starou baseline na undo stack a novou hodnotu si zapamatuje jako novou baseline.
    /// Redo stack se maže (nová větev historie). Vrací true, když skutečně přibyl krok —
    /// caller podle toho může MarkDirty (nechceme označovat dirty, když se nic nezměnilo).
    /// </summary>
    public bool CommitIfChanged(BlazorDiagram diagram, GraphSettings settings)
    {
        if (Suspended) return false;

        var now = Serialize(diagram, settings);
        if (now == _current) return false;

        PushUndo(_current);
        _current = now;
        ClearRedo();
        return true;
    }

    /// <summary>
    /// Vrátí graf o krok zpět. Aktuální baseline se přesune na redo stack, undo se popne
    /// a stane se novou baseline (žádná serializace navíc — všechno už je hotové).
    /// Vrací nové <see cref="GraphSettings"/> obnovené z uloženého dokumentu (mohou se lišit).
    /// </summary>
    public GraphSettings? Undo(BlazorDiagram diagram, GraphSettings current,
                               NedCatalog catalog, AssetIndex? assetIndex)
    {
        if (_undoCount == 0) return null;

        // Aktuální baseline (= _current, ne re-serializovat) → redo.
        PushRedo(_current ?? Serialize(diagram, current));
        var target = PopUndo()!;
        _current = target;
        return Restore(diagram, target, catalog, assetIndex);
    }

    /// <summary>Opak <see cref="Undo"/>.</summary>
    public GraphSettings? Redo(BlazorDiagram diagram, GraphSettings current,
                               NedCatalog catalog, AssetIndex? assetIndex)
    {
        if (_redoCount == 0) return null;

        PushUndo(_current ?? Serialize(diagram, current));
        var target = PopRedo()!;
        _current = target;
        return Restore(diagram, target, catalog, assetIndex);
    }

    private GraphSettings Restore(BlazorDiagram diagram, string json,
                                  NedCatalog catalog, AssetIndex? assetIndex)
    {
        Suspended = true;
        try
        {
            var doc = GraphPersistence.Deserialize(json)!;
            return GraphPersistence.LoadInto(diagram, doc, catalog, assetIndex);
        }
        finally { Suspended = false; }
    }

    private static string Serialize(BlazorDiagram diagram, GraphSettings settings) =>
        GraphPersistence.Serialize(GraphPersistence.ToDocument(diagram, settings));

    // ── Ring buffers ─────────────────────────────

    private void PushUndo(string? s)
    {
        _undo[_undoHead] = s;
        _undoHead = (_undoHead + 1) % MaxHistory;
        if (_undoCount < MaxHistory) _undoCount++;
    }

    private string? PopUndo()
    {
        _undoHead = (_undoHead - 1 + MaxHistory) % MaxHistory;
        var s = _undo[_undoHead];
        _undo[_undoHead] = null;
        _undoCount--;
        return s;
    }

    private void PushRedo(string? s)
    {
        _redo[_redoHead] = s;
        _redoHead = (_redoHead + 1) % MaxHistory;
        if (_redoCount < MaxHistory) _redoCount++;
    }

    private string? PopRedo()
    {
        _redoHead = (_redoHead - 1 + MaxHistory) % MaxHistory;
        var s = _redo[_redoHead];
        _redo[_redoHead] = null;
        _redoCount--;
        return s;
    }

    private void ClearRedo()
    {
        Array.Clear(_redo);
        _redoHead = 0;
        _redoCount = 0;
    }
}
