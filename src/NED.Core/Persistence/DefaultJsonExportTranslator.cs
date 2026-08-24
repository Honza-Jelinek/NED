using System.Text.Json;
using System.Text.Json.Serialization;
using NED.Abstractions;

namespace NED.Core.Persistence;

/// <summary>Vestavěný výchozí formát exportu podle veřejné smlouvy ned-export-v1.</summary>
public sealed class DefaultJsonExportTranslator : IExportTranslator
{
    public string Id => "ned.json";
    public string DisplayName => "JSON (default)";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Translate(ExportModel model)
    {
        var payload = new Dictionary<string, object?>
        {
            ["exportVersion"] = model.ExportVersion,
            ["packs"] = model.Packs.Select(PackPayload).ToList(),
            ["settings"] = SettingsPayload(model),
        };
        if (model.Inputs.Count > 0)
            payload["inputs"] = model.Inputs.Select(InputPayload).ToList();
        if (model.Outputs.Count > 0)
            payload["outputs"] = model.Outputs.Select(OutputPayload).ToList();
        if (model.Nodes is not null)
        {
            payload["entry"] = model.Entry;
            payload["nodes"] = model.Nodes;
            payload["exec"] = model.ExecEdges?.Select(EdgePayload).ToList();
            if (model.Functions.Count > 0)
                payload["functions"] = model.Functions.Select(FunctionPayload).ToList();
        }
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    private static Dictionary<string, object?> SettingsPayload(ExportModel model)
    {
        var settings = new Dictionary<string, object?>();
        if (model.GraphKind is not null) settings["graphKind"] = model.GraphKind;
        return settings;
    }

    /// <summary>
    /// Deklarace návratu. <c>value</c> se píše i když je null — u nezapojeného skaláru
    /// je null sama o sobě odpověď, kdežto vynechaný klíč by vypadal jako chybějící
    /// deklarace. V exec toku se vynechá celý, hodnoty tam nesou Return uzly.
    /// </summary>
    private static Dictionary<string, object?> OutputPayload(ExportGraphOutput output)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = output.Name,
            ["type"] = output.Type,
            ["multiple"] = output.Multiple,
        };
        if (output.HasValue) payload["value"] = output.Value;
        return payload;
    }

    // Prázdné sekce se vynechávají celé — WhenWritingNull na hodnoty slovníku neplatí,
    // takže by jinak v každé funkci bez parametrů zůstalo "inputs": null.
    private static Dictionary<string, object?> FunctionPayload(ExportFunction function)
    {
        var payload = new Dictionary<string, object?> { ["id"] = function.Id, ["name"] = function.Name };
        if (function.Inputs.Count > 0)
            payload["inputs"] = function.Inputs.Select(InputPayload).ToList();
        payload["entry"] = function.Entry;
        payload["nodes"] = function.Nodes;
        payload["exec"] = function.ExecEdges.Select(EdgePayload).ToList();
        if (function.Outputs.Count > 0)
            payload["outputs"] = function.Outputs.Select(output => new Dictionary<string, object?>
            {
                ["name"] = output.Name,
                ["type"] = output.Type,
                ["multiple"] = output.Multiple,
            }).ToList();
        return payload;
    }

    private static Dictionary<string, object?> EdgePayload(ExportExecEdge edge) => new()
    {
        ["from"] = edge.From, ["pin"] = edge.Pin, ["to"] = edge.To,
    };

    private static Dictionary<string, object?> InputPayload(ExportGraphInput input) => new()
    {
        ["name"] = input.Name,
        ["type"] = input.Type,
        ["multiple"] = input.Multiple,
        ["default"] = input.Default,
        ["description"] = input.Description,
    };

    private static Dictionary<string, object?> PackPayload(ExportPack pack)
    {
        var payload = new Dictionary<string, object?> { ["id"] = pack.Id };
        if (pack.Version is not null) payload["version"] = pack.Version;
        return payload;
    }
}
