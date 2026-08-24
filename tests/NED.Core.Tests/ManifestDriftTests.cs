using System.Reflection;
using System.Runtime.CompilerServices;
using NED.Abstractions.Manifest;
using NED.Manifest.Generator;

namespace NED.Core.Tests;

/// <summary>
/// Manifesty v repu se nesmí rozejít s anotovanými typy. Generace není zapojená do buildu
/// (viz generate-manifests.ps1), takže tenhle test je jediná pojistka — přidáš property
/// a zapomeneš přegenerovat, test spadne.
/// </summary>
public class ManifestDriftTests
{
    [Theory]
    [InlineData("src/Sandbox/Sandbox.nodes.json", typeof(Sandbox.Add))]
    [InlineData("src/NED.Core/Manifest/ned.builtin.nodes.json", typeof(OutputNode))]
    public void CommittedManifest_MatchesAnnotatedTypes(string relativePath, Type typeFromPack)
    {
        var committed = File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");
        var regenerated = ManifestJson.Write(ManifestBuilder.Build(typeFromPack.Assembly, out _)).Replace("\r\n", "\n");

        Assert.True(committed == regenerated,
            $"{relativePath} je zastaralý — spusť .\\generate-manifests.ps1 a commitni výsledek.");
    }

    /// <summary>Polymorfie portů stojí na Extends.</summary>
    [Fact]
    public void Manifest_CarriesInheritance()
    {
        var manifest = ManifestBuilder.Build(typeof(Fixtures.BaseNode).Assembly, out _);

        var child = Assert.Single(manifest.Types, t => t.Id.EndsWith("/DerivedNode", StringComparison.Ordinal));
        Assert.Contains(child.Extends, id => id.EndsWith("/BaseNode", StringComparison.Ordinal));
    }

    /// <summary>Enum bez seznamu hodnot by nešel vykreslit jako dropdown.</summary>
    [Fact]
    public void Manifest_CarriesEnumValues()
    {
        var manifest = ManifestBuilder.Build(typeof(GraphInputNode).Assembly, out _);

        var exposure = Assert.Single(manifest.Enums, e => e.Id == "ned/InputExposure");
        Assert.Contains("Port", exposure.Values);
    }

    /// <summary>Typ bez bezparametrického konstruktoru se ohlásí při generování, ne až pádem editoru.</summary>
    [Fact]
    public void TypeWithoutParameterlessCtor_IsReportedNotSilentlyDropped()
    {
        ManifestBuilder.Build(typeof(Fixtures.NoCtorNode).Assembly, out var warnings);

        Assert.Contains(warnings, w => w.TypeName!.Contains(nameof(Fixtures.NoCtorNode)));
    }

    private static string RepoRoot([CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, "..", ".."));
}
