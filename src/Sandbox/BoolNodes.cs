using NED.Abstractions;

namespace Sandbox;

[NodeInfo("Bool", Category = "Bool")]
[NodeOutput(typeof(bool))]
public class BoolConstant : IGraphData
{
    [NodeField("Value")] public bool Value { get; set; }
}

[NodeInfo("And", Category = "Bool")]
[NodeOutput(typeof(bool))]
public class And : IGraphData
{
    [NodePort("A")] public bool A { get; set; }
    [NodePort("B")] public bool B { get; set; }
}

[NodeInfo("Or", Category = "Bool")]
[NodeOutput(typeof(bool))]
public class Or : IGraphData
{
    [NodePort("A")] public bool A { get; set; }
    [NodePort("B")] public bool B { get; set; }
}

[NodeInfo("Not", Category = "Bool")]
[NodeOutput(typeof(bool))]
public class Not : IGraphData
{
    [NodePort("In")] public bool In { get; set; }
}
