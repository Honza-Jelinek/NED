# NED.Core — srdce editoru

Blazor knihovna (`net10.0`, RCL), kterou hostitel (composition root, [06-project-structure.md](06-project-structure.md)) integruje. Obsahuje rendering uzlů z manifestu, typované porty, save/load/export, asset/knihovní vrstvu, undo a celý editor jako jednu drop-in komponentu. Závisí na `Z.Blazor.Diagrams` (plátno) a `MudBlazor` (dialogy, menu).

Tento dokument popisuje **engine primitives**. Editor jako UX (taby, picker, kontextová menu, zkratky) je v [11-editor-ux.md](11-editor-ux.md); přepínání port↔pole v [12-ports-and-fields.md](12-ports-and-fields.md); knihovny/assety v [13-libraries-and-assets.md](13-libraries-and-assets.md).

## Registrace — `AddNed`

Hostitel registruje NED do DI a řekne, které assembly oskenovat na `IGraphData` typy:

```csharp
services.AddNed(o => o
    .Manifest("manifests/Sandbox.nodes.json")     // node pack (Math/String/Bool)
    .Manifest("manifests/Sandbox.nodes.json")
    .LibraryConfig(Path.Combine(appData, "NED", "libraries.json")));
```

`AddNed(Action<NedOptions>)` zaregistruje: `NedOptions` (singleton), `NedTheme` (singleton), `NedCatalog` (scoped), `AssetIndex` (scoped) a MudBlazor služby. `NedOptions` fluent API:

| Metoda | Účel |
|---|---|
| `Manifest(path)` / `Manifest(NodeManifest)` | node pack k načtení (viz [14-manifest.md](14-manifest.md)) |
| `LibraryConfig(jsonPath)` | kam ukládat seznam knihovních rootů (přežije restart) |
| `Style(category, color, icon)` | přepiš/přidej default vzhled kategorie |
| `LoadTheme(jsonPath)` | načti `NedTheme` ze souboru (merge přes builtin defaults) |
| `PortColor(typeName, color)` | přepiš barvu portu pro typ (např. `"double"`) |

Engine je doménově slepý a typy uzlů bere z manifestů. Hardcodované jsou pouze runtime hooky
vestavěných interface uzlů (`OutputNode`, `ReturnNode`, `GraphInputNode`, `ExecEntry`).

## NedCatalog — auto-discovery

```csharp
public sealed class NedCatalog
{
    public IReadOnlyList<NodeTypeDescriptor> AllTypes { get; }
    public NodeTypeDescriptor? Resolve(string? typeId);
    public IEnumerable<IGrouping<string, NodeTypeDescriptor>> ByCategory();
    public IReadOnlyList<string> SelectableTypes();
    public static IEnumerable<(string Type, bool Multiple)> InputPorts(NodeTypeDescriptor type);
    public static IEnumerable<(string Type, bool Multiple)> OutputPorts(NodeTypeDescriptor type);
    public static IEnumerable<string> OutputTypesOf(NodeTypeDescriptor type);
    public static bool HasExecPort(NodeTypeDescriptor type);
}
```

`NedCatalog` slučuje manifesty podle stabilních type id. Reflexi používá jen pro discovery skutečného
kódu `IExportTranslator`; vadné typy z translator assembly odfiltruje `SafeGetTypes`.

`SelectableTypes()` vrací type id, která lze zvolit jako output typ grafu / hodnotu type-pickeru:
`any` jako první, skaláry a produkovatelné typy napříč katalogem. Vynechá sinky, vestavěné interface
uzly a uzavřený řídicí typ `exec`.

## NedTheme — centrální styling

Jedno místo pro vzhled kategorií (barva headeru + ikona) i barvy portů (typ → barva). Předvyplněno z **embedded `ned-theme.json`**, přepsatelné z konfigurace.

