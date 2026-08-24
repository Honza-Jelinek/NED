# Typované porty — link validace a barvy

Porty nejsou generické `IGraphData`. Každý port nese **datový typ** a NED z něj odvozuje barvu i validaci, které linky smí vzniknout.

## TypedPortModel

```csharp
public sealed class TypedPortModel : PortModel
{
    // Stabilní type id z manifestu (in: co přijímám, out: co produkuji).
    // Settable — GraphInputNode ho mění za běhu přes type-picker.
    public string DataType { get; set; }

    public bool Multiple { get; set; }          // port nese pole; vstup navíc přijme víc linků
    public string? Label { get; set; }          // popisek (tooltip); output ho má null
    public bool HasExplicitDefault { get; set; } // má autorskou výchozí hodnotu (tooltip)
    public object? DefaultValue { get; set; }    // výchozí hodnota (tooltip)

    public string Tooltip { get; }              // "Label: Typ[]  •  default: X"

    public override bool CanAttachTo(ILinkable other) { ... }
}
```

`Tooltip` se zobrazí při najetí na port a používá lidsky čitelné názvy (`TypeNames.Friendly`): `Double`, `String`, `Bool`, `Any` (= `IGraphData`).

## Kompatibilita typů

`CanAttachTo` zjistí směr (levý = input, pravý = output) a zavolá `IsCompatible(output, input)`. Output je kompatibilní se vstupním slotem, pokud platí **jedno** z:

1. **Input je `object` / `IGraphData` / přiřaditelný interface** (Any) — přijme cokoliv
   ```
   Number(double) → deklarovaný vstup typu any  ✓
   ```
2. **Přesná shoda nebo polymorfie** (`input.IsAssignableFrom(output)`)
   ```
   PerkChild → slot typu Perk  ✓    // PerkChild : Perk
   ```
3. **Implicitní číselné rozšíření** — output rank ≤ input rank
   ```
   rank:  byte 1 < short 2 < int 3 < long 4 < float 5 < double 6 < decimal 7
   Number(int) → Add.A(double)   ✓   // 3 ≤ 6
   Number(double) → slot(float)  ✗   // 6 > 5, zúžení zakázáno
   ```

Vedle datového typu se kontroluje i arita. Skalár lze připojit do skalárního i polového vstupu,
polový výstup pouze do polového vstupu. Polový vstup tak může sesbírat skaláry i zploštit již
polové producery; export druhý případ označí markerem `$spread`.

Výsledek (Sandbox nody):
- `Number(→double) → Add.A(∈double)` ✓
- `Bool(→bool) → And.A(∈bool)` ✓
- `Number(→double) → And.A(∈bool)` ✗ link se nevytvoří
- `cokoliv → deklarovaný vstup typu any` ✓
- `double[] → double` ✗, `double → double[]` ✓, `double[] → double[]` ✓

Nekompatibilní link **nevznikne** — ZBD ho při dropu zahodí.

## Port barva

Barva se počítá z `NedTheme.PortColorFor(DataType)` (ne ze statické třídy — theming je centrální, konfigurovatelný přes `ned-theme.json`, viz [03-ned-core-engine.md](03-ned-core-engine.md)):

| Typ (CLR) | Default barva | Příklad nodu |
|---|---|---|
| `Int32`/`Int64`/`Int16`/`Byte` | zelená `#5dba6a` | celočíselné |
| `Double`/`Single`/`Decimal` | tyrkysová `#3a8f7a` | `Number`, `Add`, `Sum` |
| `String` | růžová `#d4699a` | `Text`, `Concat`, `ToUpper` |
| `Boolean` | červená `#c45a5a` | `Bool`, `And`, `Or`, `Not` |
| `exec` | bílá `#E8E8E8` | `ExecEntry`, `Branch`, `Sequence` |
| ostatní (Any) | `#8a93a8` | doménové typy |

Když je port typu, který v theme nemá barvu (doménový `Perk`, `Equipment`…), fallback je **barva kategorie toho typu** (`NedTheme.Resolve(info).Color`), a teprve není-li ani ta, barva vlastnícího nodu.

## Port tvar

Tvar řídí CSS třídy widgetu podle vlastností vstupu:

| Vlastnost | Tvar | Případ |
|---|---|---|
| běžný | kruh | `[NodePort("A")]` |
| `Multiple = true` | oválný (protáhlý) | `[NodePort("Values", Multiple = true)]` |
| `Optional` (nebo má default) | hranatý | `[NodePort("In", Optional = true)]` |
| type id `exec` | řídicí štítek; dutý bez linku, plný po napojení | `[NodePort("In")] public Exec? In` |

`Optional` se odvodí i automaticky: vstup s property hodnotou ≠ `default(T)` se bere jako nepovinný (graf smí zůstat nepřipojený, použije se default).

## Když port vůbec není

Skalární vstup může být přepnut do **field režimu** (inline konstanta) — pak žádný port neexistuje a hodnota se edituje přímo v těle nodu. To je per-instance a ukládá se. Detail viz [12-ports-and-fields.md](12-ports-and-fields.md).

## Typy po Loadu

Persistence neukládá typ portu — při rekonstrukci `DataNodeModel` ho znovu vezme z manifestového
descriptoru. `GraphInputNode` navíc čte zvolené type id z `InputTypeName`; `OutputNode` a
`ReturnNode` obnoví dynamické vstupy z `Settings.Outputs`, včetně arity. Cílový port linku se dohledá
podle **jména vstupu** (`GraphLinkDto.ToPort`), zatímco stabilní id deklarace umožní port zachovat při
přejmenování za běhu. Viz [04-persistence.md](04-persistence.md).
