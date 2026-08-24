namespace NED.Core.Manifest;

/// <summary>Sleduje manifestové soubory a publikuje nový atomický snapshot katalogu.</summary>
public sealed class ManifestStore : IDisposable
{
    private const int DebounceMs = 400;
    private readonly NedOptions _options;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _reloadTimer;
    private IReadOnlyList<NedNotice> _watchIssues = Array.Empty<NedNotice>();
    private NedCatalog? _catalog;
    private bool _disposed;

    public ManifestStore(NedOptions options)
    {
        _options = options;
        _reloadTimer = new Timer(_ => ReloadNow(), null, Timeout.Infinite, Timeout.Infinite);
        _options.PackFilesChanged += OnPackFilesChanged;
    }

    internal void Start(NedCatalog catalog)
    {
        if (_catalog is not null) return;
        _catalog = catalog;
        RebuildWatchers();
        if (_watchIssues.Count > 0) ReloadNow();
    }

    internal void ReloadNow()
    {
        if (_disposed || _catalog is null) return;
        var manifests = _options.ReloadFileManifests(out var issues);
        _catalog.Reload(_options.Manifests.Concat(manifests), issues.Concat(_watchIssues));
    }

    private void OnPackFilesChanged()
    {
        RebuildWatchers();
        QueueReload();
    }

    private void RebuildWatchers()
    {
        foreach (var watcher in _watchers.Values) watcher.Dispose();
        _watchers.Clear();
        var issues = new List<NedNotice>();

        foreach (var path in _options.PackFilePaths())
        {
            var directory = Path.GetDirectoryName(path);
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) || !Directory.Exists(directory))
                continue;

            try
            {
                var watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                watcher.Created += OnFileChanged;
                watcher.Changed += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += OnFileChanged;
                _watchers[path] = watcher;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new NedNotice(NedNoticeSeverity.Warning, "Notice_PackWatchFailed",
                    new object?[] { fileName, ex.Message }));
            }
        }

        _watchIssues = issues;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) => QueueReload();

    private void QueueReload()
    {
        if (!_disposed) _reloadTimer.Change(DebounceMs, Timeout.Infinite);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _options.PackFilesChanged -= OnPackFilesChanged;
        _reloadTimer.Dispose();
        foreach (var watcher in _watchers.Values) watcher.Dispose();
        _watchers.Clear();
    }
}
