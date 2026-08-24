using NED.Abstractions.Manifest;

namespace NED.Core.NodePacks;

/// <summary>Typ zdroje, který provider umí přijmout od hostitelského file pickeru.</summary>
public enum NodePackSourceKind
{
    File,
    Directory,
}

/// <summary>Popis generátoru zobrazovaný v UI. Provider id je stabilní hodnota ukládaná do workspace.</summary>
public sealed class NodePackGeneratorDescriptor
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public NodePackSourceKind SourceKind { get; init; }

    /// <summary>
    /// Pojmenované filtry pro nativní file picker. První může být souhrnný, další užší
    /// (např. jeden pro každý projektový jazyk). Hostitel, který je neumí, může dál použít
    /// pouze <see cref="SourcePatterns"/>.
    /// </summary>
    public IReadOnlyList<NodePackSourceFilter> SourceFilters { get; init; } =
        Array.Empty<NodePackSourceFilter>();

    /// <summary>Zpětně kompatibilní souhrnný seznam podporovaných masek zdroje.</summary>
    public IReadOnlyList<string> SourcePatterns { get; init; } = Array.Empty<string>();

    /// <summary>Klíče options, kterým provider rozumí. Slouží k odhalení překlepů v uloženém receptu.</summary>
    public IReadOnlyList<string> KnownOptions { get; init; } = Array.Empty<string>();

    public IEnumerable<string> UnknownOptions(IReadOnlyDictionary<string, string> options) =>
        options.Keys.Where(key => !KnownOptions.Contains(key, StringComparer.OrdinalIgnoreCase));
}

public sealed class NodePackSourceFilter
{
    public required string DisplayName { get; init; }
    public IReadOnlyList<string> Patterns { get; init; } = Array.Empty<string>();
}

/// <summary>Obecný požadavek na generaci; význam options určuje konkrétní provider.</summary>
public sealed class NodePackGenerationRequest
{
    public required string Source { get; init; }
    public string? OutputPath { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Rozšíření editoru pro jeden build systém nebo jazyk. Implementace musí spouštět cizí
/// build/reflexi mimo proces editoru a vrátit pouze přenosný manifestový protokol.
/// </summary>
public interface INodePackGeneratorProvider
{
    NodePackGeneratorDescriptor Descriptor { get; }

    Task<ManifestGenerationResponse> GenerateAsync(
        NodePackGenerationRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}

public static class ManifestGenerationProtocol
{
    public static ManifestGenerationResponse Validate(ManifestGenerationResponse response)
    {
        if (response.ProtocolVersion == ManifestGenerationResponse.Current) return response;
        return new ManifestGenerationResponse
        {
            Success = false,
            Error = $"unsupported manifest generation protocol {response.ProtocolVersion}; expected {ManifestGenerationResponse.Current}",
        };
    }
}
