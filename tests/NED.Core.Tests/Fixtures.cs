using NED.Abstractions;

namespace NED.Core.Tests.Fixtures;

/// <summary>
/// Uzel bez bezparametrického konstruktoru. Dřív takový typ prošel katalogem a spadl až
/// při kliknutí v paletě (<c>Activator.CreateInstance</c>); generátor ho má ohlásit hned.
/// </summary>
[NodeInfo("No Ctor", Category = "Fixtures")]
public sealed class NoCtorNode : IGraphData
{
    public NoCtorNode(int required) => Value = required;

    [NodeField("Value")] public int Value { get; set; }
}

[NodeInfo("Base fixture", Category = "Fixtures")]
public class BaseNode : IGraphData;

[NodeInfo("Derived fixture", Category = "Fixtures")]
public sealed class DerivedNode : BaseNode;
