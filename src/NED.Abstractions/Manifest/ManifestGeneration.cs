using System.Collections.Generic;

namespace NED.Abstractions.Manifest;

/// <summary>
/// Jazykově neutrální odpověď externího generátoru node packu. Generátor ji v režimu
/// strojového výstupu zapíše jako jediný JSON objekt na stdout; editor nevěří pouze
/// návratovému kódu a výsledný manifest znovu načte a ověří.
/// </summary>
public sealed class ManifestGenerationResponse
{
    public const int Current = 1;

    public int ProtocolVersion { get; set; } = Current;
    public bool Success { get; set; }
    public string? ManifestPath { get; set; }
    public PackInfo? Pack { get; set; }
    public int TypeCount { get; set; }
    public int EnumCount { get; set; }
    public string? Error { get; set; }
    public List<ManifestGenerationDiagnostic> Diagnostics { get; set; } = new();
}

/// <summary>Jedna strukturovaná poznámka generátoru, nezávislá na jeho implementačním jazyku.</summary>
public sealed class ManifestGenerationDiagnostic
{
    public ManifestGenerationSeverity Severity { get; set; }
    public string Message { get; set; } = "";
    public string? Source { get; set; }
}

public enum ManifestGenerationSeverity
{
    Info,
    Warning,
    Error,
}
