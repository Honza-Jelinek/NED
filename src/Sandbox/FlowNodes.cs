using NED.Abstractions;

namespace Sandbox;

[NodeInfo("Branch", Category = "Flow")]
[NodeOutput("True", typeof(Exec))]
[NodeOutput("False", typeof(Exec))]
public sealed class Branch : IGraphData
{
    [NodePort("In", Multiple = true)] public Exec? In { get; set; }
    [NodePort("Cond")] public bool Cond { get; set; }
}

[NodeInfo("Sequence", Category = "Flow")]
[NodeOutput("Then", typeof(Exec))]
[NodeOutput("Next", typeof(Exec))]
public sealed class Sequence : IGraphData
{
    [NodePort("In", Multiple = true)] public Exec? In { get; set; }
}

/// <summary>
/// Smycka: <c>Body</c> je vnorena cesta, po jejim dobehnuti se rizeni vraci sem.
/// Sandbox uzel schvalne — validator potrebuje aspon jeden Subflow pin, aby sel
/// otestovat rozdil mezi „konec tela" a „konec behu".
/// </summary>
[NodeInfo("Loop", Category = "Flow")]
[NodeOutput("Body", typeof(Exec), ExecRole = ExecOutputRole.Subflow)]
[NodeOutput("Then", typeof(Exec))]
public sealed class Loop : IGraphData
{
    [NodePort("In", Multiple = true)] public Exec? In { get; set; }
}
