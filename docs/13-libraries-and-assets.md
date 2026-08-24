# Knihovny a assety — index, reference, file management

Aby šly subgrafy referencovat napříč soubory (a aby měl editor přehled o dostupných grafech), skenuje NED **knihovní složky** a buduje index assetů podle stabilního GUID. Tahle vrstva žije v `NED.Core/Assets/` a navazuje na ni knihovní panel + správa souborů v editoru.

## Identita assetu — GUID v souboru

Každý `.nedgraph.json` nese v `Settings.Id` **vložený GUID** (ne `.meta` sidecar). Přežije přejmenování i přesun — reference subgrafu drží GUID, ne cestu. Při duplikaci souboru se **musí** vygenerovat nový GUID (jinak by index považoval kopii za duplicitu; první nalezený vyhrává).

## AssetIndex

```csharp
public sealed class AssetIndex
{
    public event Action? Changed;                 // po každém rescanu / změně rootů (UI refresh)

    public IReadOnlyList<string> Roots { get; }
    public IReadOnlyList<AssetEntry> Entries { get; }
    public IEnumerable<AssetEntry> Subgraphs();    // všechny vkládatelné assety; tok filtruje picker
    public IEnumerable<AssetEntry> Templates();    // pouze Instanceable == true
    public AssetEntry? Resolve(Guid id);           // reference subgrafu; null = stale

    public void AddRoot(string path);              // + SaveRoots + Rescan
    public void RemoveRoot(string path);
    public void Rescan();                          // přečte všechny knihovny znovu
}
```

`Rescan` projde každý root rekurzivně, na každém `*.nedgraph.json` zavolá `TryRead` (odolné — poškozený soubor index nezboří) a postaví mapu `GUID → AssetEntry`. `Resolve` slouží `SubgraphNodeModel` k navázání reference.

```csharp
public sealed class AssetEntry
{
    public Guid Id;  public string Path;  public string Name;
    public GraphFlow Flow;  public bool Instanceable;
    public SubgraphInterface Interface;   // cache: vstupy + návratové deklarace
}
```

### Rozhraní se cachuje

`AssetIndex.BuildInterface` odvodí z těla souboru `SubgraphInterface` **bez** načítání celého grafu do diagramu:

```csharp
public sealed class SubgraphInput
{ public string Name; public string Type; public InputExposure Exposure; public string? Default; public int Order; }

public sealed class SubgraphOutput
{ public string Id; public string Name; public string Type; public bool Multiple; public int Order; }

public sealed class SubgraphInterface
{
    public GraphFlow Flow;
    public IReadOnlyList<SubgraphInput> Inputs;
    public IReadOnlyList<SubgraphOutput> Outputs;
}
```

Vstupy se čtou z `GraphInputNode` uzlů v souboru a řadí podle `Order`; návraty přímo ze
`Settings.Outputs`, kde pořadí určuje index v poli. Stabilní id, typ a arita návratu se přenesou do
výstupního portu. Díky cache postaví `SubgraphNodeModel` porty okamžitě — tělo grafu se načte až při
exportu nebo otevření k editaci. `SubgraphInterface.CanBePlacedIn()` povolí datový graf v obou
tocích, ale exec graf jen v exec toku.

## Persistence knihovních rootů

Seznam rootů i uživatelské zásahy do node packů se ukládají do workspace souboru, jehož cestu dodá
host přes `NedOptions.LibraryConfig(...)`. WPF shell používá `%AppData%/NED/workspace.json` a při
Výchozí WPF shell ukládá konfiguraci do `%AppData%/NED/libraries.json`. `null` cesta znamená pouze
in-memory konfiguraci.

## Hot reload

`AssetIndex.Changed` se vyvolá po každém rescanu. `NedCanvas` na něj reaguje
`RefreshSubgraphReferences()` — projde **všechny** taby a u každého `SubgraphNodeModel` zavolá
`RebuildFromInterface` z aktuálního indexu. Po uložení změněného grafu se tak parametry i návratové
porty referencí okamžitě synchronizují. Stabilní id návratové deklarace zachová link při přejmenování;
odebraná deklarace odstraní jen svůj port a linky. Knihovní panel se také překreslí.

## Manage Libraries dialog

`File → Manage Libraries` (`ManageLibrariesDialog`, MudBlazor): seznam rootů s možností odebrat, přidání cesty ručně nebo přes **Procházet…** (host `OnPickFolderRequested` → WPF folder dialog), tlačítko Přeskenovat, počitadlo indexovaných assetů/subgrafů. „Assets" složku serveru/jiné apky stačí přidat jako knihovnu.

Stejný dialog spravuje **Node packs**. Packy objevené hostitelem i ručně přidané manifesty lze
zapnout a vypnout; klíčem je cesta, ne pack id. Projekt lze vybrat nativním dialogem a předat
registrovanému `INodePackGeneratorProvider`. `.NET` provider podporuje `.csproj`, `.fsproj` a
`.vbproj`, spustí build mimo proces editoru a uloží obecný generační recept do workspace.

ManifestStore sleduje jednotlivé soubory s debounce. Změna manifestu nebo enable/disable atomicky
přestaví katalog a otevřené taby projdou bezztrátovým DTO round-tripem. Nezměněný dokument si
zachová dirty stav i undo historii; výběr, pan a zoom se obnoví.

## Knihovní panel

Levý sbalitelný panel (`LibraryPanel`): assety seskupené podle rootů (`MudNavGroup`), s ikonou toku
(datový / exec). Klik → otevře asset v tabu. Pravý klik → kontextové menu se souborovými operacemi:

| Akce | Co dělá | Host callback |
|---|---|---|
| Otevřít v Průzkumníku | označí soubor v Exploreru | `OnRevealInExplorer` |
| Otevřít ve VS Code | otevře v externím editoru | `OnOpenInEditor` |
| Přejmenovat | `TextPromptDialog` → přepíše `Settings.Name` uvnitř souboru + přesune soubor + aktualizuje otevřený tab | — |
| Duplikovat | kopie s **novým GUID** a unikátním názvem | — |
| Smazat | potvrzení → smaže (do koše) + zavře otevřený tab | `OnDeleteFile` |

Po každé operaci následuje `Rescan` + překreslení.

## Kdy se index obnovuje

- start (`LoadRoots` + `Rescan`),
- přidání/odebrání rootu,
- `Save` / `Save As` (nový/změněný asset se objeví),
- rename / duplicate / delete,
- ruční Přeskenovat.

## Vztah k subgrafům

Tahle vrstva je nosič referencí pro [10-subgraphs.md](10-subgraphs.md): picker bere subgrafy z `AssetIndex.Subgraphs()`, `SubgraphNodeModel` staví porty z `AssetEntry.Interface`, a export resolvuje tělo přes `AssetIndex.Resolve(guid)` + `Path`.
