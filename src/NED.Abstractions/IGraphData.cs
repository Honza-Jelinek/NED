namespace NED.Abstractions;

/// <summary>
/// Marker pro každý typ, který se může objevit jako node v grafu.
///
/// Záměrně prázdný: NED z anotovaného typu čte jen <b>tvar</b> (atributy + properties),
/// nikdy nespouští jeho kód. Díky tomu jde metadata vyexportovat do manifestu a editor
/// pak běží bez reference na doménovou assembly.
/// </summary>
public interface IGraphData
{
}

/// <summary>
/// Marker pro autory packů v C#: <c>[NodeOutput("Then", typeof(Exec))]</c> nebo
/// <c>[NodePort("In")] public Exec? In</c> vyrobí port typu <c>exec</c>.
///
/// Není <see cref="IGraphData"/> — v grafu se nikdy neobjeví jako uzel. Generátor ho
/// překládá přes <c>TypeIds.FromClrType</c>, což je dřív než kontrola markeru.
/// </summary>
public sealed class Exec;
