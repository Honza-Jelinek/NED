namespace NED.Core;

public sealed class InstanceData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid TemplateId { get; set; }
    public string TemplateName { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = new();
}
