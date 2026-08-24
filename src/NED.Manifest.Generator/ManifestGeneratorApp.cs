using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using NED.Abstractions.Manifest;

namespace NED.Manifest.Generator;

public static class ManifestGeneratorApp
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            await output.WriteLineAsync(HelpText);
            return args.Length == 0 ? 1 : 0;
        }

        var machineOutput = args.Contains("--json", StringComparer.Ordinal);
        var source = Path.GetFullPath(args[0]);
        if (!File.Exists(source))
            return await FailAsync($"source not found: {source}", machineOutput, output, error);

        try
        {
            var configuration = Option(args, "-c", "--configuration") ?? "Debug";
            var framework = Option(args, "-f", "--framework");
            var project = DotNetProject.IsProject(source)
                ? await DotNetProject.BuildAsync(source, configuration, framework, error, cancellationToken)
                : null;
            var assemblyPath = project?.TargetPath ?? source;
            var outPath = OutputPath(args, source, project);

            using var loadedAssembly = LoadAssembly(assemblyPath);
            var manifest = ManifestBuilder.Build(loadedAssembly.Assembly, out var warnings);
            if (project is not null && manifest.Types.Count == 0)
                return await FailAsync(
                    "project produced no concrete IGraphData node types; verify its NED.Abstractions reference",
                    machineOutput, output, error);

            var response = new ManifestGenerationResponse
            {
                Success = true,
                ManifestPath = outPath,
                Pack = manifest.Pack,
                TypeCount = manifest.Types.Count,
                EnumCount = manifest.Enums.Count,
                Diagnostics = warnings.Select(w => new ManifestGenerationDiagnostic
                {
                    Severity = ManifestGenerationSeverity.Warning,
                    Source = w.TypeName,
                    Message = w.Reason,
                }).ToList(),
            };

            foreach (var warning in response.Diagnostics)
                await error.WriteLineAsync($"ned-manifest: skipped {warning.Source} — {warning.Message}");

            AtomicFile.WriteAllText(outPath, ManifestJson.Write(manifest));
            if (machineOutput)
                await output.WriteLineAsync(JsonSerializer.Serialize(response, ManifestJson.Options));
            else
                await output.WriteLineAsync(
                    $"ned-manifest: {manifest.Types.Count} types, {manifest.Enums.Count} enums → {outPath}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            return await FailAsync("generation was cancelled", machineOutput, output, error);
        }
        catch (Exception ex)
        {
            return await FailAsync(ex.Message, machineOutput, output, error);
        }
    }

    private static LoadedAssembly LoadAssembly(string assemblyPath)
    {
        if (!assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"unsupported source: {assemblyPath}");
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException("built assembly was not found", assemblyPath);

        var context = new ManifestAssemblyLoadContext(assemblyPath);
        try
        {
            return new LoadedAssembly(context, context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath)));
        }
        catch (Exception ex)
        {
            context.Unload();
            throw new InvalidOperationException($"cannot load {assemblyPath}: {ex.Message}", ex);
        }
    }

    private sealed class LoadedAssembly(ManifestAssemblyLoadContext context, Assembly assembly) : IDisposable
    {
        public Assembly Assembly { get; } = assembly;
        public void Dispose() => context.Unload();
    }

    private sealed class ManifestAssemblyLoadContext(string mainAssemblyPath)
        : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var abstractions = typeof(NodeManifest).Assembly;
            if (AssemblyName.ReferenceMatchesDefinition(assemblyName, abstractions.GetName()))
                return abstractions;

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? 0 : LoadUnmanagedDllFromPath(path);
        }
    }

    private static string OutputPath(string[] args, string source, DotNetProjectInfo? project)
    {
        var configured = Option(args, "-o", "--output");
        if (configured is not null) return Path.GetFullPath(configured);
        if (project is not null)
            return Path.Combine(Path.GetDirectoryName(source)!, project.AssemblyName + ManifestFile.Extension);
        return Path.ChangeExtension(source, null) + ManifestFile.Extension;
    }

    private static string? Option(string[] args, string shortName, string longName)
    {
        var index = Array.FindIndex(args, value => value == shortName || value == longName);
        if (index < 0) return null;
        if (index + 1 >= args.Length)
            throw new ArgumentException($"missing value for {args[index]}");
        return args[index + 1];
    }

    private static async Task<int> FailAsync(
        string message,
        bool machineOutput,
        TextWriter output,
        TextWriter error)
    {
        if (machineOutput)
        {
            var response = new ManifestGenerationResponse { Success = false, Error = message };
            await output.WriteLineAsync(JsonSerializer.Serialize(response, ManifestJson.Options));
        }
        await error.WriteLineAsync($"ned-manifest: {message}");
        return 1;
    }

    private const string HelpText = """
        ned-manifest — generates a NED node manifest from an annotated .NET assembly or project.

          ned-manifest <assembly.dll> [-o <out.json>] [--json]
          ned-manifest <project.csproj|fsproj|vbproj> [-c Debug] [-f <tfm>] [-o <out.json>] [--json]

        A project is built first. Without -o, its manifest is written next to the project.
        --json writes a machine-readable ManifestGenerationResponse to stdout.
        """;
}
