using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NED.Abstractions.Manifest;
using NED.Core.NodePacks;

namespace NED.Shell.Wpf.NodePacks;

public sealed class DotNetNodePackGeneratorProvider : INodePackGeneratorProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public NodePackGeneratorDescriptor Descriptor { get; } = new()
    {
        Id = "dotnet",
        DisplayName = ".NET project",
        SourceKind = NodePackSourceKind.File,
        SourcePatterns = ["*.csproj", "*.fsproj", "*.vbproj"],
        KnownOptions = ["Configuration", "Framework"],
        SourceFilters =
        [
            new NodePackSourceFilter
            {
                DisplayName = ".NET projects",
                Patterns = ["*.csproj", "*.fsproj", "*.vbproj"],
            },
            new NodePackSourceFilter { DisplayName = "C# projects", Patterns = ["*.csproj"] },
            new NodePackSourceFilter { DisplayName = "F# projects", Patterns = ["*.fsproj"] },
            new NodePackSourceFilter { DisplayName = "Visual Basic projects", Patterns = ["*.vbproj"] },
        ],
    };

    public async Task<ManifestGenerationResponse> GenerateAsync(
        NodePackGenerationRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var tool = Path.Combine(AppContext.BaseDirectory, "tools", "ned-manifest", "ned-manifest.exe");
        if (!File.Exists(tool)) return Failure($"ned-manifest tool was not found: {tool}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = tool,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add(request.Source);
        process.StartInfo.ArgumentList.Add("--json");
        process.StartInfo.ArgumentList.Add("--configuration");
        process.StartInfo.ArgumentList.Add(Option(request, "Configuration") ?? "Debug");
        if (Option(request, "Framework") is { } framework)
        {
            process.StartInfo.ArgumentList.Add("--framework");
            process.StartInfo.ArgumentList.Add(framework);
        }
        if (!string.IsNullOrWhiteSpace(request.OutputPath))
        {
            process.StartInfo.ArgumentList.Add("--output");
            process.StartInfo.ArgumentList.Add(request.OutputPath);
        }

        try
        {
            if (!process.Start()) return Failure("ned-manifest process could not be started");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = PumpLogAsync(process.StandardError, progress, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await stderr;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }

        var responseJson = await stdout;
        ManifestGenerationResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ManifestGenerationResponse>(responseJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Failure($"ned-manifest returned invalid JSON: {ex.Message}");
        }

        if (response is null) return Failure("ned-manifest returned no result");
        response = ManifestGenerationProtocol.Validate(response);
        foreach (var key in Descriptor.UnknownOptions(request.Options))
        {
            response.Diagnostics.Add(new ManifestGenerationDiagnostic
            {
                Severity = ManifestGenerationSeverity.Warning,
                Message = $"unknown generation option '{key}' was ignored",
            });
        }
        if (process.ExitCode != 0 && response.Success)
        {
            response.Success = false;
            response.Error = $"ned-manifest failed with exit code {process.ExitCode}";
        }
        return response;
    }

    private static string? Option(NodePackGenerationRequest request, string name) =>
        request.Options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static async Task<string> PumpLogAsync(
        StreamReader reader,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var complete = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            complete.AppendLine(line);
            if (!string.IsNullOrWhiteSpace(line)) progress?.Report(line);
        }
        return complete.ToString();
    }

    private static ManifestGenerationResponse Failure(string error) => new()
    {
        Success = false,
        Error = error,
    };
}
