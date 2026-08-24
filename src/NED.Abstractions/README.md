# NED.Abstractions

Lehký kontrakt pro datové typy, které se mají zobrazit jako uzly v editoru NED. Projekt cílí na `netstandard2.0`, nemá externí závislosti a nezná Blazor, WPF ani implementaci editoru. Díky tomu jej může sdílet doména, editor i případný Unity klient.

## Co obsahuje

- `IGraphData` – prázdné marker rozhraní každého uzlu.
- Atributy pro popis uzlů:
  - `[NodeInfo]` – název, kategorie, volitelná barva a ikona;
  - `[NodeField]` – inline editovatelná hodnota;
  - `[NodePort]` – linkovatelný vstup;
  - `[NodeOutput]` – pojmenovaný výstupní typ (atribut lze použít vícekrát);
  - `[NodeSink]` – uzel bez výstupu;
  - `[NodeTypePicker]` a `[NedDescription]` – renderovací a popisná metadata.

## Použití

```csharp
using NED.Abstractions;

[NodeInfo("Damage", Category = "Combat")]
public sealed class Damage : IGraphData
{
    [NodeField("Amount", Min = 0)]
    public int Amount { get; set; }

    [NodePort("Multiplier", Optional = true)]
    public double Multiplier { get; set; } = 1;

}
```

Generátor `ned-manifest` z anotované assembly vyrobí manifest a editor čte ten — doménovou assembly nikdy nenačítá. Atributy slouží pouze jako metadata; runtime je může ignorovat. Viz [manifest](../../docs/14-manifest.md).

## Hranice projektu

`NED.Abstractions` nesmí získat závislost na `NED.Core`, `NED.Shell.Wpf`, `MudBlazor`, `Z.Blazor.Diagrams` ani na konkrétní doméně. Persistence, UI a export patří do `NED.Core`.

Další informace: [kontrakt a atributy](../../docs/01-abstractions.md), [typované porty](../../docs/02-typed-ports.md) a [struktura řešení](../../docs/06-project-structure.md).
