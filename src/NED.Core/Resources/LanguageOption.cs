using System.Globalization;
using System.IO;

namespace NED.Core.Resources;

public sealed record LanguageOption(string Culture, string NativeName);

/// <summary>
/// Zjistí dostupné jazyky za běhu: neutrální (en, zabudovaný ve hlavním assembly)
/// + všechny satelitní assembly (složky cs/, de/, … vedle NED.Core.dll).
/// Přidání Strings.xx.resx → nový satelit → jazyk se objeví automaticky.
/// </summary>
public sealed class LanguageRegistry
{
    public IReadOnlyList<LanguageOption> Languages { get; }

    public LanguageRegistry()
    {
        var asm = typeof(LanguageRegistry).Assembly;
        var cultures = new List<string> { "en" }; // neutrální = angličtina (Strings.resx)

        try
        {
            var dir = Path.GetDirectoryName(asm.Location);
            if (!string.IsNullOrEmpty(dir))
            {
                var satellite = Path.GetFileNameWithoutExtension(asm.Location) + ".resources.dll";
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (!File.Exists(Path.Combine(sub, satellite))) continue;
                    var culture = Path.GetFileName(sub);
                    if (!cultures.Contains(culture)) cultures.Add(culture);
                }
            }
        }
        catch { /* běž s tím co máme (aspoň en) */ }

        Languages = cultures.Select(c => new LanguageOption(c, NativeNameFor(c))).ToList();
    }

    private static string NativeNameFor(string culture) => culture switch
    {
        "en" => "English",
        "cs" => "Čeština",
        _ => TryNativeName(culture),
    };

    private static string TryNativeName(string culture)
    {
        try { return CultureInfo.GetCultureInfo(culture).NativeName; }
        catch { return culture; }
    }
}
