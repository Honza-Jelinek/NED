using System.Reflection;
using NED.Abstractions;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core;

/// <summary>
/// Co editor ví o typech uzlů. Zdrojem jsou <b>manifesty</b>, ne reflexe — editor nemusí
/// být zkompilovaný proti doméně a při načítání nespouští její kód. Viz docs/14-manifest.md.
///
/// Assembly se sem pořád registrují, ale jen kvůli <see cref="IExportTranslator"/> —
/// ten je na rozdíl od typů uzlů skutečný kód, který v editoru běží.
/// </summary>
public sealed class NedCatalog
{
    private CatalogState _state = new();
    private readonly Dictionary<string, IExportTranslator> _translatorsById;

    /// <summary>Všechny známé typy uzlů napříč načtenými packy.</summary>
    public IReadOnlyList<NodeTypeDescriptor> AllTypes => Volatile.Read(ref _state).All;

    /// <summary>Načtené packy (identita + verze). Slouží k hlášení chybějícího packu při otevření grafu.</summary>
    public IReadOnlyList<PackInfo> Packs => Volatile.Read(ref _state).Packs;

    /// <summary>
    /// Co se při stavbě katalogu nepovedlo — nenačtený pack, dvakrát načtený pack, kolize
    /// type id. UI to vysype do Problems panelu; katalog sám notifier nemá, protože vzniká
    /// dřív než zbytek DI.
    /// </summary>
    public IReadOnlyList<NedNotice> Issues => Volatile.Read(ref _state).Issues;

    /// <summary>
    /// Zobrazovaná jména, která nosí víc typů naráz. Paleta u nich musí ukázat i pack,
    /// jinak jsou dva různé „Add" k nerozeznání. U ostatních by ten text byl jen šum.
    /// </summary>
    public IReadOnlySet<string> AmbiguousNames => Volatile.Read(ref _state).AmbiguousNames;

    /// <summary>Discoverované translatory exportu (vestavěný JSON + hostitelem registrované). První je vždy default.</summary>
    public IReadOnlyList<IExportTranslator> ExportTranslators { get; }

    public IExportTranslator DefaultTranslator { get; }

    public event Action? Changed;

    public NedCatalog(IEnumerable<NodeManifest> manifests,
                      IEnumerable<Assembly>? translatorAssemblies = null,
                      IEnumerable<NedNotice>? loadIssues = null)
    {
        Apply(Build(manifests, loadIssues));

        DefaultTranslator = new DefaultJsonExportTranslator();
        var translators = new List<IExportTranslator> { DefaultTranslator };
        _translatorsById = new Dictionary<string, IExportTranslator>(StringComparer.Ordinal)
        {
            [DefaultTranslator.Id] = DefaultTranslator,
        };

        foreach (var t in (translatorAssemblies ?? Array.Empty<Assembly>())
                     .SelectMany(SafeGetTypes)
                     .Where(t => typeof(IExportTranslator).IsAssignableFrom(t)
                                 && t is { IsAbstract: false, IsInterface: false }
                                 && t != typeof(DefaultJsonExportTranslator))
                     .Distinct())
        {
            if (Activator.CreateInstance(t) is not IExportTranslator instance) continue;
            if (_translatorsById.ContainsKey(instance.Id)) continue; // first-wins
            _translatorsById[instance.Id] = instance;
            translators.Add(instance);
        }
        ExportTranslators = translators;
    }

    public void Reload(IEnumerable<NodeManifest> manifests, IEnumerable<NedNotice>? loadIssues = null)
    {
        Apply(Build(manifests, loadIssues));
        Changed?.Invoke();
    }

