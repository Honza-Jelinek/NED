using System.Text.Json;
using System.Text.Json.Serialization;

namespace NED.Core.Assets;

/// <summary>
/// Co tenhle editor vidí: knihovní kořeny s assety a node packy (manifesty).
///
/// Volba packu patří sem, ne do session — jinak si ji uživatel vybírá při každém spuštění
/// a špatná volba udělá z celého grafu placeholdery.
/// </summary>
public sealed class Workspace
{
    /// <summary>Adresáře, ve kterých se hledají <c>*.nedgraph.json</c> a <c>*.nedinst.json</c>.</summary>
    public List<string> Roots { get; set; } = new();

    /// <summary>Cesty k manifestům node packů (viz docs/14-manifest.md).</summary>
    public List<string> Manifests { get; set; } = new();

    /// <summary>
    /// Node packy v novém rozšiřitelném formátu. <see cref="Manifests"/> se dál čte kvůli
    /// kompatibilitě; při první uživatelské změně se položky převedou sem.
    /// </summary>
    public List<WorkspacePack> Packs { get; set; } = new();

    /// <summary>
    /// Výchozí hodnota <c>GraphSettings.Instanceable</c> pro nově založený graf. Preference
    /// patří sem, ne do session — a tohle je zatím jediná; až jich bude víc, dostanou vlastní
    /// sekci. UI pro její nastavení ještě není, per-graf přepínač je v Details panelu.
    /// </summary>
    public bool NewGraphInstanceable { get; set; }

    public IReadOnlyList<WorkspacePack> EffectivePacks()
    {
        var result = new List<WorkspacePack>();
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in Packs)
        {
            var key = WorkspaceConfig.PathKey(pack.Path);
            if (positions.TryGetValue(key, out var position)) result[position] = pack;
            else
            {
                positions[key] = result.Count;
                result.Add(pack);
            }
        }

        foreach (var path in Manifests)
        {
            var key = WorkspaceConfig.PathKey(path);
            if (positions.ContainsKey(key)) continue;
            positions[key] = result.Count;
            result.Add(new WorkspacePack { Path = path });
        }
        return result;
    }
}

/// <summary>Jedna uživatelem spravovaná cesta k manifestu a volitelný recept pro jeho regeneraci.</summary>
public sealed class WorkspacePack
{
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public WorkspacePackGeneration? Generation { get; set; }
}

/// <summary>
/// Jazykově neutrální provenance manifestu. Provider si vykládá <see cref="Options"/>;
/// editor pouze uchovává recept a umí poznat, zda je daný provider nainstalovaný.
/// </summary>
public sealed class WorkspacePackGeneration
{
    public string Provider { get; set; } = "";
    public string Source { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = new();
}

/// <summary>Čtení/zápis <see cref="Workspace"/>. Best-effort — poškozený config nesmí shodit start.</summary>
public static class WorkspaceConfig
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string PathKey(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not normalize workspace path: {ex.Message}");
            return path;
        }
    }

    public static Workspace Load(string? path)
    {
        if (path is null || !File.Exists(path)) return new Workspace();

        try
        {
            var json = File.ReadAllText(path);

            // Starší formát byl holé pole kořenů. Načti ho, ať uživatel nepřijde o knihovny.
            if (json.TrimStart().StartsWith('['))
                return new Workspace { Roots = JsonSerializer.Deserialize<List<string>>(json) ?? new() };

            return JsonSerializer.Deserialize<Workspace>(json, JsonOpts) ?? new Workspace();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Workspace config read failed: {ex.Message}");
            return new Workspace();
        }
    }

    public static void Save(string? path, Workspace workspace)
    {
        if (path is null) return;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(workspace, JsonOpts));
        }
        catch (Exception ex)
        {
            // Nezapsaný config znamená jen ztrátu nastavení po restartu, ne ztrátu dat.
            System.Diagnostics.Debug.WriteLine($"Workspace config write failed: {ex.Message}");
        }
    }
}
