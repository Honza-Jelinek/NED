using NED.Core.Assets;

namespace NED.Core;

public sealed class InstanceTab : TabBase
{
    public InstanceData Data { get; set; } = new();
    public SubgraphInterface? TemplateInterface { get; set; }

    public override string Title => string.IsNullOrWhiteSpace(Data.Name)
        ? (FilePath is not null
            ? Path.GetFileNameWithoutExtension(FilePath).Replace(".nedinst", "")
            : $"Instance {Index}")
        : Data.Name;

    public override string Icon => "⚙";
    public override bool CanExport => TemplateInterface is not null;
}
