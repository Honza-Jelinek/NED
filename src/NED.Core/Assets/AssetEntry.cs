using System;

namespace NED.Core.Assets;

/// <summary>
/// Jeden indexovaný asset (graf nebo subgraf) nalezený v knihovní složce.
/// Identita je GUID vložený v souboru, ne cesta — přežije přesun/přejmenování.
/// </summary>
public sealed class AssetEntry
{
    public required Guid   Id   { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public GraphFlow       Flow { get; init; } = GraphFlow.Data;

    /// <summary>Smí se z grafu udělat instance (bývalá role <c>Template</c>).</summary>
    public bool Instanceable { get; init; }

    /// <summary>Cachované rozhraní (návratový typ; vstupy v C2).</summary>
    public SubgraphInterface Interface { get; init; } = new();
}
