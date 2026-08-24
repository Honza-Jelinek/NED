namespace NED.Abstractions.Manifest;

/// <summary>
/// Konvence pojmenování souborů manifestů. Sdílí ji generátor (výchozí název výstupu)
/// i editor (hledání packů ve složce) — kdyby žila na dvou místech, rozejdou se.
/// </summary>
public static class ManifestFile
{
    /// <summary>Přípona manifestu node packu.</summary>
    public const string Extension = ".nodes.json";

    /// <summary>Maska pro vyhledání manifestů ve složce.</summary>
    public const string SearchPattern = "*" + Extension;
}
