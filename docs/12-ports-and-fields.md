# Vstupy: port ↔ pole (přepínatelná expozice)

Klíčová UX vlastnost NEDa: **každý vstup nodu může být buď port (linkovatelný), nebo pole (inline konstanta)** — a u většiny typů se to dá za běhu přepnout. Atribut (`[NodePort]` vs. `[NodeField]`) určuje jen **výchozí** režim. Je to obdoba „pin default values" v UE/Blueprintu: nemusíš tahat samostatný `Number` node, když chceš jen zadat konstantu.

## Model — `NodeInput`

Při stavbě `DataNodeModel` se z každé `[NodePort]`/`[NodeField]` property vytvoří jeden `NodeInput`:

```csharp
public sealed class NodeInput
{
    public PropertyInfo Prop;     public string Label;     public Type DataType;
    public bool Multiple;         // pole (N linků) — NEpřepínatelné
    public bool Optional;         // nepovinný (auto: má-li default ≠ default(T))
    public bool TypePicker;       // [NodeTypePicker] — vždy pole, nikdy port
    public bool Complex;          // doménový typ (ne skalár) — ve field režimu dropdown tříd
    public bool Togglable;        // známý datový skalár mimo Details → smí se přepínat
    public bool DefaultAsPort;    // výchozí režim: [NodePort] → true, [NodeField] → false
    public bool AsPort;           // AKTUÁLNÍ režim
    public TypedPortModel? Port;  // živý port (jen v port režimu)
}
```

### Pravidla režimu

| Vstup | Výchozí | Přepínatelný? | Field režim vykreslí |
|---|---|---|---|
| skalár `[NodePort]` (`double`, `bool`, enum…) | port | ✅ | input / checkbox / select |
| skalár `[NodeField]` | pole | ✅ | input / checkbox / select |
| doménový typ (`Perk`, `Equipment`…) | dle atributu | ✅ | **dropdown** tříd z katalogu + subgrafů |
| `Multiple = true` (pole, N linků) | port | ❌ | — (zůstává port) |
| `[NodeTypePicker]` | pole | ❌ | dropdown typů z katalogu |
| `any` | port | ❌ | — (bez konkrétního typu nejde nabídnout smysluplný editor hodnoty) |
| `exec` | port | ❌ | — (řídicí hrana nemá konstantní hodnotu) |

Přepínatelné jsou známé datové skaláry mimo `Details`; výjimky tvoří `exec`, `any`, pole
(`Multiple`) a type-pickery. Doménové typy přepínatelné zůstávají, protože jejich field režim nabízí
konkrétní kompatibilní typy. `Complex = !typePicker && !IsScalar(type)`.

### Exec porty

`exec` je uzavřený řídicí typ: propojí se pouze s `exec`, nikdy s `any` ani s datovým typem.
Exec vstup musí mít `Kind: "Port"`; katalog chybný ruční manifest ohlásí a vstup načte jako
port. Pro sbíhání větví lze použít `Multiple: true`, aniž by UI zobrazovalo array ovál nebo `[]`.
Exec řádky se ve widgetu řadí před datové a používají bílý trojúhelníkový pin.

## Přepnutí za běhu

Pravý klik na port nebo pole → kontextové menu „Převést na pole / Převést na port" (`PortContextMenu`, přes `NodeEditorBridge`). Vlastní přepnutí:

```csharp
node.SetExposure(input, asPort);   // DataNodeModel
```

- **pole → port**: vytvoří `TypedPortModel` (typ z `DataType`, label, default pro tooltip), zaregistruje do `Inputs`.
- **port → pole**: zruší linky portu (ručně přes jejich diagram — `RemovePort` je nečistí), odebere port.

Přepnutí je zaznamenané do undo (`RecordUndo` před akcí).

## Hodnoty a render

`NodeFieldInput` je sdílený inline editor řízený stringovou hodnotou. Větví podle typu:

- **type-picker** → `<select>` z `Catalog.SelectableTypes()` (hodnota = type id).
- **complex** → `<select>`: kompatibilní type id z katalogu + subgrafy s kompatibilním output typem
  (hodnota = `"sg:GUID"`); vyloučí se právě editovaný graf.
- **enum** → `<select>` z hodnot `EnumDescriptor` v manifestu.
- **bool** → checkbox; **číslo** → number input; **jinak** → text.

Kde se hodnota drží:
- **skalár i complex hodnota** v `DataNodeModel.Values` pod stabilním jménem vstupu;
- **hodnota, které současný manifest nerozumí**, v `UnknownValues`, aby přežila save;
- **type-picker** jako type id string; po změně `RefreshDynamicTypes()` přetypuje porty.

## Persistence

Ukládá se **jen to, co se liší od výchozího** (čisté soubory, viz [04-persistence.md](04-persistence.md)):

- `GraphNodeDto.PortModes` — vstupy s režimem ≠ `DefaultAsPort`.
- skalární hodnoty jsou ve `Fields`.

Při loadu se expozice (`PortModes`) obnoví **před** připojováním linků, aby porty existovaly.

## Chování při exportu

Každý režim se v [05-export.md](05-export.md) přeloží jinak:

| Režim vstupu | Export |
|---|---|
| port (připojený) | rekurzivní strom producera; polový vstup → pole všech zdrojů |
| port (nepřipojený) | `null` |
| skalární pole | literálová hodnota (enum jako string) |
| doménové pole — třída | `{ "$type": "$literal", "value": "…" }` |
| doménové pole — `"sg:GUID"` | inline subgrafu (viz [10-subgraphs.md](10-subgraphs.md)) |

Pokud do polového vstupu vede producent, který sám vydává pole, export ho vloží jako
`{ "$spread": ... }`. Runtime tak pole zploští o jednu úroveň místo vytvoření matice.

## Subgraf-nody mají totéž

Vstupy `SubgraphNodeModel` se chovají stejně, jen expozice je **per-instance override** (`ExposureOverride`) nad výchozí expozicí z rozhraní subgrafu (`SubgraphInput.Exposure`). Field hodnoty jsou ve `FieldValues`, přepnutí přes `SetExposure(inputName, asPort)`. Render i kontextové menu sdílí stejný vizuál jako `DataNodeWidget`.