```json
// ned-theme.json (zkráceno)
{
  "fallback":   { "color": "#5a607a", "icon": "•" },
  "categories": {
    "Math":   { "color": "#3a6fb0", "icon": "∑" },
    "String": { "color": "#3a8f5a", "icon": "Ab" },
    "Bool":   { "color": "#b06a2c", "icon": "✓" },
    "Output": { "color": "#8844aa", "icon": "➤" }
  },
  "portColors": { "any": "#8a93a8", "int": "#5dba6a", "double": "#3a8f7a",
                  "string": "#d4699a", "bool": "#c45a5a", ... }
}
```

```csharp
public sealed class NedTheme
{
    public NodeStyle Fallback { get; }
    public string AnyPortColor { get; }

    public NodeStyle ForCategory(string category);
    public NodeStyle Resolve(NodeInfoAttribute info);   // merge: info.Color ?? cat.Color, info.Icon ?? cat.Icon
    public string   PortColorFor(Type t);               // rozbalí nullable/array; neznámý → AnyPortColor

    public NedTheme Set(string category, string color, string icon);
    public NedTheme SetPortColor(string typeName, string color);
    public NedTheme SetFallback(string color, string icon);

    public static NedTheme CreateDefault();
    public static NedTheme LoadFromEmbedded();          // používá NedOptions ve výchozím stavu
    public static NedTheme LoadFromJson(string json);   // merge přes builtin defaults
    public static NedTheme LoadFromFile(string path);
}
```

`Resolve` **mergeuje per-property**: explicitní `Color`/`Icon` z `[NodeInfo]` má přednost, jinak se vezme hodnota kategorie. Takže node může přepsat jen ikonu a barvu nechat z theme.

`NodeStyle` je `record (string Color, string Icon)`.

## DataNodeModel — univerzální model nad manifestem

Jeden model pro **všechny** typy uzlů (analogie UE `UK2Node_CallFunction` — žádné per-typ nody). Vstupy i výstup se čtou z deskriptoru v manifestu; hodnoty uzlu jsou pytel `Dictionary<string, object?>`, ne instance doménové třídy.

```csharp
public class DataNodeModel : NodeModel
{
    public NodeTypeDescriptor Descriptor { get; }
    public Dictionary<string, object?> Values { get; }
    public List<NodeInput> InputDefs { get; }                  // všechny vstupy (port i field režim)
    public Dictionary<string, NodeInput> Inputs { get; }       // port.Id → vstup, jen aktivní porty
    public Dictionary<string, TypedPortModel> Outputs { get; } // name → pravý port; prázdné u sinku
    public string OutputType { get; }

    public void SetExposure(NodeInput input, bool asPort);     // přepnutí port↔pole (viz doc 12)
    public void RefreshDynamicTypes();                         // znovu čte InputTypeName u GraphInputNode
    public void SyncDeclaredInputs(IReadOnlyList<GraphOutput> outputs); // Output/Return porty
}
```

`NodeInput` reprezentuje jeden manifestový nebo dynamicky deklarovaný vstup. `DefaultAsPort` vychází
z `InputKind`, ale `exec` a `any` jsou port vždy. `Togglable` je false pro `exec`, `any`, `Multiple`,
type-picker a `Details`; `Details` se u `exec` také vynutí na false. Výstupní typy běžných uzlů
pocházejí z descriptoru, `GraphInputNode` čte zvolený typ přes `ResolveOutputType()` a sinky staví
porty z grafových deklarací. Detail přepínání vstupů: [12-ports-and-fields.md](12-ports-and-fields.md).

### Porty — pevný směr

- **Input = vlevo** (`PortAlignment.Left`), generovaný z `[NodePort]`/`[NodeField]`
- **Output = vpravo** (`PortAlignment.Right`), jeden nebo více pojmenovaných portů, žádný u sink nodů

Žádný `PortSide` enum — směr je implicitní a neměnný.

## DataNodeWidget.razor — rendering z deskriptoru

