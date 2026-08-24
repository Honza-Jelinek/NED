using NED.Abstractions;

namespace Sandbox;

[NodeInfo("Number", Category = "Math")]
[NodeOutput(typeof(double))]
public class NumberConstant : IGraphData
{
    [NodeField("Value")] public double Value { get; set; }
}

[NodeInfo("Add", Category = "Math")]
[NedDescription("Sečte dvě čísla.")]
[NodeOutput(typeof(double))]
public class Add : IGraphData
{
    [NodePort("A")]
    [NedDescription("První sčítanec")]
    public double A { get; set; }
    [NodePort("B")]
    [NedDescription("Druhý sčítanec")]
    public double B { get; set; }
}

[NodeInfo("Subtract", Category = "Math")]
[NodeOutput(typeof(double))]
public class Subtract : IGraphData
{
    [NodePort("A")] public double A { get; set; }
    [NodePort("B")] public double B { get; set; }
}

[NodeInfo("Multiply", Category = "Math")]
[NodeOutput(typeof(double))]
public class Multiply : IGraphData
{
    [NodePort("A")] public double A { get; set; }
    [NodePort("B")] public double B { get; set; }
}

[NodeInfo("Divide", Category = "Math")]
[NodeOutput(typeof(double))]
public class Divide : IGraphData
{
    [NodePort("A")] public double A { get; set; }
    [NodePort("B")] public double B { get; set; }
}

[NodeInfo("Sum", Category = "Math")]
[NodeOutput(typeof(double))]
public class Sum : IGraphData
{
    [NodePort("Values", Multiple = true)] public double Values { get; set; }
}


[NodeInfo("Average", Category = "Math")]
public class Average : IGraphData
{
    [NodePort("Values", Multiple = true)] public double Values { get; set; }
}