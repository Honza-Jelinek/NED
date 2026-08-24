namespace NED.Core.Assets;

public sealed class InstanceEntry
{
    public required Guid Id { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required Guid TemplateId { get; init; }
}
