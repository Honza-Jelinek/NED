using System.Reflection;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;
using NED.Manifest.Generator;

namespace NED.Core.Tests;

/// <summary>
/// Inline editor pole. Razor se tady nerenderuje, testuje se rozhodovani PRED nim —
/// tam obe chyby byly.
/// </summary>
public sealed class FieldEditorTests
{
    /// <summary>
    /// Enum vstup vypada jako doménový typ (neni skalar), takze <c>Complex</c> je true.
    /// Widget proto musi dat prednost seznamu hodnot — jinak nabidne producenty
    /// <c>pack/MujEnum</c>, zadne nenajde a dropdown vyjde prazdny.
    /// </summary>
    [Fact]
    public void EnumInput_IsComplexButHasValues_SoEnumBranchMustWin()
    {
        var catalog = TestGraph.Catalog();
        var node = new DataNodeModel(catalog.Resolve(BuiltInIds.GraphInput)!, catalog: catalog);

        var exposure = Assert.Single(node.InputDefs, input => input.DataType == "ned/InputExposure");
        var values = catalog.EnumValues(exposure.DataType);

        Assert.True(exposure.Complex);
        Assert.NotNull(values);
        Assert.Contains("Port", values!);

        // Presne to pravidlo, ktere widget pouziva: enum vyhrava nad komplexnim dropdownem.
        Assert.False(ShowComplex(isComplex: exposure.Complex, enumValues: values));
        Assert.True(ShowComplex(isComplex: true, enumValues: null));
    }

    /// <summary>Vestavěný <c>Exposure</c> na Input uzlu je taky enum — nesmi vypadnout.</summary>
    [Fact]
    public void BuiltInExposureEnum_HasValues()
    {
        var catalog = TestGraph.Catalog();
        var input = new DataNodeModel(catalog.Resolve(BuiltInIds.GraphInput)!, catalog: catalog)
            .InputDefs.Single(item => item.Name == BuiltInIds.GraphInputExposure);

        var values = catalog.EnumValues(input.DataType);

        Assert.NotNull(values);
        Assert.Equal(["Port", "Field"], values!);
        Assert.False(ShowComplex(input.Complex, values));
    }

    /// <summary>
    /// Neznamy parametr komponenty projde buildem a spadne az za behu (Blazor ho nema kam
    /// dat). Prave takhle se <c>FieldType=</c> misto <c>TypeId=</c> dostalo do
    /// SubgraphNodeWidgetu a rozbilo kazdy subgraf s field vstupem.
    /// </summary>
    [Fact]
    public void EveryNodeFieldInputUsage_NamesDeclaredParameters()
    {
        var declared = typeof(NodeFieldInput)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "NED.Core"), "*.razor", SearchOption.AllDirectories))
        {
            var markup = File.ReadAllText(file);
            foreach (Match usage in Regex.Matches(markup, @"<NodeFieldInput\b(?<attrs>[^>]*)/?>"))
            foreach (Match attribute in Regex.Matches(usage.Groups["attrs"].Value, @"(?<!@)\b(?<name>[A-Z]\w*)="))
                Assert.True(declared.Contains(attribute.Groups["name"].Value),
                    $"{Path.GetFileName(file)}: NodeFieldInput nezná parametr "
                    + $"'{attribute.Groups["name"].Value}' — spadne to až za běhu.");
        }
    }

    /// <summary>Kopie pravidla z <c>NodeFieldInput.ShowComplex</c>.</summary>
    private static bool ShowComplex(bool isComplex, IReadOnlyList<string>? enumValues) =>
        isComplex && enumValues is null;

    private static string RepoRoot([CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, "..", ".."));
}
