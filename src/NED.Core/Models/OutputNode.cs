using NED.Abstractions;

namespace NED.Core;

/// <summary>
/// Sink datového grafu — místo, kam se scházejí dráty návratových hodnot.
///
/// Nemá žádná vlastní pole: co graf vrací, deklaruje <c>GraphSettings.Outputs</c>,
/// a uzel z toho jen staví vstupní porty (<see cref="DataNodeModel.SyncDeclaredInputs"/>).
/// Stejně jako <see cref="GraphInputNode"/> se za běhu <b>neinstanciuje</b> — je to jen
/// deklarace metadat, ze které generátor vyrobí vestavěný manifest.
///
/// V exec toku nemá místo; tam hodnoty vracejí <see cref="ReturnNode"/> uzly, protože
/// návrat je událost v exec pořadí a datový tah pozpátku žádné pořadí nemá.
/// </summary>
[NodeInfo("Output", Category = "Output", Color = "#3758CC", Icon = "➤")]
[NodeSink]
public sealed class OutputNode : IGraphData
{
}
