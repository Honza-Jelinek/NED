using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions.Manifest;
using NED.Core.Persistence;
using NED.Core.Manifest;

namespace NED.Core;

/// <summary>
/// Placeholder pro node, jehož <see cref="GraphNodeDto.TypeName"/> se nepodařilo resolvovat
/// v katalogu (chybí node pack). Drží PŮVODNÍ DTO beze změny, aby se při
/// dalším uložení nic neztratilo (Fields, PortModes). Vstupní porty se staví
/// z linků v dokumentu (nevíme, které vstupy byly porty) — všechny s typem Any.
/// </summary>
public sealed class MissingNodeModel : NodeModel
{
    /// <summary>Původní deserializované DTO — zdroj pravdy pro round-trip save.</summary>
    public GraphNodeDto Dto { get; }

    /// <summary>Vstupní porty — jméno vstupu (ToPort z linků) → port (pro save/load linků).</summary>
    public Dictionary<string, TypedPortModel> InputPorts { get; } = new();

    /// <summary>Výstupní porty (Any), odvozené z FromPort uložených linků.</summary>
    public Dictionary<string, TypedPortModel> Outputs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Id modelu = Id z DTO, aby linky zapsané při save (FromNode/ToNode = node.Id)
    /// odpovídaly Id nodu v dokumentu.
    /// </summary>
    public MissingNodeModel(GraphNodeDto dto, IEnumerable<string> inputNames,
                            IEnumerable<string> outputNames, Point? position = null)
        : base(dto.Id, position ?? new Point(dto.X, dto.Y))
    {
        Dto = dto;

        foreach (var name in inputNames.Distinct())
        {
            var port = new TypedPortModel(this, PortAlignment.Left, TypeIds.Any)
            {
                Label = name,
            };
            AddPort(port);
            InputPorts[name] = port;
        }

        var names = outputNames.Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0) names.Add(BuiltInIds.DefaultOutput);
        foreach (var name in names)
        {
            var output = new TypedPortModel(this, PortAlignment.Right, TypeIds.Any) { Label = name };
            AddPort(output);
            Outputs[name] = output;
        }
    }

    /// <summary>Krátký název typu — poslední segment za tečkou (pro header widgetu).</summary>
    public string ShortTypeName
    {
        get
        {
            var full = Dto.TypeName;
            var dot = full.LastIndexOf('.');
            return dot >= 0 && dot < full.Length - 1 ? full[(dot + 1)..] : full;
        }
    }

    /// <summary>Kopie DTO s novým Id (pro duplikaci nodu). Slovníky se kopírují mělce.</summary>
    public GraphNodeDto CloneDto() => new()
    {
        Id = Guid.NewGuid().ToString(),
        X = Dto.X,
        Y = Dto.Y,
        TypeName = Dto.TypeName,
        Fields = new Dictionary<string, object?>(Dto.Fields),
        PortModes = Dto.PortModes is null ? null : new Dictionary<string, bool>(Dto.PortModes),
    };
}
