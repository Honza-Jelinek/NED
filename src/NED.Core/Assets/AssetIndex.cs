using System.Collections.Concurrent;
using System.Text.Json;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using NED.Core.Persistence;

namespace NED.Core.Assets;

/// <summary>
/// Skenuje knihovní složky (asset roots), čte z každého <c>*.nedgraph.json</c>
/// vložený GUID + rozhraní a buduje mapu GUID → <see cref="AssetEntry"/>.
/// Reference subgrafů se resolvují právě zde. Seznam rootů se (volitelně)
/// persistuje do konfiguračního souboru, jehož cestu dodá hostitel.
///
/// Plný <see cref="Rescan"/> běží jen při startu a při přidání/odebrání rootu.
/// Vlastní zápisy editoru (save/rename/delete/duplicate) volají <see cref="UpdateFile"/>/
/// <see cref="RemoveFile"/>/<see cref="MoveFile"/> — přeindexuje se jen dotčený soubor.
/// Externí změny (jiný proces, git checkout…) hlídá <see cref="FileSystemWatcher"/>
/// per root s debounce, aby dávkové operace (git pull) nevyvolaly bouři reindexů.
/// </summary>
public sealed class AssetIndex : IDisposable
{
    private const int DebounceMs = 400;

    private readonly INedNotifier _notifier;
    private readonly string? _configPath;
    private readonly List<string> _roots = new();
    private readonly object _gate = new();

    private Dictionary<Guid, AssetEntry> _byId = new();
    private List<AssetEntry> _entries = new();
    private Dictionary<string, Guid> _idByPath = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<Guid, InstanceEntry> _instancesById = new();
    private List<InstanceEntry> _instances = new();
    private Dictionary<string, Guid> _instIdByPath = new(StringComparer.OrdinalIgnoreCase);

    // Snapshoty pro bezpečné čtení bez zámku (Blazor render může běžet souběžně s watcher mutací).
    private volatile IReadOnlyList<AssetEntry> _entriesSnapshot = Array.Empty<AssetEntry>();
    private volatile IReadOnlyList<InstanceEntry> _instancesSnapshot = Array.Empty<InstanceEntry>();

    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _debounceTimer;
    private bool _disposed;

    /// <summary>Vyvolá se po každém přeskenování / inkrementální změně / změně rootů (UI refresh).
    /// Může přijít z libovolného vlákna (watcher debounce timer) — odběratel musí marshalovat.</summary>
    public event Action? Changed;

    public AssetIndex(NedOptions options, INedNotifier notifier)
    {
        _notifier = notifier;
        _configPath = options.LibraryConfigPath;
        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        LoadRoots();
        Rescan();
        foreach (var root in _roots) StartWatcher(root);
    }

    public IReadOnlyList<string> Roots => _roots;
    public IReadOnlyList<AssetEntry> Entries => _entriesSnapshot;

    /// <summary>Výchozí <c>Instanceable</c> pro nový graf — z workspace configu.</summary>
    public bool NewGraphInstanceable => WorkspaceConfig.Load(_configPath).NewGraphInstanceable;

    /// <summary>
    /// Assety vkládatelné jako SubgraphNode — všechny. Jestli konkrétní graf do konkrétního
    /// grafu patří, rozhodne <see cref="SubgraphInterface.CanBePlacedIn"/>; sebe-referenci
    /// vyřazuje picker přes <c>ExcludeSubgraphId</c>.
    /// </summary>
    public IEnumerable<AssetEntry> Subgraphs() => Entries;

    /// <summary>Assety, ze kterých se smí dělat instance (bývalá role <c>Template</c>).</summary>
    public IEnumerable<AssetEntry> Templates() => Entries.Where(e => e.Instanceable);

    public IReadOnlyList<InstanceEntry> InstanceEntries => _instancesSnapshot;
    public IEnumerable<InstanceEntry> InstancesOf(Guid templateId) =>
        InstanceEntries.Where(i => i.TemplateId == templateId);
    public InstanceEntry? ResolveInstance(Guid id)
    {
        lock (_gate) return _instancesById.TryGetValue(id, out var e) ? e : null;
    }

