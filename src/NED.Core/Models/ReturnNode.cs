using NED.Abstractions;

namespace NED.Core;

/// <summary>
/// Konec exec větve, který v okamžiku průchodu vrátí hodnoty deklarované Output uzly.
/// Hodnotové vstupy doplňuje editor dynamicky; manifest nese jen řídicí vstup.
/// </summary>
[NodeInfo("Return", Category = "Flow", Color = "#3758CC", Icon = "↩")]
[NodeSink]
public sealed class ReturnNode : IGraphData
{
    [NodePort("In", Id = Manifest.BuiltInIds.ExecInput, Multiple = true)]
    public Exec? In { get; set; }
}
