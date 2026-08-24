using System.IO;

namespace NED.Shell.Wpf;

/// <summary>
/// Perzistence layoutu dokovatelných panelů do %AppData%/NED/layout.json.
/// Obsah je neprůhledný golden-layout JSON (viz ned-dock.js) — host ho jen ukládá/čte.
/// </summary>
internal static class LayoutFileStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NED", "layout.json");

    public static string? Load()
    {
        try { return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null; }
        catch { return null; }
    }

    public static void Save(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, json);
        }
        catch { /* best effort — neblokuj UI kvůli layoutu */ }
    }
}
