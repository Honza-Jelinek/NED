# NED.Core

Doménově nezávislé jádro editoru NED. Jde o Razor Class Library pro `net10.0`, která z anotovaných `IGraphData` tříd vytváří editovatelné uzly a poskytuje celý editor jako komponentu `NedCanvas`.

## Funkce

- načítání node packů z manifestů (`NedCatalog`) — bez reference na doménu;
- univerzální model a widget pro uzly, typované porty a validace propojení;
- centrální vzhled uzlů a portů (`NedTheme`);
- grafy, subgrafy, záložky, picker, kontextová menu, undo/redo a klávesové zkratky;
- ukládání a načítání `.nedgraph.json`, export čistých dat a správa knihoven assetů;
- lokalizace a komponenty založené na Blazor, MudBlazor a Z.Blazor.Diagrams.

## Integrace do hostitele

Hostitel zaregistruje služby NED a určí, které node packy načíst ([manifest](../../docs/14-manifest.md)):

```csharp
services.AddNed(options => options
    .Manifest(Path.Combine(AppContext.BaseDirectory, "manifests", "MyDomain.nodes.json"))
    .LibraryConfig(Path.Combine(appData, "MyEditor", "libraries.json"))
    .Style("Combat", "#a45c48", "⚔"));
```

Poté host umístí editor do Razor stránky. `NedCanvas` přijímá callbacky pro ukládání souborů a akce operačního systému, takže `NED.Core` nemusí záviset na WPF ani na konkrétní platformě.

```razor
<NedCanvas OnWriteFile="WriteFile"
           OnSaveAsRequested="SaveAs"
           OnLoadRequested="Load"
           OnExportRequested="Export" />
```

## Závislosti a hranice

Projekt referencuje `NED.Abstractions`, `MudBlazor` a `Z.Blazor.Diagrams`. Nesmí referencovat `Sandbox` ani jiný uživatelský node pack; jejich typy editor načítá z manifestů.

Podrobnosti: [engine](../../docs/03-ned-core-engine.md), [persistence](../../docs/04-persistence.md), [export](../../docs/05-export.md) a [UX editoru](../../docs/11-editor-ux.md).
