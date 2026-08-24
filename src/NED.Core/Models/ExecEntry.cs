using NED.Abstractions;

namespace NED.Core;

[NodeInfo("Exec Entry", Category = "Flow", Color = "#3758CC", Icon = "▶")]
[NodeOutput("Then", typeof(Exec))]
public sealed class ExecEntry : IGraphData;
