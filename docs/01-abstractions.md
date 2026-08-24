# NED.Abstractions — kontrakt editoru

Abstrakční vrstva, kterou implementují node packy (například `Sandbox`). Nemá závislost na Z.Blazor.Diagrams, Blazor, WPF ani MudBlazor a cílí na `netstandard2.0`.

Obsah projektu (kompletní výčet):

```
IGraphData.cs          marker interface
EditorAttributes.cs    [NodeInfo] [NodeField] [NodeTypePicker] [NodePort] [NodeOutput] [NodeSink]
IExportTranslator.cs   jazykově neutrální model a pluggable export translator
Manifest/              DTO node-pack manifestu a stabilní type id
IsExternalInit.cs       shim pro `init` accessory na netstandard2.0
```

## IGraphData — marker interface

```csharp
public interface IGraphData
{
}
```

Každý typ, který se má objevit jako node v grafu, toto implementuje.

Rozhraní je záměrně **prázdné**. Generátor z anotovaného typu čte jen *tvar* — atributy a
properties — a zapíše ho do manifestu. Editor pak běží bez reference na doménovou assembly a při
načítání packu její kód nespouští; generování z projektu ale spouští jeho build, viz [14](14-manifest.md).

Příklad (z projektu Sandbox):
```csharp
[NodeInfo("Add", Category = "Math")]
[NodeOutput(typeof(double))]
public class Add : IGraphData
{
    [NodePort("A")] public double A { get; set; }
    [NodePort("B")] public double B { get; set; }
}
```

## Metadata atributy

Čistá anotace — `ned-manifest` je čte reflexí mimo proces editoru a zapíše jejich tvar do JSON;
NED.Core pak generuje UI z manifestu. Server/runtime atributy může ignorovat.

### `[NodeInfo]` — metadata třídy

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class NodeInfoAttribute : Attribute
{
    public string Name { get; }                         // "Add", "Perk", ...
    public string Category { get; init; } = "General";  // "Math", "Perks", ...
    public string? Color { get; init; }                 // CSS hex; null = vzhled z NedTheme dle Category
    public string? Icon { get; init; }                  // emoji/symbol; null = z NedTheme dle Category

    public NodeInfoAttribute(string name) => Name = name;
}
```

`Color`/`Icon` jsou **nullable**: `null` znamená „nepřepsáno" → vzhled se vezme z [`NedTheme`](03-ned-core-engine.md) podle `Category`. Tím se barvy řídí centrálně z `ned-theme.json` a doménové typy nemusí hardcodovat hex.

```csharp
[NodeInfo("Add", Category = "Math")]                          // barva/ikona z theme (Math)
[NodeInfo("Perk", Category = "Perks", Color = "#caa44b", Icon = "★")]  // explicitní override
```

### `[NodeField]` — inline editovatelné pole

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class NodeFieldAttribute : Attribute
{
    public string Label { get; }
    public double Min { get; init; } = double.NaN;
    public double Max { get; init; } = double.NaN;

    public NodeFieldAttribute(string label) => Label = label;
}
```

Widget vykreslí input podle typu property: `int`/`double`/… → number, `string` → text, `bool` → checkbox, `enum` → select.

```csharp
[NodeField("Value")] public double Value { get; set; }
[NodeField("Slot")]  public EquipmentSlot Slot { get; set; }   // → dropdown enumu
```

> **Pozn.:** `[NodeField]` a `[NodePort]` se obě stanou „vstupem" nodu a u skalárních typů jsou za běhu **přepínatelné mezi polem a portem**. Atribut určuje jen výchozí režim. Viz [12-ports-and-fields.md](12-ports-and-fields.md).

