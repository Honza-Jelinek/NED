using System.Text.Json;
using System.Text.Json.Serialization;

namespace NED.Core.Persistence;

public sealed class InstanceDocument
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string TemplateId { get; set; } = "";
    public string? TemplateName { get; set; }
    public Dictionary<string, string> Values { get; set; } = new();
}

public static class InstancePersistence
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static InstanceDocument ToDocument(InstanceData data) => new()
    {
        Id = data.Id.ToString(),
        Name = string.IsNullOrWhiteSpace(data.Name) ? null : data.Name,
        TemplateId = data.TemplateId.ToString(),
        TemplateName = data.TemplateName,
        Values = new Dictionary<string, string>(data.Values),
    };

    public static InstanceData FromDocument(InstanceDocument doc) => new()
    {
        Id = Guid.TryParse(doc.Id, out var id) && id != Guid.Empty ? id : Guid.NewGuid(),
        Name = doc.Name ?? "",
        TemplateId = Guid.TryParse(doc.TemplateId, out var tid) ? tid : Guid.Empty,
        TemplateName = doc.TemplateName ?? "",
        Values = new Dictionary<string, string>(doc.Values),
    };

    public static string Serialize(InstanceDocument doc) =>
        JsonSerializer.Serialize(doc, JsonOpts);

    public static InstanceDocument? Deserialize(string json) =>
        JsonSerializer.Deserialize<InstanceDocument>(json, JsonOpts);
}
