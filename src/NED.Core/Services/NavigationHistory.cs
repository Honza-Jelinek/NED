namespace NED.Core;

/// <summary>
/// Lineární historie navigace mezi taby (back/forward jako prohlížeč/VS Code).
/// Drží seznam navštívených tabů a kurzor; nová návštěva zahodí "forward" větev.
/// </summary>
public sealed class NavigationHistory
{
    private readonly List<TabBase> _entries = new();
    private int _cursor = -1;

    public bool CanBack => _cursor > 0;
    public bool CanForward => _cursor >= 0 && _cursor < _entries.Count - 1;

    /// <summary>Zaznamená návštěvu tabu. Pokud je už aktuální, nic nedělá; jinak zahodí forward větev a posune kurzor na konec.</summary>
    public void Visit(TabBase tab)
    {
        if (_cursor >= 0 && _entries[_cursor] == tab) return;

        // Zahoď vše za kurzorem (rozvětvení historie).
        if (_cursor < _entries.Count - 1)
            _entries.RemoveRange(_cursor + 1, _entries.Count - _cursor - 1);

        _entries.Add(tab);
        _cursor = _entries.Count - 1;
    }

    /// <summary>Posun zpět. Vrací cílový tab, nebo null pokud není kam.</summary>
    public TabBase? Back()
    {
        if (!CanBack) return null;
        _cursor--;
        return _entries[_cursor];
    }

    /// <summary>Posun vpřed. Vrací cílový tab, nebo null pokud není kam.</summary>
    public TabBase? Forward()
    {
        if (!CanForward) return null;
        _cursor++;
        return _entries[_cursor];
    }

    /// <summary>Odstraní všechny výskyty zavřeného tabu a srovná kurzor (volá se při zavření tabu).</summary>
    public void Remove(TabBase tab)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i] != tab) continue;
            _entries.RemoveAt(i);
            if (i <= _cursor) _cursor--;
        }

        // Slij sousední duplikáty, aby back/forward nedělaly "prázdné" kroky.
        for (int i = _entries.Count - 1; i > 0; i--)
        {
            if (_entries[i] != _entries[i - 1]) continue;
            _entries.RemoveAt(i);
            if (i <= _cursor) _cursor--;
        }

        if (_cursor < -1) _cursor = -1;
        if (_cursor >= _entries.Count) _cursor = _entries.Count - 1;
    }
}
