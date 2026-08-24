using System.IO;
using System.Text.Json;

namespace NED.Shell.Wpf;

/// <summary>
/// Perzistence volby jazyka do %AppData%/NED/settings.json.
/// Čteno v App ctoru (před prvním renderem) — proto soubor, ne localStorage.
/// </summary>
internal static class LanguageStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NED", "settings.json");

    private sealed record Settings(string? Language);

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath))?.Language;
        }
        catch { return null; }
    }

    public static void Save(string culture)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Settings(culture)));
        }
        catch { /* best effort — neblokuj UI kvůli settings */ }
    }
}