Jeden widget vykreslí jakýkoli `DataNodeModel`:

```
┌──────────────────────────────┐
│ ∑ Add               [header]  │  ← barva/ikona z NedTheme(.Resolve)
├──────────────────────────────┤
│ ● A                        ○ │  ← ● vstupní porty (barva z typu), ○ výstup
│ ● B                          │
├──────────────────────────────┤
│ Value: [10____]              │  ← vstupy ve field režimu (inline input)
└──────────────────────────────┘
```

- Header: `NedTheme.Resolve(Node.Info)` (barva + ikona), se spodním gradient stripem laděným do barvy výstupu.
- Vstupy v **port** režimu → řádek s portem (barva `PortColorFor`) a popiskem; v **field** režimu → inline editor (`NodeFieldInput`: text/number/checkbox/select/type-picker/complex dropdown).
- Pravý klik na přepínatelný vstup → kontext menu port↔pole (přes `NodeEditorBridge`).

## Built-in nody (v Core, ne v doméně)

### OutputNode a ReturnNode — návratové body

```csharp
[NodeInfo("Output", Category = "Output", Color = "#8844aa", Icon = "➤")]
[NodeSink]
public sealed class OutputNode : IGraphData { }

[NodeInfo("Return", Category = "Flow", Color = "#3758CC", Icon = "↩")]
[NodeSink]
public sealed class ReturnNode : IGraphData
{
    [NodePort("In", Id = "$exec", Multiple = true)] public Exec? In { get; set; }
}
```

- `GraphSettings.Outputs` je zdroj pravdy pro jméno, type id, pořadí a aritu návratů.
- Datový graf má právě jeden `OutputNode`; ten z deklarací vytvoří všechny vstupní porty a export
  táhne hodnoty zpět od tohoto sinku.
- Exec graf používá libovolný počet `ReturnNode`; každý má exec vstup a stejné dynamické hodnotové
  porty. Konkrétní větev tak vrátí své hodnoty v okamžiku, kdy na Return dorazí řízení.
- Stabilní `GraphOutput.Id` drží identitu portu při přejmenování nebo změně typu. Odebrání deklarace
  odstraní odpovídající port i jeho linky.

### GraphInputNode — parametr subgrafu

Built-in `IGraphData` (kategorie „Interface"), dostupný v datovém i exec toku. Detail parametrů
vkládaného grafu je v [10-subgraphs.md](10-subgraphs.md), exec parametrů v
[15-exec-graphs.md](15-exec-graphs.md).

## Registrace widgetů a plátno

Každý tab má vlastní `BlazorDiagram` a registruje **oba** widgety:

```csharp
Diagram.RegisterComponent<DataNodeModel, DataNodeWidget>();
Diagram.RegisterComponent<SubgraphNodeModel, SubgraphNodeWidget>();
```

`NedCanvas` je drop-in komponenta, kterou hostitel umístí do stránky:

```razor
<NedCanvas OnWriteFile="..." OnSaveAsRequested="..." OnLoadRequested="..."
           OnExportRequested="..." OnPickFolderRequested="..." ... />
```

Veškeré file I/O a OS-specifické akce řeší host přes callbacky — Core neví o WPF/OS. Kompletní seznam callbacků a UX viz [11-editor-ux.md](11-editor-ux.md).

## Kdy psát vlastní node/widget (extension point)

Generický `DataNodeModel` + `DataNodeWidget` pokryje 90 %+ typů. Vlastní node se píše, když porty **nepochází z manifestu** nebo je potřeba nestandardní layout. Mechanismus = **vlastní `NodeModel` + vlastní widget + `RegisterComponent`**, přesně jako to dělá `SubgraphNodeModel`/`SubgraphNodeWidget` (porty staví z dat). Žádný `[CustomNodeWidget]` atribut neexistuje — je to plnohodnotná ZBD registrace.
