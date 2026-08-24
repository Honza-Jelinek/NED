using NED.Abstractions.Manifest;

namespace NED.Core.Tests;

/// <summary>
/// Kolize packů. First-wins zůstává (deterministické), ale nesmí být tiché — dřív
/// dostal uživatel starší verzi typu a nikde se to nedozvěděl.
/// </summary>
public class PackConflictTests
{
    /// <summary>Tentýž pack podruhé se zahodí celý a ohlásí jednou — ne kolizí u každého typu.</summary>
    [Fact]
    public void SamePackId_MergesDistinctTypesAndReportsWarning()
    {
        var catalog = new NedCatalog(new[]
        {
            Pack("dup", ("dup/A", "A")),
            Pack("dup", ("dup/B", "B")),
        });

        var issue = Assert.Single(catalog.Issues);
        Assert.Equal("Notice_PackConflict", issue.MessageKey);
        Assert.Equal(NedNoticeSeverity.Warning, issue.Severity);
        Assert.Equal("dup", issue.Args![0]);

        Assert.Equal(2, catalog.Packs.Count(p => p.Id == "dup"));
        Assert.Equal(2, catalog.AllTypes.Count(t => t.Id.StartsWith("dup/")));
    }

    [Fact]
    public void SamePackId_TypeCollisionIsStillReportedAndFirstWins()
    {
        var catalog = new NedCatalog(new[]
        {
            Pack("dup", ("dup/Thing", "First")),
            Pack("dup", ("dup/Thing", "Second")),
        });

        Assert.Collection(catalog.Issues,
            issue => Assert.Equal("Notice_PackConflict", issue.MessageKey),
            issue => Assert.Equal("Notice_TypeConflict", issue.MessageKey));
        Assert.Equal("First", catalog.Resolve("dup/Thing")!.Name);
    }

    /// <summary>
    /// Pack, který prohlásí typ s cizím prefixem. Vítěz je první načtený a hlásí se to
    /// per typ — na rozdíl od dvojího načtení téhož packu jde o skutečně jinou situaci.
    /// </summary>
    [Fact]
    public void TypeDeclaredByTwoPacks_ReportsWinnerAndLoser()
    {
        var catalog = new NedCatalog(new[]
        {
            Pack("first", ("shared/Thing", "Thing")),
            Pack("second", ("shared/Thing", "Thing")),
        });

        var issue = Assert.Single(catalog.Issues);
        Assert.Equal("Notice_TypeConflict", issue.MessageKey);
        Assert.Equal(new object?[] { "shared/Thing", "first", "second" }, issue.Args);

        Assert.Equal("first", catalog.PackOf("shared/Thing"));
    }

    /// <summary>Dva různé typy se stejným zobrazovaným jménem — paleta je musí umět odlišit.</summary>
    [Fact]
    public void DuplicateDisplayName_IsFlaggedAsAmbiguous()
    {
        var catalog = new NedCatalog(new[]
        {
            Pack("math", ("math/Add", "Add")),
            Pack("vectors", ("vectors/Add", "Add"), ("vectors/Cross", "Cross")),
        });

        Assert.Contains("Add", catalog.AmbiguousNames);
        Assert.DoesNotContain("Cross", catalog.AmbiguousNames);
        Assert.Empty(catalog.Issues);   // stejné jméno není chyba, jen nejednoznačnost
    }

    /// <summary>Bez kolizí se nehlásí nic — hlášení nesmí být šum na pozadí.</summary>
    [Fact]
    public void DistinctPacks_ReportNothing()
    {
        var catalog = new NedCatalog(new[]
        {
            Pack("a", ("a/One", "One")),
            Pack("b", ("b/Two", "Two")),
        });

        Assert.Empty(catalog.Issues);
        Assert.Empty(catalog.AmbiguousNames);
    }

    /// <summary>
    /// Ručně psaný manifest snadno pojmenuje dva výstupy stejně. Model si vezme první;
    /// bez hlášky by druhý port jen visel na uzlu a linky na něj by se při uložení ztratily.
    /// </summary>
    [Fact]
    public void DuplicateOutputName_IsReportedAndFirstWins()
    {
        var catalog = new NedCatalog(new[]
        {
            new NodeManifest
            {
                Pack = new PackInfo { Id = "flow" },
                Types =
                {
                    new NodeTypeDescriptor
                    {
                        Id = "flow/Loop",
                        Name = "Loop",
                        Outputs =
                        {
                            new NodeOutputDescriptor { Name = "Body", Type = TypeIds.Int },
                            new NodeOutputDescriptor { Name = "Body", Type = TypeIds.String },
                        },
                    },
                },
            },
        });

        var issue = Assert.Single(catalog.Issues);
        Assert.Equal("Notice_DuplicateOutputName", issue.MessageKey);
        Assert.Equal(new object?[] { "flow/Loop", "Body" }, issue.Args);

        var node = new DataNodeModel(catalog.Resolve("flow/Loop")!, catalog: catalog);
        var port = Assert.Single(node.Outputs);
        Assert.Equal(TypeIds.Int, port.Value.DataType);      // první vyhrál
        Assert.Single(node.Ports, p => p.Alignment == Blazor.Diagrams.Core.Models.PortAlignment.Right);
    }

    private static NodeManifest Pack(string packId, params (string Id, string Name)[] types) => new()
    {
        Pack = new PackInfo { Id = packId },
        Types = types.Select(t => new NodeTypeDescriptor
        {
            Id = t.Id,
            Name = t.Name,
            Outputs = { new NodeOutputDescriptor { Type = t.Id } },
        }).ToList(),
    };
}
