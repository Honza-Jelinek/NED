using System.Reflection;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;

namespace NED.Core;

/// <summary>
/// Konfigurace NEDa. Hostitel sem registruje <b>manifesty</b> se svými typy uzlů —
/// editor při načítání doménu nezná a nespouští její kód (viz docs/14-manifest.md).
///
/// Assembly se registrují jen kvůli <see cref="Abstractions.IExportTranslator"/>, což je
/// jediné rozšíření, které v editoru skutečně běží.
/// </summary>
public sealed class NedOptions
{
    internal List<Assembly> TranslatorAssemblies { get; } = new();
    internal List<NodeManifest> Manifests { get; } = new();
    internal List<string> ManifestPaths { get; } = new();
    internal event Action? PackFilesChanged;

    /// <summary>
    /// Packy, které se nepodařilo načíst. Sbírají se, protože <c>AddNed</c> běží ještě před
    /// <c>BuildServiceProvider()</c> — <see cref="INedNotifier"/> v tu chvíli neexistuje, takže
    /// chyba nemá kam odejít. Katalog je převezme a UI je vysype do Problems panelu.
    /// </summary>
    internal List<NedNotice> LoadIssues { get; } = new();

    /// <summary>
    /// Cesta k JSON souboru s workspace konfigurací (knihovní rooty + manifesty).
    /// null = neukládat napříč sezeními (jen in-memory). Dodá hostitel.
    /// </summary>
    public string? LibraryConfigPath { get; set; }

    /// <summary>Styling uzlů (kategorie → default vzhled). Předvyplněno z embedded ned-theme.json.</summary>
    internal NedTheme Theme { get; set; } = NedTheme.LoadFromEmbedded();

    /// <summary>
    /// Zaregistruje node pack ze souboru. Načte se až při sestavení katalogu, kdy už jsou
    /// známé workspace overrides. Poškozený soubor nesmí shodit start editoru, ale
    /// nesmí ani zmizet beze stopy — jinak uživatel uvidí placeholdery a nedozví se proč.
    /// </summary>
    public NedOptions Manifest(string jsonFilePath)
    {
        var path = Assets.WorkspaceConfig.PathKey(jsonFilePath);
        if (!ManifestPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            ManifestPaths.Add(path);
        return this;
    }

    /// <summary>
    /// Načte všechny <c>*.nodes.json</c> ze složky — konvence „packy dodané s aplikací".
    /// Patří sem, a ne do každého hostitele zvlášť; jinak se ten cyklus opíše v každém shellu.
    /// Neexistující složka je v pořádku (aplikace bez vlastních packů).
    /// </summary>
    public NedOptions ManifestFolder(string directory)
    {
        if (!Directory.Exists(directory)) return this;

        foreach (var file in Directory.EnumerateFiles(directory, ManifestFile.SearchPattern).OrderBy(f => f))
            Manifest(file);

        return this;
    }

    private static NedNotice Failure(string path, string reason) =>
        new(NedNoticeSeverity.Error, "Notice_PackLoadFailed", new object?[] { Path.GetFileName(path), reason });

    /// <summary>
    /// Soubory registrované hostitelem a ve workspace sloučí podle cesty. Hostitelský soubor je
    /// implicitně zapnutý; explicitní workspace záznam jej může vypnout. Načtení se materializuje
    /// před konstrukcí katalogu, aby se případné chyby dostaly do <see cref="LoadIssues"/>.
    /// </summary>
    internal IReadOnlyList<NodeManifest> LoadFileManifests()
    {
        var manifests = ReloadFileManifests(out var issues);
        LoadIssues.Clear();
        LoadIssues.AddRange(issues);
        return manifests;
    }

    internal IReadOnlyList<NodeManifest> ReloadFileManifests(out IReadOnlyList<NedNotice> issues)
    {
        var failures = new List<NedNotice>();
        var workspacePacks = Assets.WorkspaceConfig.Load(LibraryConfigPath).EffectivePacks();
        var overrides = new Dictionary<string, Assets.WorkspacePack>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in workspacePacks)
            overrides[Assets.WorkspaceConfig.PathKey(pack.Path)] = pack;
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ManifestPaths)
        {
            var key = Assets.WorkspaceConfig.PathKey(path);
            if (seen.Add(key) && (!overrides.TryGetValue(key, out var configured) || configured.Enabled))
                paths.Add(path);
        }

        foreach (var pack in workspacePacks)
        {
            var key = Assets.WorkspaceConfig.PathKey(pack.Path);
            if (pack.Enabled && seen.Add(key)) paths.Add(pack.Path);
        }

        var manifests = paths.Select(path => ReadManifest(path, failures)).OfType<NodeManifest>().ToList();
        issues = failures;
        return manifests;
    }

    internal IReadOnlyList<string> PackFilePaths()
    {
        var workspace = Assets.WorkspaceConfig.Load(LibraryConfigPath).EffectivePacks();
        return ManifestPaths.Concat(workspace.Select(pack => pack.Path))
            .Select(Assets.WorkspaceConfig.PathKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal void NotifyPackFilesChanged() => PackFilesChanged?.Invoke();

    private static NodeManifest? ReadManifest(string path, List<NedNotice> issues)
    {
        try
        {
            if (!File.Exists(path))
            {
                issues.Add(Failure(path, "file not found"));
                return null;
            }

            if (ManifestJson.ReadFile(path) is { } manifest)
            {
                if (ManifestJson.IsNewerThanSupported(manifest))
                {
                    issues.Add(new NedNotice(NedNoticeSeverity.Warning, "Notice_ManifestVersionNewer",
                        new object?[] { Path.GetFileName(path), manifest.ManifestVersion, NodeManifest.Current }));
                }
                return manifest;
            }
            issues.Add(Failure(path, "empty manifest"));
        }
        catch (Exception ex)
        {
            issues.Add(Failure(path, ex.Message));
        }
        return null;
    }

    /// <summary>Node pack sestavený v paměti (testy, hostitel s vlastním zdrojem metadat).</summary>
    public NedOptions Manifest(NodeManifest manifest)
    {
        Manifests.Add(manifest);
        return this;
    }

    /// <summary>Assembly s vlastními <see cref="Abstractions.IExportTranslator"/> implementacemi.</summary>
    public NedOptions RegisterTranslators(Assembly assembly)
    {
        if (!TranslatorAssemblies.Contains(assembly))
            TranslatorAssemblies.Add(assembly);
        return this;
    }

    public NedOptions RegisterTranslatorsOf<T>() => RegisterTranslators(typeof(T).Assembly);

    /// <summary>Kam uložit workspace (knihovní rooty + manifesty), aby přežil restart.</summary>
    public NedOptions LibraryConfig(string jsonFilePath)
    {
        LibraryConfigPath = jsonFilePath;
        return this;
    }

    /// <summary>Přepíše/přidá default vzhled pro kategorii (barva headeru + ikona).</summary>
    public NedOptions Style(string category, string color, string icon)
    {
        Theme.Set(category, color, icon);
        return this;
    }

    /// <summary>Načte theme z JSON souboru. Hodnoty se mergeují přes builtin defaults.</summary>
    public NedOptions LoadTheme(string jsonFilePath)
    {
        Theme = NedTheme.LoadFromFile(jsonFilePath);
        return this;
    }

    /// <summary>Přepíše barvu portu pro daný type id (např. "double", "#00ff00").</summary>
    public NedOptions PortColor(string typeName, string color)
    {
        Theme.SetPortColor(typeName, color);
        return this;
    }
}
