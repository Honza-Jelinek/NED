using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NED.Abstractions.Manifest;
using NED.Core.Assets;
using NED.Core.Manifest;

namespace NED.Core.Tests;

/// <summary>
/// Načítání node packů. Poškozený pack nesmí shodit start editoru — ale nesmí ani zmizet
/// beze stopy, jinak uživatel vidí placeholdery a nedozví se proč.
/// </summary>
public class PackLoadingTests
{
    [Fact]
    public void ManifestFolder_LoadsEveryManifestInIt()
    {
        using var dir = new TempDir();
        dir.Copy("src/Sandbox/Sandbox.nodes.json");
        dir.Write("notes.txt", "tohle není manifest");   // jiná přípona se ignoruje

        var catalog = CatalogFrom(o => o.ManifestFolder(dir.Path));

        Assert.Contains("sandbox", catalog.Packs.Select(p => p.Id));
        Assert.Empty(catalog.Issues);
    }

    /// <summary>Aplikace bez vlastních packů je legitimní stav, ne chyba.</summary>
    [Fact]
    public void ManifestFolder_MissingDirectoryIsNotAnError()
    {
        var catalog = CatalogFrom(o => o.ManifestFolder(Path.Combine(Path.GetTempPath(), "ned-nope", Guid.NewGuid().ToString("N"))));

        Assert.Empty(catalog.Issues);
        Assert.NotNull(catalog.Resolve(Manifest.BuiltInIds.Output));
    }

    [Fact]
    public void ManifestFolder_WorkspaceCanDisableDiscoveredPack()
    {
        using var dir = new TempDir();
        dir.Copy("src/Sandbox/Sandbox.nodes.json");
        var secondPack = File.ReadAllText(RepoFile("src/Sandbox/Sandbox.nodes.json"))
            .Replace("\"Id\": \"sandbox\"", "\"Id\": \"sample.second\"", StringComparison.Ordinal);
        dir.Write("Sample.Second.nodes.json", secondPack);
        var sandboxPath = Path.Combine(dir.Path, "Sandbox.nodes.json");
        var workspacePath = Path.Combine(dir.Path, "workspace.json");
        WorkspaceConfig.Save(workspacePath, new Workspace
        {
            Packs =
            {
                new WorkspacePack { Path = sandboxPath, Enabled = false },
            },
        });
        var originalWorkspace = File.ReadAllText(workspacePath);

        // Stejné pořadí jako ve WPF shellu: folder se registruje dřív, než je známá cesta configu.
        var catalog = CatalogFrom(options => options
            .ManifestFolder(dir.Path)
            .LibraryConfig(workspacePath));

        Assert.DoesNotContain("sandbox", catalog.Packs.Select(pack => pack.Id));
        Assert.Contains("sample.second", catalog.Packs.Select(pack => pack.Id));
        Assert.Equal(originalWorkspace, File.ReadAllText(workspacePath));
    }

    [Fact]
    public void CorruptManifest_IsReportedAndDoesNotThrow()
    {
        using var dir = new TempDir();
        dir.Write("broken" + ManifestFile.Extension, "{ tohle není platný JSON");

        var catalog = CatalogFrom(o => o.ManifestFolder(dir.Path));

        var issue = Assert.Single(catalog.Issues);
        Assert.Equal("Notice_PackLoadFailed", issue.MessageKey);
        Assert.Contains("broken", issue.Args![0]!.ToString());

        // Editor je dál použitelný — vestavěné uzly nikam nezmizely.
        Assert.NotNull(catalog.Resolve(Manifest.BuiltInIds.Output));
    }

    [Fact]
    public void MissingManifestFile_IsReported()
    {
        var catalog = CatalogFrom(o => o.Manifest(Path.Combine(Path.GetTempPath(), "chybi" + ManifestFile.Extension)));

        var issue = Assert.Single(catalog.Issues);
        Assert.Equal("Notice_PackLoadFailed", issue.MessageKey);
    }

    [Fact]
    public void NewerManifestVersion_IsLoadedWithWarning()
    {
        using var dir = new TempDir();
        var source = File.ReadAllText(RepoFile("src/Sandbox/Sandbox.nodes.json"));
        dir.Write("future.nodes.json", source.Replace("\"ManifestVersion\": 1", "\"ManifestVersion\": 99"));

        var catalog = CatalogFrom(options => options.ManifestFolder(dir.Path));

        Assert.Contains("sandbox", catalog.Packs.Select(pack => pack.Id));
        var issue = Assert.Single(catalog.Issues);
        Assert.Equal("Notice_ManifestVersionNewer", issue.MessageKey);
        Assert.Equal(99, issue.Args![1]);
    }

    [Fact]
    public void ManifestStore_ReloadsChangedManifest()
    {
        using var dir = new TempDir();
        var manifestPath = dir.Copy("src/Sandbox/Sandbox.nodes.json");
        var services = new ServiceCollection();
        services.AddNed(options => options.Manifest(manifestPath));
        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<NedCatalog>();
        var store = provider.GetRequiredService<ManifestStore>();
        var changedJson = File.ReadAllText(manifestPath)
            .Replace("\"sandbox/Add\"", "\"sandbox/AddReloaded\"", StringComparison.Ordinal);
        File.WriteAllText(manifestPath, changedJson);

        store.ReloadNow();

        Assert.Null(catalog.Resolve("sandbox/Add"));
        Assert.NotNull(catalog.Resolve("sandbox/AddReloaded"));
    }

    private static NedCatalog CatalogFrom(Action<NedOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddNed(configure);
        return services.BuildServiceProvider().GetRequiredService<NedCatalog>();
    }

    private static string RepoFile(string relative, [CallerFilePath] string callerPath = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(callerPath)!, "..", "..", relative));

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ned-tests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Write(string name, string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, name), content);

        public string Copy(string repoRelative, [CallerFilePath] string callerPath = "")
        {
            var source = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(callerPath)!, "..", "..", repoRelative));
            var destination = System.IO.Path.Combine(Path, System.IO.Path.GetFileName(source));
            File.Copy(source, destination);
            return destination;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Temporary pack directory cleanup failed: {ex.Message}");
            }
        }
    }
}
