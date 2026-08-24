namespace NED.Core;

/// <summary>Výsledek dialogu Nový graf.</summary>
/// <param name="Flow">Data flow, nebo exec.</param>
/// <param name="OutputType">Type id první deklarované návratové hodnoty. null = žádná.</param>
public sealed record NewGraphResult(GraphFlow Flow, string? OutputType, string? Name);
