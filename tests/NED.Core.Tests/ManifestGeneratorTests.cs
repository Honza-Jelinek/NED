using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NED.Abstractions.Manifest;
using NED.Manifest.Generator;
using NED.Core.NodePacks;
using NED.Abstractions;

namespace NED.Core.Tests;

public class ManifestGeneratorTests
{
    [Theory]
    [InlineData("nodes.csproj")]
    [InlineData("nodes.fsproj")]
    [InlineData("nodes.vbproj")]
    public void DotNetProvider_RecognizesSupportedProjectLanguages(string fileName)
    {
        Assert.True(DotNetProject.IsProject(fileName));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Generator_BuildsProjectAndProducesMachineReadableManifest()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(), "ned-tests", Guid.NewGuid().ToString("N"));
        var manifestPath = Path.Combine(outputDirectory, "sandbox.nodes.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ManifestGeneratorApp.RunAsync(
            [RepoFile("src/Sandbox/Sandbox.csproj"), "--json", "--output", manifestPath],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var response = JsonSerializer.Deserialize<ManifestGenerationResponse>(
            output.ToString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(response);
        Assert.True(response.Success, response.Error);
        Assert.Equal(1, response.ProtocolVersion);
        Assert.Equal("sandbox", response.Pack?.Id);
        Assert.True(response.TypeCount > 0);

        var manifest = NED.Core.Manifest.ManifestJson.ReadFile(manifestPath);
        Assert.NotNull(manifest);
        Assert.Equal("sandbox", manifest.Pack.Id);
        Assert.Equal(response.TypeCount, manifest.Types.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Generator_TwoRunsUseTheirOwnProjectDependencies()
    {
        var sandbox = await Generate(RepoFile("src/Sandbox/Sandbox.csproj"));
        var builtIn = await Generate(RepoFile("src/NED.Core/NED.Core.csproj"));

        Assert.Equal("sandbox", sandbox.Pack?.Id);
        Assert.Equal("ned", builtIn.Pack?.Id);
        Assert.NotEqual(sandbox.Pack?.Id, builtIn.Pack?.Id);
    }

    [Fact]
    public void UnknownGenerationProtocol_IsRejected()
    {
        var response = ManifestGenerationProtocol.Validate(new ManifestGenerationResponse
        {
            ProtocolVersion = 99,
            Success = true,
        });

        Assert.False(response.Success);
        Assert.Contains("99", response.Error);
    }

    [Fact]
    public void UnknownGeneratorOption_IsReportedByDescriptor()
    {
        var descriptor = new NodePackGeneratorDescriptor
        {
            Id = "test",
            DisplayName = "Test",
            KnownOptions = ["Configuration"],
        };

        var unknown = descriptor.UnknownOptions(new Dictionary<string, string>
        {
            ["Configuration"] = "Debug",
            ["Configuraton"] = "Release",
        });

        Assert.Equal("Configuraton", Assert.Single(unknown));
    }

    [Fact]
    public void MultipleNodeOutputAttributes_AreGeneratedWithStableNames()
    {
        var manifest = ManifestBuilder.Build(typeof(MultipleOutputTestNode).Assembly, out _);
        var descriptor = manifest.Types.Single(type => type.Name == nameof(MultipleOutputTestNode));

        Assert.Collection(descriptor.Outputs,
            output =>
            {
                Assert.Equal("Index", output.Name);
                Assert.Equal(TypeIds.Int, output.Type);
                Assert.True(output.Multiple);
            },
            output =>
            {
                Assert.Equal("Completed", output.Name);
                Assert.Equal(descriptor.Id, output.Type);
                Assert.False(output.Multiple);
            });
    }

    [Fact]
    public void EmptyStringDefault_IsExplicitAndDisplayedUnambiguously()
    {
        var manifest = ManifestBuilder.Build(typeof(EmptyStringDefaultTestNode).Assembly, out _);
        var descriptor = manifest.Types.Single(type => type.Name == nameof(EmptyStringDefaultTestNode));
        var inputDescriptor = Assert.Single(descriptor.Inputs);

        Assert.True(inputDescriptor.HasExplicitDefault);
        Assert.Equal(string.Empty, inputDescriptor.Default);

        var node = new DataNodeModel(descriptor);
        var input = Assert.Single(node.InputDefs);
        Assert.Equal("Seller: string  •  default: \"\"", input.Port?.TypeLine);
    }

    [Fact]
    public void Stateful_RemainsClrOnlyMetadata()
    {
        var statefulInfo = typeof(StatefulTestNode).GetCustomAttribute<NodeInfoAttribute>();

        Assert.NotNull(statefulInfo);
        Assert.True(statefulInfo.Stateful);
        Assert.False(new NodeInfoAttribute("Stateless").Stateful);

        var manifest = ManifestBuilder.Build(typeof(StatefulTestNode).Assembly, out _);
        var json = ManifestJson.Write(manifest);

        Assert.DoesNotContain("\"Stateful\":", json);
    }

    private static async Task<ManifestGenerationResponse> Generate(string project)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ned-tests", Guid.NewGuid().ToString("N"));
        var manifestPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(project) + ".nodes.json");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await ManifestGeneratorApp.RunAsync(
            [project, "--json", "--output", manifestPath],
            output,
            error,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, exitCode);
        return JsonSerializer.Deserialize<ManifestGenerationResponse>(output.ToString())!;
    }

    private static string RepoFile(string relative, [CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, "..", "..", relative));
}

[NodeOutput("Index", typeof(int), Multiple = true)]
[NodeOutput("Completed")]
public sealed class MultipleOutputTestNode : IGraphData;

public sealed class EmptyStringDefaultTestNode : IGraphData
{
    [NodePort("Seller")]
    public string Seller { get; set; } = string.Empty;
}

[NodeInfo("State Test", Stateful = true)]
public sealed class StatefulTestNode : IGraphData;
