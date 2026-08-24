namespace NED.Core;

/// <summary>
/// Sdílené čtení/zápis hodnoty <see cref="NodeInput"/>. Jediný zdroj pravdy pro konverze —
/// používá ho inline widget na uzlu (<c>DataNodeWidget</c>) i Details panel (<c>NodeFieldEditor</c>).
/// </summary>
public static class NodeFieldIO
{
    /// <summary>Hodnota pole jako řetězec.</summary>
    public static string? Read(DataNodeModel node, NodeInput input) =>
        node.ValueAsString(input.Name) ?? (input.Complex ? "" : null);

    /// <summary>
    /// Zapíše hodnotu pole. Zaznamená undo a podle druhu vstupu dorovná navazující stav
    /// (type-picker mění typ výstupního portu, popis se propisuje na port).
    /// </summary>
    public static void Write(DataNodeModel node, NodeInput input, object? raw,
                             Action? recordUndo, Action? revalidate)
    {
        recordUndo?.Invoke();

        var text = raw?.ToString() ?? "";

        if (input.TypePicker)
        {
            node.Values[input.Name] = text;
            node.RefreshDynamicTypes();
            revalidate?.Invoke();   // změna typu může rozbít navazující linky
            return;
        }

        // Doménový vstup drží výběr jako řetězec (jméno typu nebo "sg:GUID") — nekonvertuj.
        node.Values[input.Name] = input.Complex
            ? text
            : ValueFormat.Parse(text, input.DataType,
                                node.Values.TryGetValue(input.Name, out var old) ? old : null);

        if (node.IsGraphInputNode && input.Name == Manifest.BuiltInIds.GraphInputMultiple)
        {
            node.RefreshDynamicTypes();
            revalidate?.Invoke();
            return;
        }

        node.SyncOutputMetadata();
        node.Refresh();
    }
}