    /// <summary>Resolvuje asset podle GUID (pro SubgraphNode reference). null = stale/nenalezeno.</summary>
    public AssetEntry? Resolve(Guid id)
    {
        lock (_gate) return _byId.TryGetValue(id, out var e) ? e : null;
    }

    // ── Knihovny (asset roots) ──────────────────────────────

    public void AddRoot(string path)
    {
        var norm = Normalize(path);
        if (string.IsNullOrWhiteSpace(norm)) return;
        if (_roots.Contains(norm, StringComparer.OrdinalIgnoreCase)) return;
        _roots.Add(norm);
        SaveRoots();
        Rescan();
        StartWatcher(norm);
    }

    public void RemoveRoot(string path)
    {
        if (_roots.RemoveAll(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            StopWatcher(path);
            SaveRoots();
            Rescan();
        }
    }

    /// <summary>Přečte všechny knihovny znovu a přestaví index. Voláno jen při startu a AddRoot/RemoveRoot.</summary>
    public void Rescan()
    {
        var byId = new Dictionary<Guid, AssetEntry>();
        var entries = new List<AssetEntry>();
        var idByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var instancesById = new Dictionary<Guid, InstanceEntry>();
        var instances = new List<InstanceEntry>();
        var instIdByPath = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in _roots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.nedgraph.json", SearchOption.AllDirectories))
            {
                var entry = TryRead(file);
                if (entry is null) continue;
                InsertGraphEntry(entry, byId, entries, idByPath);
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.nedinst.json", SearchOption.AllDirectories))
            {
                var inst = TryReadInstance(file);
                if (inst is null) continue;
                if (instancesById.ContainsKey(inst.Id)) continue;
                instancesById[inst.Id] = inst;
                instances.Add(inst);
                instIdByPath[inst.Path] = inst.Id;
            }
        }

        lock (_gate)
        {
            _byId = byId;
            _entries = entries;
            _idByPath = idByPath;
            _instancesById = instancesById;
            _instances = instances;
            _instIdByPath = instIdByPath;
            PublishSnapshotsLocked();
        }

        Changed?.Invoke();
    }

    // ── Inkrementální update (vlastní zápisy editoru + watcher po debounce) ──

    /// <summary>Inkrementálně přeindexuje jeden soubor (po vlastním zápisu editoru, nebo z watcheru).</summary>
    public void UpdateFile(string path)
    {
        if (path.EndsWith(".nedgraph.json", StringComparison.OrdinalIgnoreCase))
            UpdateGraphFile(path);
        else if (path.EndsWith(".nedinst.json", StringComparison.OrdinalIgnoreCase))
            UpdateInstanceFile(path);
        else
            return;

        Changed?.Invoke();
    }

    /// <summary>Odstraní soubor z indexu (po smazání editorem, nebo z watcheru).</summary>
    public void RemoveFile(string path)
    {
        var changed = false;
        lock (_gate)
        {
            if (_idByPath.TryGetValue(path, out var gid))
            {
                _idByPath.Remove(path);
                _byId.Remove(gid);
                _entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
                changed = true;
            }
            if (_instIdByPath.TryGetValue(path, out var iid))
            {
                _instIdByPath.Remove(path);
                _instancesById.Remove(iid);
                _instances.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
                changed = true;
            }
            if (changed) PublishSnapshotsLocked();
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Přesun/rename: odebere starou cestu, přeindexuje novou.</summary>
    public void MoveFile(string oldPath, string newPath)
    {
        RemoveFile(oldPath);
        UpdateFile(newPath);
    }

    private void UpdateGraphFile(string path)
    {
        AssetEntry? entry;
        try { entry = File.Exists(path) ? TryRead(path) : null; }
        catch { entry = null; }

        lock (_gate)
        {
            if (_idByPath.TryGetValue(path, out var oldId))
            {
                _idByPath.Remove(path);
                _byId.Remove(oldId);
                _entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            }

            if (entry is not null)
                InsertGraphEntry(entry, _byId, _entries, _idByPath);

            PublishSnapshotsLocked();
        }
    }

    private void UpdateInstanceFile(string path)
    {
        InstanceEntry? inst;
        try { inst = File.Exists(path) ? TryReadInstance(path) : null; }
        catch { inst = null; }

        lock (_gate)
        {
            if (_instIdByPath.TryGetValue(path, out var oldId))
            {
                _instIdByPath.Remove(path);
                _instancesById.Remove(oldId);
                _instances.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            }

            if (inst is not null && !_instancesById.ContainsKey(inst.Id))
            {
                _instancesById[inst.Id] = inst;
                _instances.Add(inst);
                _instIdByPath[inst.Path] = inst.Id;
            }

            PublishSnapshotsLocked();
        }
    }

    /// <summary>Vloží asset do indexu; při kolizi Id (duplikovaný soubor) opraví novější kopii
    /// novým GUID a upozorní přes <see cref="INedNotifier"/>. Volá se pod _gate, nebo (Rescan)
    /// nad lokálními dictionary bez potřeby zámku.</summary>
    private void InsertGraphEntry(AssetEntry entry, Dictionary<Guid, AssetEntry> byId, List<AssetEntry> entries, Dictionary<string, Guid> idByPath)
    {
        if (byId.TryGetValue(entry.Id, out var existing)
            && !string.Equals(existing.Path, entry.Path, StringComparison.OrdinalIgnoreCase))
        {
            var resolved = ResolveDuplicateId(existing, entry, byId, entries, idByPath);
            if (resolved is null) return; // repair selhal → first-wins, incoming přeskoč
            entry = resolved;
        }

        byId[entry.Id] = entry;
        idByPath[entry.Path] = entry.Id;
        entries.Add(entry);
    }

    /// <summary>Novější ze dvou souborů se stejným Id dostane nový GUID zapsaný do souboru
    /// (reference ostatních assetů dál míří na starší soubor, který si identitu nechá).
    /// Vrací entry, kterou má volající vložit pod (případně novým) Id, nebo null = přeskoč incoming.</summary>
    private AssetEntry? ResolveDuplicateId(AssetEntry existing, AssetEntry incoming,
        Dictionary<Guid, AssetEntry> byId, List<AssetEntry> entries, Dictionary<string, Guid> idByPath)
    {
        var existingTime = SafeLastWrite(existing.Path);
        var incomingTime = SafeLastWrite(incoming.Path);
        var incomingIsNewer = incomingTime >= existingTime;

        var pathToRepair = incomingIsNewer ? incoming.Path : existing.Path;
        var newId = AssetFileService.ReassignId(pathToRepair);
        var fileName = Path.GetFileName(pathToRepair);

        if (newId is null)
        {
            _notifier.Warn("Notice_DuplicateIdUnrepaired", fileName);
            return incomingIsNewer ? null : incoming; // first-wins fallback
        }

        _notifier.Warn("Notice_DuplicateIdRepaired", fileName);

        if (incomingIsNewer)
            return CloneWithId(incoming, newId.Value);

        // Starší (incoming) si nechá původní Id; už zaindexovaný novější soubor přeindexuj pod novým.
        byId.Remove(existing.Id);
        entries.RemoveAll(e => string.Equals(e.Path, existing.Path, StringComparison.OrdinalIgnoreCase));
        var relocated = CloneWithId(existing, newId.Value);
        byId[relocated.Id] = relocated;
        idByPath[relocated.Path] = relocated.Id;
        entries.Add(relocated);
        return incoming;
    }

    private static AssetEntry CloneWithId(AssetEntry e, Guid newId) => new()
    {
        Id = newId,
        Path = e.Path,
        Name = e.Name,
        Flow = e.Flow,
        Instanceable = e.Instanceable,
        Interface = e.Interface,
    };

    private static DateTime SafeLastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private void PublishSnapshotsLocked()
    {
        _entriesSnapshot = _entries.ToList();
        _instancesSnapshot = _instances.ToList();
    }

    private AssetEntry? TryRead(string file)
    {
        try
        {
            var doc = GraphPersistence.Deserialize(File.ReadAllText(file));
            if (doc is null) return null;
            if (!Guid.TryParse(doc.Settings.Id, out var id) || id == Guid.Empty) return null;

            var name = string.IsNullOrWhiteSpace(doc.Settings.Name)
                ? Path.GetFileNameWithoutExtension(file).Replace(".nedgraph", "")
                : doc.Settings.Name!;

            return new AssetEntry
            {
                Id = id,
                Path = file,
                Name = name,
                Flow = ReadFlow(doc),
                Instanceable = doc.Settings.Instanceable,
                Interface = BuildInterface(doc),
            };
        }
        catch
        {
            _notifier.Warn("Notice_AssetReadFailed", Path.GetFileName(file));
            return null; // poškozený / nečitelný soubor index nezboří
        }
    }

    /// <summary>
    /// Odvodí rozhraní subgrafu z jeho souboru: vstupy (parametry) + návratový typ.
    /// Čte se přímo z DTO, bez katalogu — rozhraní je jen jméno, type id a expozice.
    /// </summary>
    private SubgraphInterface BuildInterface(GraphDocument doc)
    {
        var inputs = new List<SubgraphInput>();

        foreach (var n in doc.Nodes.Where(n => n.TypeName == BuiltInIds.GraphInput))
        {
            var typeName = FieldStr(n, BuiltInIds.GraphInputTypeName);
            inputs.Add(new SubgraphInput
            {
                Name = FieldStr(n, BuiltInIds.GraphInputName) ?? "In",
                Type = string.IsNullOrEmpty(typeName) ? TypeIds.Any : typeName!,
                Multiple = FieldBool(n, BuiltInIds.GraphInputMultiple),
                Exposure = Enum.TryParse<InputExposure>(FieldStr(n, BuiltInIds.GraphInputExposure), out var ex)
                    ? ex : InputExposure.Port,
                Default = FieldStr(n, BuiltInIds.GraphInputDefault),
                Order = FieldInt(n, BuiltInIds.GraphInputOrder),
                Description = FieldStr(n, BuiltInIds.GraphInputDescription),
            });
        }

        // Výstupy jsou deklarace v nastavení grafu, ne uzly na plátně — pořadí je pořadí v poli.
        var outputs = (doc.Settings.Outputs ?? new List<GraphOutputDto>())
            .Where(o => !string.IsNullOrWhiteSpace(o.Name))
            .Select((o, index) => new SubgraphOutput
            {
                Id = o.Id ?? "",
                Name = o.Name,
                Type = string.IsNullOrWhiteSpace(o.Type) ? TypeIds.Any : o.Type,
                Multiple = o.Multiple,
                Order = index,
            })
            .ToList();

        return new SubgraphInterface
        {
            Flow = ReadFlow(doc),
            Inputs = inputs.OrderBy(i => i.Order).ToList(),
            Outputs = outputs,
        };
    }

    private static GraphFlow ReadFlow(GraphDocument doc) =>
        Enum.TryParse<GraphFlow>(doc.Settings.Flow, ignoreCase: true, out var flow) ? flow : GraphFlow.Data;

    private InstanceEntry? TryReadInstance(string file)
    {
        try
        {
            var doc = Persistence.InstancePersistence.Deserialize(File.ReadAllText(file));
            if (doc is null) return null;
            if (!Guid.TryParse(doc.Id, out var id) || id == Guid.Empty) return null;
            // Neplatný/prázdný TemplateId instanci NEZAHODÍ (jinak by osiřelá instance zmizela
            // z knihovny) — zůstane s Guid.Empty a zobrazí se jako orphan (Resolve vrátí null).
            Guid.TryParse(doc.TemplateId, out var tid);
            var name = string.IsNullOrWhiteSpace(doc.Name)
                ? Path.GetFileNameWithoutExtension(file).Replace(".nedinst", "")
                : doc.Name;
            return new InstanceEntry { Id = id, Path = file, Name = name, TemplateId = tid };
        }
        catch
        {
            _notifier.Warn("Notice_AssetReadFailed", Path.GetFileName(file));
            return null;
        }
    }

    private static string? FieldStr(GraphNodeDto n, string key)
    {
        if (!n.Fields.TryGetValue(key, out var v) || v is null) return null;
        return v is JsonElement je
            ? (je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString())
            : v.ToString();
    }

    private static int FieldInt(GraphNodeDto n, string key)
    {
        if (!n.Fields.TryGetValue(key, out var v) || v is null) return 0;
        if (v is JsonElement je)
            return je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var i) ? i : 0;
        return int.TryParse(v.ToString(), out var x) ? x : 0;
    }

    private static bool FieldBool(GraphNodeDto n, string key) =>
        bool.TryParse(FieldStr(n, key), out var value) && value;

    private static string Normalize(string p) =>
        string.IsNullOrWhiteSpace(p) ? "" : Path.GetFullPath(p.Trim());

    // ── FileSystemWatcher (externí změny mimo editor) ───────

    private void StartWatcher(string root)
    {
        if (_watchers.ContainsKey(root) || !Directory.Exists(root)) return;

        var w = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            Filter = "*.json",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
        w.Created += (_, e) => QueueChange(e.FullPath);
        w.Changed += (_, e) => QueueChange(e.FullPath);
        w.Deleted += (_, e) => QueueChange(e.FullPath);
        w.Renamed += (_, e) => { QueueChange(e.OldFullPath); QueueChange(e.FullPath); };
        w.EnableRaisingEvents = true;
        _watchers[root] = w;
    }

    private void StopWatcher(string root)
    {
        if (!_watchers.Remove(root, out var w)) return;
        w.EnableRaisingEvents = false;
        w.Dispose();
    }

    private void QueueChange(string path)
    {
        if (_disposed) return;
        if (!path.EndsWith(".nedgraph.json", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".nedinst.json", StringComparison.OrdinalIgnoreCase))
            return;

        _pending[path] = 0;
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_disposed) return;

        var paths = _pending.Keys.ToList();
        foreach (var p in paths) _pending.TryRemove(p, out _);
        if (paths.Count == 0) return;

        foreach (var path in paths)
        {
            if (_disposed) return;
            try
            {
                if (File.Exists(path)) UpdateFileNoEvent(path);
                else RemoveFileNoEvent(path);
            }
            catch
            {
                // Soubor uprostřed zápisu (sharing violation) — příští watcher event (Changed
                // po dokončení zápisu) to dorovná; nic dalšího tu dělat nejde.
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Jako UpdateFile, ale bez vlastního Changed — volající (debounce) hlásí jednou za dávku.</summary>
    private void UpdateFileNoEvent(string path)
    {
        if (path.EndsWith(".nedgraph.json", StringComparison.OrdinalIgnoreCase)) UpdateGraphFile(path);
        else if (path.EndsWith(".nedinst.json", StringComparison.OrdinalIgnoreCase)) UpdateInstanceFile(path);
    }

    private void RemoveFileNoEvent(string path)
    {
        lock (_gate)
        {
            var changed = false;
            if (_idByPath.TryGetValue(path, out var gid))
            {
                _idByPath.Remove(path);
                _byId.Remove(gid);
                _entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
                changed = true;
            }
            if (_instIdByPath.TryGetValue(path, out var iid))
            {
                _instIdByPath.Remove(path);
                _instancesById.Remove(iid);
                _instances.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
                changed = true;
            }
            if (changed) PublishSnapshotsLocked();
        }
    }

    // ── Persistence workspace ───────────────────────────────

    private void LoadRoots()
    {
        foreach (var r in WorkspaceConfig.Load(_configPath).Roots
                     .Select(Normalize).Where(r => r.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            _roots.Add(r);
    }

    /// <summary>Uloží kořeny; seznam manifestů ve workspace zůstane nedotčený.</summary>
    private void SaveRoots()
    {
        var workspace = WorkspaceConfig.Load(_configPath);
        workspace.Roots = _roots.ToList();
        WorkspaceConfig.Save(_configPath, workspace);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer.Dispose();
        foreach (var w in _watchers.Values)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }
}
