using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NED.Core.Manifest;

namespace NED.Core.Tests;

/// <summary>
/// Ověřuje cestu, kterou jde skutečný hostitel: <c>AddNed</c> + manifesty ze souborů.
/// Build sám o sobě neřekne, jestli se manifesty za běhu opravdu načtou.
/// </summary>
public class CompositionTests
{
    [Fact]
    public void AddNed_LoadsPacksFromManifestFiles()
    {
        var services = new ServiceCollection();
        services.AddNed(o => o
            .Manifest(RepoFile("src/Sandbox/Sandbox.nodes.json")));

        var catalog = services.BuildServiceProvider().GetRequiredService<NedCatalog>();

        var packs = catalog.Packs.Select(p => p.Id).ToList();
        Assert.Contains(BuiltInIds.Pack, packs);   // vestavěné uzly jsou vždy
        Assert.Contains("sandbox", packs);

        // Bez Output uzlu nejde postavit graf, bez Add uzlu by ukázkový pack byl k ničemu.
        Assert.NotNull(catalog.Resolve(BuiltInIds.Output));
        Assert.NotNull(catalog.Resolve("sandbox/Add"));
    }

    /// <summary>Polymorfie uvnitř packu respektuje vztah odvozeného a základního typu.</summary>
    [Fact]
    public void Catalog_AllowsSubtypeIntoBaseTypePort()
    {
        var manifest = NED.Manifest.Generator.ManifestBuilder.Build(typeof(Fixtures.BaseNode).Assembly, out _);
        var catalog = new NedCatalog([manifest]);
        var derived = manifest.Types.Single(type => type.Id.EndsWith("/DerivedNode", StringComparison.Ordinal));
        var baseType = manifest.Types.Single(type => type.Id.EndsWith("/BaseNode", StringComparison.Ordinal));

        Assert.True(catalog.IsCompatible(derived.Id, baseType.Id));
        Assert.False(catalog.IsCompatible(baseType.Id, derived.Id));
    }

    /// <summary>Editor musí jít složit i bez jediného doménového packu.</summary>
    [Fact]
    public void AddNed_WorksWithNoPacks()
    {
        var services = new ServiceCollection();
        services.AddNed(_ => { });

        var catalog = services.BuildServiceProvider().GetRequiredService<NedCatalog>();

        Assert.NotNull(catalog.Resolve(BuiltInIds.Output));
        Assert.NotNull(catalog.Resolve(BuiltInIds.GraphInput));
    }

    private static string RepoFile(string relative, [CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, "..", "..", relative));
}
