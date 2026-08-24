using NED.Abstractions;

namespace Sandbox;

[NodeInfo("Text", Category = "String")]
[NodeOutput(typeof(string))]
public class StringConstant : IGraphData
{
    [NodeField("Text")] public string Text { get; set; } = "";
}

[NodeInfo("Concat", Category = "String")]
[NodeOutput(typeof(string))]
public class Concat : IGraphData
{
    [NodePort("A", Multiple = true)] public string A { get; set; } = "";
}

[NodeInfo("To Upper", Category = "String")]
[NodeOutput(typeof(string))]
public class ToUpper : IGraphData
{
    [NodePort("In", Optional = true)] public string In { get; set; } = "";
}
