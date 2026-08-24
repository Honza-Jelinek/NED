# Editor UX — plátno, taby, picker, undo, zkratky

`NedCanvas` je celý editor jako **jedna drop-in komponenta**. Skládá se z menu baru, lišty záložek, knihovního panelu a plátna, plus plovoucích overlayů (picker, kontextová menu). Stav i logika žijí v `NedCanvas.razor.cs`; vizuální části jsou samostatné komponenty. Veškeré file I/O a OS-specifické akce řeší **host přes callbacky** — Core neví o WPF/OS.

```
┌─ NedMenuBar ──────────────────────────────── status ─┐
├─ LibraryPanel ─┬─ NedTabBar ───────────────────────── ┤
│  (knihovny)    │  ◆ Graph1   ⧉ AddTwo   …             │
│                ├──────────────────────────────────────┤
│                │  NedHomeScreen  /  DiagramCanvas      │
└────────────────┴──────────────────────────────────────┘
   + overlaye: NodePicker, NodeContextMenu, PortContextMenu, box-select
```

## Záložky (taby)

Každý otevřený graf je `EditorTab` s **vlastním** `BlazorDiagram`, `GraphSettings`, `UndoManager` a cestou k souboru:

```csharp
public sealed class EditorTab
{
    public int Index { get; }                 // pořadí (pro "Untitled N")
    public BlazorDiagram Diagram { get; }      // vlastní plátno, registruje oba widgety
    public GraphSettings Settings { get; set; }
    public UndoManager Undo { get; }
    public string? FilePath { get; set; }      // null = nový, neuložený
    public int Added { get; set; }             // počet přidaných nodů (positioning)
    public string Title { get; }               // Settings.Name → název souboru → "Untitled N"
}
```

Přepnutí tabu remountuje `DiagramCanvas` (`@key="Active"`), aby ZBD bindlo na diagram daného tabu a necacheovalo první. Otevřít už otevřený soubor → jen přepne na jeho záložku. Bez aktivního tabu se zobrazí **home screen** (New / Open).

> **Detail:** první `DiagramCanvas` v životě appky měří porty dřív, než se ustálí layout (fonty + home→canvas přechod) → linky by „visely". Fix: `ned-keys.js#whenLayoutStable()` počká na fonty + 2 animation frames, pak se canvas jednou remountuje (`_canvasGen++`) → ZBD re-observe portů. Další grafy už jsou OK.

## Menu bar a status

`NedMenuBar` (MudBlazor `MudAppBar`):

- **File**: New · Open · Save · Save As… · Export · Manage Libraries
- **Edit**: Undo · Redo
- **status** vpravo: `{Flow} • Outputs: {jméno: typ[]} • Nodes: N Links: M`

`New` otevře `NewGraphDialog` s volbou Data/Exec. Datový graf vybere typ první deklarace `Result`
a založí jediný `OutputNode`; exec graf začíná bez návratů a s jediným `ExecEntry`. Další návratové
deklarace, tok a příznak `Instanceable` se upravují v grafovém Details panelu. Save/Export jsou
disabled, dokud není aktivní graf.

## Node picker (místo levé palety)

Plovoucí vyhledávač nodů, dvě cesty otevření:

1. **Pravý klik na prázdno** → picker bez filtru (všechny nody + subgrafy z knihoven).
2. **Vytažení linku z portu a puštění na prázdno** → picker **filtrovaný podle typu portu**, s **auto-propojením**:
   - zdroj je výstup (produkuje `T`) → nabídne jen nody, které `T` přijmou (`InputPortTypes` kompatibilní), a po výběru je rovnou napojí;
   - zdroj je vstup (chce `T`) → nabídne jen nody, které `T` produkují.