    private static CatalogState Build(IEnumerable<NodeManifest> manifests, IEnumerable<NedNotice>? loadIssues)
    {
        var state = new CatalogState();
        if (loadIssues is not null) state.Issues.AddRange(loadIssues);
        AddManifest(state, ManifestJson.BuiltIn());
        foreach (var manifest in manifests) AddManifest(state, manifest);
        state.All.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        state.AmbiguousNames = state.All.GroupBy(t => t.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        return state;
    }

    private void Apply(CatalogState state) => Volatile.Write(ref _state, state);

    private static void AddManifest(CatalogState state, NodeManifest manifest)
    {
        // Pack id není globálně registrované. Nekolidující typy se mergují; konkrétní
        // type id zůstává deterministicky first-wins.
        if (state.Packs.Any(p => string.Equals(p.Id, manifest.Pack.Id, StringComparison.Ordinal)))
        {
            state.Issues.Add(new NedNotice(NedNoticeSeverity.Warning, "Notice_PackConflict",
                new object?[] { manifest.Pack.Id }));
        }

        state.Packs.Add(manifest.Pack);

        foreach (var e in manifest.Enums)
            if (!state.Enums.ContainsKey(e.Id))
                state.Enums[e.Id] = e;

        foreach (var t in manifest.Types)
        {
            // Jen hlásit. Descriptor patří tomu, kdo manifest předal (NedOptions.Manifest
            // drží tytéž instance napříč reloady), takže přepsat ho tady by hlášení
            // umlčelo hned při prvním hot reloadu. Na port to srovná DataNodeModel.
            foreach (var input in t.Inputs.Where(input =>
                         input.Type == TypeIds.Exec &&
                         (input.Kind != InputKind.Port || input.Details)))
            {
                state.Issues.Add(new NedNotice(NedNoticeSeverity.Warning, "Notice_ExecMustBePort",
                    new object?[] { t.Id, input.Name }));
            }

            // Kolize napříč různými packy — pack prohlásil typ s cizím prefixem.
            // First-wins zůstává (deterministické), ale mlčky ne.
            if (state.PackOfType.TryGetValue(t.Id, out var owner))
            {
                state.Issues.Add(new NedNotice(NedNoticeSeverity.Warning, "Notice_TypeConflict",
                    new object?[] { t.Id, owner, manifest.Pack.Id }));
                continue;
            }

            // Ruční manifest snadno pojmenuje dva výstupy stejně. Model si vezme první,
            // ale mlčky by z toho vznikl port, na který nejde adresovat link — a při
            // uložení by linky na něj zmizely.
            foreach (var duplicate in t.Outputs
                         .GroupBy(o => o.Name, StringComparer.Ordinal)
                         .Where(g => g.Count() > 1))
            {
                state.Issues.Add(new NedNotice(NedNoticeSeverity.Warning, "Notice_DuplicateOutputName",
                    new object?[] { t.Id, duplicate.Key }));
            }

            state.ById[t.Id] = t;
            state.PackOfType[t.Id] = manifest.Pack.Id;
            state.All.Add(t);
        }
    }

    /// <summary>Pack, ze kterého typ pochází. null = neznámý typ.</summary>
    public string? PackOf(string? typeId) =>
        typeId is not null && Volatile.Read(ref _state).PackOfType.TryGetValue(typeId, out var pack) ? pack : null;

    // ── Typy uzlů ────────────────────────────────────────────

    /// <summary>Typ uzlu podle id. null = neznámý (chybí pack) — volající udělá placeholder.</summary>
    public NodeTypeDescriptor? Resolve(string? typeId) =>
        typeId is null ? null : Volatile.Read(ref _state).ById.TryGetValue(typeId, out var t) ? t : null;

    public IEnumerable<IGrouping<string, NodeTypeDescriptor>> ByCategory() =>
        AllTypes.GroupBy(t => t.Category);

    /// <summary>Hodnoty enumu pro dropdown; null = není enum.</summary>
    public IReadOnlyList<string>? EnumValues(string? typeId) =>
        typeId is not null && Volatile.Read(ref _state).Enums.TryGetValue(typeId, out var e) ? e.Values : null;

    /// <summary>Předkové typu (pro kompatibilitu portů). Prázdné u neznámého typu.</summary>
    public IReadOnlyList<string> ExtendsOf(string? typeId) =>
        Resolve(typeId)?.Extends ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>Kompatibilita bez portů (picker filtry) — předky si dohledá sám.</summary>
    public bool IsCompatible(string outputTypeId, string inputTypeId) =>
        TypeIds.IsCompatible(outputTypeId, ExtendsOf(outputTypeId), inputTypeId);

    /// <summary>
    /// Volitelné datové typy (output typ grafu i type-picker vstupů): skaláry + vše,
    /// co jde v katalogu vyprodukovat. Vynechává vestavěné interface uzly.
    /// </summary>
    public IReadOnlyList<string> SelectableTypes()
    {
        var types = new List<string> { TypeIds.Any };   // Any první
        types.AddRange(new[]
        {
            TypeIds.Bool, TypeIds.Int, TypeIds.Long, TypeIds.Float,
            TypeIds.Double, TypeIds.Decimal, TypeIds.String,
        });

        foreach (var t in AllTypes)
        {
            if (t.Id is BuiltInIds.Output or BuiltInIds.GraphInput) continue;
            foreach (var output in t.Outputs)
            {
                var id = output.Type ?? t.Id;
                if (id != TypeIds.Exec && !types.Contains(id)) types.Add(id);
            }
        }
        return types;
    }

    /// <summary>
    /// Vstupní porty uzlu i s aritou — pro filtr pickeru „co tenhle uzel přijme".
    /// Arita musí být součástí odpovědi: nabídnout uzel, na který se pak link nedá
    /// dotáhnout, znamená položit na plátno osamocený uzel.
    /// </summary>
    public static IEnumerable<(string Type, bool Multiple)> InputPorts(NodeTypeDescriptor type) =>
        type.Inputs.Where(i => i.Kind == InputKind.Port).Select(i => (i.Type, i.Multiple));

    /// <summary>Výstupní porty uzlu i s aritou — druhá strana téhož filtru.</summary>
    public static IEnumerable<(string Type, bool Multiple)> OutputPorts(NodeTypeDescriptor type) =>
        type.Outputs.Select(output => (output.Type ?? type.Id, output.Multiple));

    /// <summary>Co uzel produkuje na výstupu (bez arity — pro dropdown typů).</summary>
    public static IEnumerable<string> OutputTypesOf(NodeTypeDescriptor type) =>
        type.Outputs.Select(output => output.Type ?? type.Id);

    /// <summary>
    /// Má uzel řídicí pin? V datovém grafu takový uzel nemá co dělat — exec hrany se
    /// do exportu vůbec nedostanou, takže by tam jen visel a mlčky nic nedělal.
    /// </summary>
    public static bool HasExecPort(NodeTypeDescriptor type) =>
        type.Inputs.Any(input => input.Type == TypeIds.Exec)
        || type.Outputs.Any(output => output.Type == TypeIds.Exec);

    // ── Export translatory ───────────────────────────────────

    /// <summary>Najde translator podle Id; null/nenalezeno → vestavěný default.</summary>
    public IExportTranslator FindTranslator(string? id) =>
        !string.IsNullOrEmpty(id) && _translatorsById.TryGetValue(id!, out var t) ? t : DefaultTranslator;

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private sealed class CatalogState
    {
        public Dictionary<string, NodeTypeDescriptor> ById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, EnumDescriptor> Enums { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> PackOfType { get; } = new(StringComparer.Ordinal);
        public List<NodeTypeDescriptor> All { get; } = new();
        public List<PackInfo> Packs { get; } = new();
        public List<NedNotice> Issues { get; } = new();
        public IReadOnlySet<string> AmbiguousNames { get; set; } = new HashSet<string>(StringComparer.Ordinal);
    }
}