### `[NodeTypePicker]` — render hint: dropdown typů

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class NodeTypePickerAttribute : Attribute { }
```

Doplněk k `[NodeField]` na `string` vlastnosti, která drží **plně kvalifikovaný název typu**. Widget místo textového pole vykreslí dropdown typů z katalogu. Property musí mít i `[NodeField]` (kvůli persistenci + popisku). Type-picker se nikdy nestane portem.

Používá to `GraphInputNode` (volba typu parametru subgrafu) — viz [10-subgraphs.md](10-subgraphs.md).

### `[NodePort]` — linkovatelný vstup (levý port)

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class NodePortAttribute : Attribute
{
    public string Label { get; }
    public bool Multiple { get; init; }   // true = pole (N linků do jednoho slotu), vizuálně oválný port
    public bool Optional { get; init; }   // true = nepovinný vstup, vizuálně hranatý port

    public NodePortAttribute(string label) => Label = label;
}
```

Barva portu se určí z **datového typu** property (ne z kategorie nodu) — viz [02-typed-ports.md](02-typed-ports.md). `Optional` se navíc **automaticky odvodí** u property s explicitní výchozí hodnotou (≠ `default(T)`), takže ho často není třeba psát ručně.

```csharp
[NodePort("A")] public double A { get; set; }                       // 1 link
[NodePort("Values", Multiple = true)] public double Values { get; set; }  // N linků (pole)
[NodePort("In", Optional = true)] public string In { get; set; } = ""; // nepovinný
```

### `[NodeOutput]` — výstupní port (pravý)

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class NodeOutputAttribute : Attribute
{
    public string Name { get; }          // bez jména = "Out"
    public Type? Type { get; }          // null = node produkuje sám sebe; typeof(double) = value-producing
    public bool Multiple { get; init; } // jeden drát nese pole prvků Type

    public NodeOutputAttribute() { }
    public NodeOutputAttribute(Type outputType) => Type = outputType;
    public NodeOutputAttribute(string name) => Name = name;
    public NodeOutputAttribute(string name, Type outputType) { ... }
}
```

Výchozí (bez atributu): node produkuje **sám sebe** (container/doménový node — `Perk`, `Equipment`). Computational nody si typ přepíšou:

```csharp
[NodeOutput(typeof(double))]   // → produkuji double, ne Add
public class Add : IGraphData { ... }

[NodeOutput("Index", typeof(int))]
[NodeOutput("Completed")]     // → více současně dostupných pojmenovaných výstupů
public sealed class ForLoop : IGraphData { ... }

[NodeOutput(typeof(double), Multiple = true)]
public sealed class NumberList : IGraphData { ... } // → jeden výstup typu double[]

[NodeSink]                     // → žádný output port (kořen, např. OutputNode)
public sealed class OutputNode : IGraphData { ... }
```

Barva výstupního portu se určí z output typu — stejné mapování jako vstup.

## Typy portů určené za běhu

Editor neinstanciuje doménové CLR typy; porty staví z `NodeTypeDescriptor` v manifestu. Vestavěné
interface uzly mají navíc runtime pravidla přímo v `DataNodeModel`:

- u `GraphInputNode` čte `ResolveOutputType()` type id z hodnoty pole `InputTypeName`;
- `OutputNode` a `ReturnNode` skládají hodnotové vstupy z deklarací v `GraphSettings.Outputs`;
- `Multiple` se přenese na výstupní port a určuje jeho aritu i export jako ploché pole.

Po změně type-pickeru `RefreshDynamicTypes()` přepočítá výstupní porty parametru. Nejde o autorský
extension point: pack popisuje statický tvar atributy a manifestem. Detail viz
[03-ned-core-engine.md](03-ned-core-engine.md) a [10-subgraphs.md](10-subgraphs.md).

## Serializace — kde se řeší

Abstractions definuje POCO model manifestu a exportní model, ale vlastní JSON čtení a zápis žije v
`NED.Core` (viz [04-persistence.md](04-persistence.md), [05-export.md](05-export.md)). Persistence
neukládá runtime strom: uzel je stabilní type id v historicky pojmenovaném poli `TypeName` plus
slovník `Fields`; při loadu se descriptor dohledá v `NedCatalog`.

## Nic víc

Abstractions nezná Z.Diagrams, Blazor ani WPF. Obsahuje autorské atributy, marker interface,
jazykově neutrální DTO manifestu/exportu. Reflexi používá pouze externí
generátor; editor pracuje s JSON manifestem.