Picker (`NodePicker`) má fulltext (`MudNavMenu` seskupený dle kategorií + sekce „Subgraphs"),
navigaci šipkami ↑/↓ a Enter. `ned-keys.js` zajišťuje `clampPicker` (drží popup ve viewportu),
`focusSearch` a `scrollHighlight`. `GraphInputNode` je dostupný v obou tocích. `OutputNode` je
singleton datového toku, `ExecEntry` singleton exec toku a `ReturnNode` se nabízí jen v exec toku;
při tažení z exec pinu se sink Return nevyřadí jen proto, že nemá výstup. Právě editovaný graf se
vyloučí z nabídky vkládaných grafů.

## Kontextová menu

- **Node** (pravý klik na node): Duplikovat · Refresh · Smazat.
  - *Duplikovat* zkopíruje skalární hodnoty vstupů + field-mode výběry + override expozice s offsetem.
  - *Refresh* přestaví `SubgraphNodeModel` z aktuálního rozhraní, u `DataNodeModel` přečte dynamické typy.
- **Vstup** (pravý klik na přepínatelný port/pole): Převést na pole ↔ Převést na port. Detail [12-ports-and-fields.md](12-ports-and-fields.md).
- **Asset** (pravý klik v knihovním panelu): viz [13-libraries-and-assets.md](13-libraries-and-assets.md).

## Výběr a single-input

- **Box-select (Shift+drag na prázdnu)**: rubber-band přes plátno; ZBD pan startuje jen bez Shiftu, takže Shift je volný. Vybere protnuté nody.
`LinkConstraints` po připojení aplikuje tři pravidla: nemultiple vstup drží nejvýš jeden link,
exec výstup drží nejvýš jedno pokračování a dva linky mezi týmž párem portů se zkolabují na jeden.
Nově připojený link vždy vyhraje nad starým.

## Undo / Redo

`UndoManager` je **snapshot-based**: snapshot = `Serialize(ToDocument(diagram, settings))`, restore = `LoadInto(...)` (postaveno na [04-persistence.md](04-persistence.md)). Tím pokrývá **všechny** operace (add/remove/move/field/exposure) bez per-akce kódu. Per-tab historie, limit 100.

Záznam stavu:
- mutace přes UI (smazání, duplikace, přidání z pickeru, změna pole, přepnutí expozice) volají `RecordUndo()` **před** změnou (přes `NodeEditorBridge.RecordUndo`);
- drag nodu: `Snapshot` na `PointerDown` (před pohybem), `Commit` na `Moved` (po pohybu) — takže se uloží stav před tažením.

Zkratky: `Ctrl+Z` undo, `Ctrl+Shift+Z` / `Ctrl+Y` redo.

## Klávesové zkratky

`ned-keys.js` registruje **document-level** `keydown` listener (capture), nezávislý na fokusu, a potlačí nativní akce WebView (`Ctrl+S` = uložit stránku). Volá zpět `[JSInvokable] HandleShortcut`:

| Zkratka | Akce |
|---|---|
| `Ctrl+S` | Save (quick-save na známou cestu, jinak Save As) |
| `Ctrl+Shift+S` | Save As |
| `Ctrl+Z` | Undo |
| `Ctrl+Shift+Z` / `Ctrl+Y` | Redo |

## Host callbacky (parametry `NedCanvas`)

Core deleguje vše OS-specifické na hosta. `NED.Shell.Wpf` to řeší WPF dialogy a `Process.Start`:

| Callback | Účel |
|---|---|
| `OnWriteFile(path, json)` | quick-save zápis na známou cestu (bez dialogu) |
| `OnSaveAsRequested(json) → path?` | Save As dialog, vrátí zvolenou cestu |
| `OnLoadRequested() → (json, path)?` | Open dialog |
| `OnExportRequested(json)` | Export dialog (uloží čistý JSON) |
| `OnPickFolderRequested() → path?` | výběr knihovní složky |
| `OnRevealInExplorer(path)` | označit soubor v Průzkumníku |
| `OnOpenInEditor(path)` | otevřít v externím editoru (VS Code) |
| `OnDeleteFile(path) → bool` | smazat soubor (do koše) |

Pokud host callback nedodá, akce se prostě neprovede (fallbacky jsou best-effort, např. `File.Delete` místo koše). Vizuální theming je MudBlazor dark palette definovaná v `NedCanvas.razor.cs`.
