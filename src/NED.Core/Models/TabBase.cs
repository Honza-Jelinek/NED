namespace NED.Core;

public abstract class TabBase
{
    private static int _counter;

    public int Index { get; } = ++_counter;
    public string? FilePath { get; set; }
    public bool IsDirty { get; set; }

    /// <summary>
    /// Rodič v řetězci zanoření (tab, z něhož byl tenhle otevřen drill-inem do subgrafu).
    /// null = kořen (otevřeno z knihovny / souboru). Slouží breadcrumb liště.
    /// </summary>
    public TabBase? OpenedFrom { get; set; }
    public abstract string Title { get; }
    public abstract string Icon { get; }
    public abstract bool CanExport { get; }
}
