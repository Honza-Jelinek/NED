using System.Diagnostics;
using System.Text.Json;

namespace NED.Manifest.Generator;

public sealed record DotNetProjectInfo(string TargetPath, string AssemblyName, string TargetFramework);

public static class DotNetProject
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".fsproj", ".vbproj",
    };

    public static bool IsProject(string path) => Extensions.Contains(Path.GetExtension(path));

    public static async Task<DotNetProjectInfo> BuildAsync(
        string projectPath,
        string configuration,
        string? requestedFramework,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        var initial = await QueryAsync(projectPath, configuration, null, log, cancellationToken);
        var frameworks = Frameworks(initial);
        var framework = ChooseFramework(frameworks, requestedFramework);

        var buildArgs = new List<string>
        {
            "build", projectPath, "-c", configuration, "--nologo",
        };
        if (!string.IsNullOrWhiteSpace(framework))
        {
            buildArgs.Add("-f");
            buildArgs.Add(framework);
        }

        var build = await RunDotNetAsync(buildArgs, cancellationToken);
        await WriteLogAsync(log, build);
        if (build.ExitCode != 0)
            throw new InvalidOperationException($"dotnet build failed ({build.ExitCode})");

        var evaluated = await QueryAsync(projectPath, configuration, framework, log, cancellationToken);
        var targetPath = evaluated.Properties.TargetPath;
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new InvalidOperationException("MSBuild did not return TargetPath");
        if (!Path.IsPathRooted(targetPath))
            targetPath = Path.GetFullPath(targetPath, Path.GetDirectoryName(projectPath)!);

        return new DotNetProjectInfo(
            targetPath,
            evaluated.Properties.AssemblyName,
            framework ?? evaluated.Properties.TargetFramework);
    }

    private static string? ChooseFramework(IReadOnlyList<string> frameworks, string? requested)
    {
        if (requested is not null)
        {
            if (frameworks.Count > 0 && !frameworks.Contains(requested, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"target framework '{requested}' is not available; choose: {string.Join(", ", frameworks)}");
            return requested;
        }

        return frameworks.Count switch
        {
            0 => null,
            1 => frameworks[0],
            _ => throw new InvalidOperationException(
                $"project targets multiple frameworks; use --framework with one of: {string.Join(", ", frameworks)}"),
        };
    }

    private static IReadOnlyList<string> Frameworks(MsBuildQuery query)
    {
        var value = string.IsNullOrWhiteSpace(query.Properties.TargetFrameworks)
            ? query.Properties.TargetFramework
            : query.Properties.TargetFrameworks;
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static async Task<MsBuildQuery> QueryAsync(
        string projectPath,
        string configuration,
        string? framework,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "msbuild", projectPath,
            "-getProperty:TargetPath",
            "-getProperty:TargetFramework",
            "-getProperty:TargetFrameworks",
            "-getProperty:AssemblyName",
            $"-property:Configuration={configuration}",
            "-nologo",
        };
        if (!string.IsNullOrWhiteSpace(framework))
            args.Add($"-property:TargetFramework={framework}");

        var result = await RunDotNetAsync(args, cancellationToken);
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            await log.WriteLineAsync(result.StandardError);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"MSBuild project evaluation failed ({result.ExitCode})");

        return JsonSerializer.Deserialize<MsBuildQuery>(result.StandardOutput)
            ?? throw new InvalidOperationException("MSBuild returned an unreadable project description");
    }

    private static async Task<ProcessResult> RunDotNetAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

        if (!process.Start())
            throw new InvalidOperationException("dotnet process could not be started");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }

        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static async Task WriteLogAsync(TextWriter log, ProcessResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            await log.WriteLineAsync(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            await log.WriteLineAsync(result.StandardError);
    }

    private sealed class MsBuildQuery
    {
        public MsBuildProperties Properties { get; set; } = new();
    }

    private sealed class MsBuildProperties
    {
        public string TargetPath { get; set; } = "";
        public string TargetFramework { get; set; } = "";
        public string TargetFrameworks { get; set; } = "";
        public string AssemblyName { get; set; } = "";
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
